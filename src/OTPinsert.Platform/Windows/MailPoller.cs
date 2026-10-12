using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using OTPinsert.Core;

namespace OTPinsert.Platform;

internal sealed class MailAuthenticationRequiredException : Exception
{
    internal MailAuthenticationRequiredException(Exception inner) : base(Localization.Text(TextId.OAuthSignInRequired), inner) { }
}

internal sealed class MailPoller
{
    private readonly TimeProvider time;

    private readonly OtpStore store;
    private readonly Action<LocalizedText> status;
    private uint? validity;
    private uint lastUid;
    internal MailPoller(OtpStore store, Action<LocalizedText> status, TimeProvider? time = null)
    {
        this.store = store;
        this.status = status;
        this.time = time ?? TimeProvider.System;
    }
    internal async Task RunAsync(string host, int port, string username, string password, bool startTls, CancellationToken stop, string? accountLabel = null, int pollingIntervalSeconds = 3, Func<ImapClient, CancellationToken, Task>? authenticate = null, string folderId = "INBOX")
    {
        if (pollingIntervalSeconds is < 1 or > 600)
        {
            throw new ArgumentOutOfRangeException(nameof(pollingIntervalSeconds));
        }

        using var network = new NetworkReconnectSignal();
        var failures = 0;
        while (!stop.IsCancellationRequested)
        {
            var recovery = network.Capture();
            try
            {
                using var client = new ImapClient { Timeout = 15_000 };
                await client.ConnectAsync(host, port, startTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect, stop);
                if (authenticate is null)
                {
                    await client.AuthenticateAsync(username, password, stop);
                }
                else
                {
                    await authenticate(client, stop);
                }

                var inbox = await MailFolders.OpenImapFolderAsync(client, folderId, stop);
                if (validity != inbox.UidValidity)
                {
                    validity = inbox.UidValidity;
                    // Baseline: existing messages are not surfaced when the app starts.
                    lastUid = inbox.UidNext.HasValue ? inbox.UidNext.Value.Id - 1 : (await inbox.SearchAsync(SearchQuery.All, stop)).LastOrDefault().Id;
                }
                failures = 0;
                status(new(TextId.MailConnected, pollingIntervalSeconds));
                while (!stop.IsCancellationRequested && client.IsConnected)
                {
                    await client.NoOpAsync(stop);
                    if (lastUid < uint.MaxValue)
                    {
                        var afterUid = lastUid;
                        var range = new UniqueIdRange(new UniqueId(afterUid + 1), UniqueId.MaxValue);
                        var uids = await inbox.SearchAsync(SearchQuery.Uids(range), stop);
                        // IMAP n:* can include the highest existing UID even when it is below n.
                        await ProcessNewMessagesAsync(uids, async uid =>
                        {
                            var summaries = await inbox.FetchAsync(new[] { uid }, MessageSummaryItems.InternalDate | MessageSummaryItems.Size, stop);
                            var summary = summaries.FirstOrDefault();
                            if (summary?.InternalDate is { } received && MailContent.IsRecent(received, store.Countdown, time) && summary.Size <= MailContent.MaximumMessageSize)
                            {
                                using var message = await inbox.GetMessageAsync(uid, stop);
                                var code = MailContent.ExtractCode(message);
                                if (code is not null)
                                {
                                    var source = MailContent.GetSource(message, accountLabel);
                                    stop.ThrowIfCancellationRequested();
                                    store.Add(code, source, received);
                                    status(new(TextId.NewEmailCodeDetected));
                                }
                            }
                        }, stop);
                    }
                    await Task.Delay(TimeSpan.FromSeconds(pollingIntervalSeconds), stop);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (MailAuthenticationRequiredException)
            {
                status(new(TextId.OAuthSignInRequired));
                break;
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.Net.Sockets.SocketException or TimeoutException or OperationCanceledException
                or MailKit.ProtocolException or MailKit.CommandException
                or MailKit.Security.AuthenticationException or System.Security.Authentication.AuthenticationException)
            {
                failures = Math.Min(failures + 1, 10);
                // Never expose protocol transcripts, message contents or credentials in logs.
                try
                {
                    await NetworkReconnectSignal.WaitWithCountdownAsync(recovery, Math.Min(30, 3 * failures),
                        seconds => status(new(TextId.EmailConnectionFailedRetrying, ex.GetType().Name, seconds)), stop);
                    status(new(TextId.StartingConnection));
                }
                catch (OperationCanceledException) { break; }
            }
            catch (Exception ex)
            {
                AppDiagnostics.ReportFailure("MailPolling.Run", ex);
                // Unexpected failures must not become an endless reconnect loop.
                status(new(TextId.MailMonitoringFailed, ex.GetType().Name));
                break;
            }
        }
    }
    // Only deterministic content failures are skipped. Transport errors and cancellation
    // escape without advancing the cursor, so an interrupted download is retried.
    internal async Task ProcessNewMessagesAsync(System.Collections.Generic.IEnumerable<UniqueId> uids, Func<UniqueId, Task> process, CancellationToken stop)
    {
        foreach (var uid in NewUids(uids, lastUid))
        {
            stop.ThrowIfCancellationRequested();
            string? skipped = null;
            try
            {
                await process(uid);
            }
            catch (Exception ex) when (ex is FormatException or ParseException or RegexMatchTimeoutException)
            {
                skipped = ex.GetType().Name;
            }
            stop.ThrowIfCancellationRequested();
            lastUid = uid.Id;
            if (skipped is not null)
            {
                status(new(TextId.MailMessageSkipped, skipped));
            }
        }
    }
    internal static UniqueId[] NewUids(System.Collections.Generic.IEnumerable<UniqueId> uids, uint afterUid)
        => uids.Where(uid => uid.Id > afterUid).Distinct().OrderBy(uid => uid.Id).ToArray();
}
