using Xunit;
using OTPcue.Core;
using OTPcue.Platform;
using MailKit;
using MimeKit;
using System.Text.RegularExpressions;

public sealed class MailProcessingTests
{
    private static void Check(bool condition, string label) => Assert.True(condition, label);

    [Fact]
    public async Task SocketFailureRetriesUntilCanceled()
    {
        // Reserve a local port without listening so connections are refused deterministically.
        using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        var port = ((System.Net.IPEndPoint)socket.LocalEndPoint!).Port;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var statuses = new List<LocalizedText>();
        var poller = new MailPoller(new OtpStore(), message =>
        {
            if (message.Id != TextId.EmailConnectionFailedRetrying)
            {
                return;
            }
            statuses.Add(message);
            if (Equals(message.Arguments[1], 6))
            {
                stop.Cancel();
            }
        });

        await poller.RunAsync("127.0.0.1", port, "test", "test", false, stop.Token);

        Assert.Equal(3, statuses[0].Arguments[1]);
        Assert.Equal(6, statuses[^1].Arguments[1]);
        Assert.All(statuses, message => Assert.Equal(
            nameof(System.Net.Sockets.SocketException), message.Arguments[0]));
    }

    [Fact]
    public void ReceiveAgeAndClockSkewBoundaries()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
        var countdown = TimeSpan.FromSeconds(60);
        Check(MailContent.IsRecent(clock.Now - countdown, countdown, clock), "Receive-age boundary is inclusive");
        Check(!MailContent.IsRecent(clock.Now - countdown - TimeSpan.FromTicks(1), countdown, clock), "Expired mail is rejected deterministically");
        Check(MailContent.IsRecent(clock.Now.AddMinutes(1), countdown, clock), "Clock-skew boundary is inclusive");
        Check(!MailContent.IsRecent(clock.Now.AddMinutes(1).AddTicks(1), countdown, clock), "Future mail outside clock-skew tolerance is rejected");
    }

    [Fact]
    public void ExtractsPlainAndHtmlWithoutBacktrackingStarvation()
    {
        using var valid = new MimeMessage { Subject = "Verification code", Body = new TextPart("plain") { Text = "Your verification code is 123456." } };
        using var html = new MimeMessage { Body = new TextPart("html") { Text = "<p>Your verification code is <b>654321</b>.</p>" } };
        using var malformed = new MimeMessage { Body = new TextPart("html") { Text = new string('<', 100_000) } };
        Check(MailContent.ExtractCode(valid) == "123456" && MailContent.ExtractCode(html) == "654321", "Plain and HTML OTP extraction preserved");
        Check(MailContent.ExtractCode(malformed) is null, "Unmatched HTML delimiters cannot trigger backtracking starvation");
    }

    [Fact]
    public void ExtractsIndentedQuotedPrintableLoginEmail()
    {
        var raw = "From: Service <noreply@example.test>\r\n" +
            "Subject: Dein =?UTF-8?Q?tempor=C3=A4rer_Anmeldecode_f=C3=BCr?= Service\r\n" +
            "MIME-Version: 1.0\r\nContent-Type: text/html; charset=iso-8859-1\r\n" +
            "Content-Transfer-Encoding: quoted-printable\r\n\r\n" +
            "<p>Gib diesen tempor=E4ren Verifizierungscode ein, um fortzufahren:</p>\r\n" +
            new string(' ', 80) + "<p>\r\n" +
            "<!--[if mso]>\r\n<span style=3D\"font-family: Lucida Console\">\r\n<![endif]-->\r\n" +
            new string(' ', 80) + "123456\r\n<!--[if mso]></span><![endif]-->\r\n</p>";
        using var stream = new System.IO.MemoryStream(System.Text.Encoding.ASCII.GetBytes(raw));
        using var message = MimeMessage.Load(stream);
        Assert.Equal("123456", MailContent.ExtractCode(message));
    }

    [Fact]
    public void HtmlCommentsCannotSupplyCodeCandidates()
    {
        using var message = new MimeMessage
        {
            Body = new TextPart("html") { Text = "<p>Verification code <!-- 654321 -->123456</p>" }
        };
        Assert.Equal("123456", MailContent.ExtractCode(message));
    }

    [Fact]
    public void SenderLabelIsSanitizedAndBounded()
    {
        using var valid = new MimeMessage { Subject = "Verification code", Body = new TextPart("plain") { Text = "Your verification code is 123456." } };
        valid.From.Add(new MailboxAddress("Service\n" + new string('x', 100), "service@example.test"));
        var source = MailContent.GetSource(valid, "Privat");
        Check(source == "Service" + new string('x', 73) + " · Privat", "Sender label removes control characters and limits length");
        using var anonymous = new MimeMessage();
        Check(MailContent.GetSource(anonymous, null) == "Unbekannt", "Missing sender has a safe display fallback");
    }

    [Fact]
    public async Task MalformedContentDoesNotBlockLaterMessages()
    {
        using var valid = new MimeMessage { Subject = "Verification code", Body = new TextPart("plain") { Text = "Your verification code is 123456." } };
        var errors = new Exception[] { new FormatException("PRIVATE mail content"), new ParseException("PRIVATE mail content", 0, 0), new RegexMatchTimeoutException("PRIVATE mail content", "pattern", TimeSpan.FromMilliseconds(100)) };
        foreach (var error in errors)
        {
            var store = new OtpStore();
            var statuses = new List<string>();
            var poller = new MailPoller(store, message => statuses.Add(message.ToString()));
            var seen = new List<uint>();
            var uids = new[] { new UniqueId(43), new UniqueId(42) };
            await poller.ProcessNewMessagesAsync(uids, uid =>
            {
                seen.Add(uid.Id);
                if (uid.Id == 42)
                {
                    return Task.FromException(error);
                }

                store.Add(MailContent.ExtractCode(valid)!, "Test", DateTimeOffset.UtcNow);
                return Task.CompletedTask;
            }, CancellationToken.None);
            Check(seen.SequenceEqual(new uint[] { 42, 43 }) && store.Latest()?.Code == "123456", error.GetType().Name + " does not block later OTP");
            await poller.ProcessNewMessagesAsync(uids, uid => { seen.Add(uid.Id); return Task.CompletedTask; }, CancellationToken.None);
            Check(seen.Count == 2, "Skipped and successful UIDs are not retried");
            Check(statuses.Count == 1 && !statuses[0].Contains("PRIVATE"), "Skipped-mail status contains no message content");
        }
    }

    [Fact]
    public async Task TransportFailurePreservesCursorForRetry()
    {
        foreach (var error in new Exception[] { new System.IO.IOException("network interruption"), new MailKit.Net.Imap.ImapProtocolException("protocol interruption"), new OperationCanceledException() })
        {
            var poller = new MailPoller(new OtpStore(), _ => { });
            var seen = new List<uint>();
            var uids = new[] { new UniqueId(42), new UniqueId(43) };
            try
            {
                await poller.ProcessNewMessagesAsync(uids, uid => { seen.Add(uid.Id); return Task.FromException(error); }, CancellationToken.None);
                throw new Exception("Expected transport/cancellation failure");
            }
            catch (Exception ex) when (ReferenceEquals(ex, error)) { }
            Check(seen.SequenceEqual(new uint[] { 42 }), "Transport/cancellation failure stops current batch");
            await poller.ProcessNewMessagesAsync(uids, uid => { seen.Add(uid.Id); return Task.CompletedTask; }, CancellationToken.None);
            Check(seen.SequenceEqual(new uint[] { 42, 42, 43 }), "Interrupted UID is retried without dropping later mail");
        }
    }

    [Fact]
    public async Task CancellationBeforeCommitPreservesCursor()
    {
        using var stop = new CancellationTokenSource();
        var canceledPoller = new MailPoller(new OtpStore(), _ => { });
        try
        {
            await canceledPoller.ProcessNewMessagesAsync(new[] { new UniqueId(42) }, _ => { stop.Cancel(); return Task.CompletedTask; }, stop.Token);
            throw new Exception("Expected cancellation before cursor commit");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        var retried = false;
        await canceledPoller.ProcessNewMessagesAsync(new[] { new UniqueId(42) }, _ => { retried = true; return Task.CompletedTask; }, CancellationToken.None);
        Check(retried, "Cancellation before cursor commit preserves UID for retry");
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now { get; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
