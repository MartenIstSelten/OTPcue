using System.Net.Http;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2.Responses;
using OTPcue.Core;
using OTPcue.Platform;
using Xunit;

public sealed class GoogleOfflineTests
{
    private static MailAccount CreateAccount() => new MailAccount(Guid.NewGuid(), "Privat", "gmail.googleapis.com", 443, "test@example.test", "", false, true)
    {
        Provider = MailProvider.Google,
        OAuthClientId = GoogleOAuthConfiguration.ClientId,
        OAuthClientSecret = GoogleOAuthConfiguration.ClientSecret
    };

    private static void Check(bool condition, string label) => Assert.True(condition, label);

    [Fact]
    public void GmailFolderNamesAreDistinctFromStableIds()
    {
        using var json = JsonDocument.Parse("""
            {"labels":[{"id":"Label_42","name":"Bestätigungen/Privat"},{"id":"INBOX","name":"INBOX"}]}
            """);
        var labels = MailFolders.GoogleLabels(json.RootElement);
        Assert.Equal("INBOX", labels[0].Id);
        Assert.Equal(new MailFolder("Label_42", "Bestätigungen/Privat"), labels[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedLabelFiltersNewMessagesAndLabelAssignments(bool labelEvent)
    {
        const string selected = "Label_42";
        var store = new OtpStore();
        var calls = new List<string>();
        var changes = new[]
        {
            new { message = new { id = "selected", labelIds = new[] { selected } }, labelIds = new[] { selected } },
            new { message = new { id = "inbox", labelIds = new[] { "INBOX" } }, labelIds = new[] { "INBOX" } }
        };
        var history = JsonSerializer.Serialize(new
        {
            historyId = "200",
            history = new[] { new Dictionary<string, object> { [labelEvent ? "labelsAdded" : "messagesAdded"] = changes } }
        });
        var poller = new GmailPoller(store, _ => { }, (path, token, stop) =>
        {
            calls.Add(path);
            return Task.FromResult(JsonDocument.Parse(path.StartsWith("history?") ? history : Message("123456", label: selected)));
        });
        Assert.Equal("200", await poller.PollOnceAsync("100", "TEST-token", "Privat", CancellationToken.None, selected));
        Assert.Contains("labelId=Label_42&", calls[0]);
        Assert.Contains("historyTypes=labelAdded", calls[0]);
        Assert.DoesNotContain(calls, path => path.Contains("messages/inbox"));
        Assert.Equal("123456", store.Latest()!.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedLabelIsRecheckedBeforeAndAfterDownloading(bool removedDuringDownload)
    {
        var store = new OtpStore();
        var calls = new List<string>();
        var history = History(["guard"]).Replace("INBOX", "Label_42");
        var poller = new GmailPoller(store, _ => { }, (path, token, stop) =>
        {
            calls.Add(path);
            var label = removedDuringDownload && path.Contains("format=metadata") ? "Label_42" : "INBOX";
            return Task.FromResult(JsonDocument.Parse(path.StartsWith("history?") ? history : Message("123456", label: label)));
        });
        await poller.PollOnceAsync("100", "TEST-token", "Privat", CancellationToken.None, "Label_42");
        Assert.Null(store.Latest());
        Assert.Equal(removedDuringDownload, calls.Any(path => path.Contains("format=raw")));
    }

    private static string Message(string code, long? received = null, long size = 100, string rawOverride = "", string label = "INBOX")
    {
        var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes("From: Service <service@example.test>\r\nSubject: Verification code\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nYour verification code is " + code)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return JsonSerializer.Serialize(new
        {
            labelIds = new[] { label },
            sizeEstimate = size,
            internalDate = (received ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).ToString(),
            raw = rawOverride == "" ? raw : rawOverride
        });
    }
    private static string History(string[] ids, string? page = null, string cursor = "200") => JsonSerializer.Serialize(new
    {
        historyId = cursor,
        nextPageToken = page,
        history = new[] { new { messagesAdded = ids.Select(id => new { message = new { id, labelIds = new[] { "INBOX" } } }).ToArray() } }
    });

    [Fact]
    public void SignInErrorsAreActionableAndRedacted()
    {
        var account = CreateAccount();
        account.Validate();
        var missingSecret = new TokenResponseException(new TokenErrorResponse { Error = "invalid_request", ErrorDescription = "client_secret is missing." });
        Check(GoogleMailAuth.DescribeSignInFailure(missingSecret).Contains("client_secret"), "callback completion followed by a missing desktop secret produces an actionable error");
        var sensitiveError = new TokenResponseException(new TokenErrorResponse { Error = "invalid_client", ErrorDescription = "TEST-secret TEST-token TEST-code" });
        Check(!GoogleMailAuth.DescribeSignInFailure(sensitiveError).Contains("TEST-"), "sign-in errors never display raw credential or authorization-code details");
    }

    [Fact]
    public void AuthorizationUsesFreshPkceAndNoClientSecret()
    {
        var account = CreateAccount();
        using (var flow = GoogleMailAuth.CreateFlow(account))
        {
            var uri = flow.CreateAuthorizationCodeRequest("http://127.0.0.1:4321/", out var verifier).Build().AbsoluteUri;
            var challenge = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Check(uri.Contains("code_challenge_method=S256") && uri.Contains("code_challenge=" + challenge), "production OAuth flow uses S256 PKCE with the shared desktop configuration");
            Check(!uri.Contains("client_secret") && !uri.Contains(account.OAuthClientSecret), "desktop client secret is never included in the browser authorization URL");
            flow.CreateAuthorizationCodeRequest("http://127.0.0.1:4321/", out var another);
            Check(verifier != another, "every authorization uses a fresh PKCE verifier");
        }
    }

    [Fact]
    public void AccountConfigurationRoundtripsAndRejectsUnsafeEndpoints()
    {
        var account = CreateAccount();
        Check(JsonSerializer.Deserialize<MailAccount>(JsonSerializer.Serialize(account)) == account, "Google account configuration survives serialization");
        foreach (var bad in new[] { account with { Host = "other.test" }, account with { Port = 993 }, account with { StartTls = true }, account with { Password = "password" }, account with { OAuthClientId = "" } })
        {
            var rejected = false;
            try
            {
                bad.Validate();
            }
            catch (System.IO.InvalidDataException) { rejected = true; }
            Check(rejected, "Google cannot send OAuth credentials to a custom mail server");
        }
    }

    [Fact]
    public void TokenMetadataAndProviderValuesRemainCompatible()
    {
        var token = new TokenResponse { AccessToken = "TEST-access", RefreshToken = "TEST-refresh", ExpiresInSeconds = 3600, IssuedUtc = DateTime.UtcNow };
        var restored = JsonSerializer.Deserialize<TokenResponse>(JsonSerializer.Serialize(token))!;
        Check(restored.RefreshToken == token.RefreshToken && restored.IssuedUtc == token.IssuedUtc && restored.ExpiresInSeconds == token.ExpiresInSeconds, "token refresh metadata survives local cache serialization");
        Check((int)MailProvider.Gmail == 1 && (int)MailProvider.Google == 4, "existing provider values remain compatible");
    }

    [Fact]
    public async Task InterruptedHistoryRetriesWithoutReplayingMessages()
    {
        var store = new OtpStore();
        var logs = new List<string>();
        var calls = new List<string>();
        var failSecond = true;
        var response = new Dictionary<string, string> { ["one"] = Message("123456"), ["two"] = Message("654321") };
        Task<JsonDocument> Request(string path, string bearer, CancellationToken stop)
        {
            stop.ThrowIfCancellationRequested();
            calls.Add(path);
            if (path.StartsWith("history?"))
            {
                return Task.FromResult(JsonDocument.Parse(path.Contains("pageToken=") ? History(["two"]) : History(["one", "one"], "next page")));
            }

            if (path.Contains("/two?") && failSecond)
            {
                throw new HttpRequestException("synthetic transport failure");
            }

            var id = path.Split('/')[1].Split('?')[0];
            return Task.FromResult(JsonDocument.Parse(response[id]));
        }
        var poller = new GmailPoller(store, message => logs.Add(message.ToString()), Request);
        var failed = false;
        try
        {
            await poller.PollOnceAsync("100", "TEST-token", "Privat", CancellationToken.None);
        }
        catch (HttpRequestException) { failed = true; }
        Check(failed && store.Latest()?.Code == "123456", "transport failure does not commit later history pages");
        store.Remove(store.Latest()!.Id);
        failSecond = false;
        Check(await poller.PollOnceAsync("100", "TEST-token", "Privat", CancellationToken.None) == "200", "all pages succeed before cursor advances");
        Check(calls.Count(p => p.Contains("/one?") && p.Contains("format=raw")) == 1, "successful messages are not replayed after a failed page");
        Check(store.Latest()?.Code == "654321" && store.Latest()?.Source == "Service · Privat", "raw Gmail message feeds existing OTP detection");
        Check(calls.Any(p => p.Contains("pageToken=next%20page")), "page token is URL encoded");
        Check(!logs.Any(s => s.Contains("TEST-token") || s.Contains("654321")), "status contains no tokens or codes");
    }

    [Fact]
    public async Task UnsuitableMessagesCannotProduceCodes()
    {
        foreach (var sample in new[] { Message("888888", DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds()), Message("888888", size: 1_000_001), Message("888888", label: "SPAM"), Message("888888", rawOverride: "!bad!") })
        {
            var isolated = new OtpStore();
            var guarded = new GmailPoller(isolated, _ => { }, (p, t, c) => Task.FromResult(JsonDocument.Parse(p.StartsWith("history?") ? History(["guard"]) : sample)));
            Check(await guarded.PollOnceAsync("100", "TEST-token", "Privat", CancellationToken.None) == "200" && isolated.Latest() is null, "old, oversized, non-INBOX or malformed message cannot produce an OTP");
        }
    }

    [Fact]
    public async Task UnsuitableMetadataPreventsRawDownload()
    {
        foreach (var sample in new[] { Message("888888", size: 2_000_001), Message("888888", DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds()), Message("888888", label: "SPAM") })
        {
            var store = new OtpStore();
            Task<JsonDocument> Request(string path, string token, CancellationToken stop)
            {
                Assert.DoesNotContain("format=raw", path);
                return Task.FromResult(JsonDocument.Parse(path.StartsWith("history?") ? History(["guard"]) : sample));
            }
            var poller = new GmailPoller(store, _ => { }, Request);
            Assert.Equal("200", await poller.PollOnceAsync("100", "TEST-token", "Privat", CancellationToken.None));
            Assert.Null(store.Latest());
        }
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("raw")]
    public async Task OversizedResponseDoesNotBlockFollowingCode(string oversizedFormat)
    {
        var store = new OtpStore();
        var logs = new List<string>();
        using var client = new HttpClient(new OversizedResponseHandler());
        async Task<JsonDocument> Request(string path, string token, CancellationToken stop)
        {
            if (path.StartsWith("history?"))
            {
                return JsonDocument.Parse(History(["large", "good"]));
            }
            if (path.Contains("/large?") && path.Contains("format=" + oversizedFormat))
            {
                // Exercise the real streamed HTTP response limit without any network access.
                return await GmailApi.GetAsync(path, token, stop, client);
            }
            return JsonDocument.Parse(Message("654321"));
        }
        var poller = new GmailPoller(store, message => logs.Add(message.ToString()), Request);
        Assert.Equal("200", await poller.PollOnceAsync("100", "TEST-token", "Privat", CancellationToken.None));
        Assert.Equal("654321", store.Latest()?.Code);
        Assert.Equal(2, logs.Count);
        Assert.DoesNotContain(logs, message => message.Contains("TEST-token") || message.Contains("654321"));
    }

    [Fact]
    public async Task UnderestimatedMessageSizeCannotProduceCode()
    {
        var body = "From: service@example.test\r\nSubject: Verification code\r\nContent-Type: text/plain\r\n\r\nYour verification code is 888888\r\n" + new string('x', 1_000_001);
        var sample = Message("888888", rawOverride: Convert.ToBase64String(Encoding.UTF8.GetBytes(body)));
        var store = new OtpStore();
        var poller = new GmailPoller(store, _ => { }, (path, token, stop) =>
            Task.FromResult(JsonDocument.Parse(path.StartsWith("history?") ? History(["large"]) : sample)));
        Assert.Equal("200", await poller.PollOnceAsync("100", "TEST-token", "Privat", CancellationToken.None));
        Assert.Null(store.Latest());
    }

    [Fact]
    public async Task InterruptedRawDownloadIsRetried()
    {
        var store = new OtpStore();
        var fail = true;
        Task<JsonDocument> Request(string path, string token, CancellationToken stop)
        {
            if (path.Contains("format=raw") && fail)
            {
                throw new System.IO.IOException("synthetic interrupted download");
            }
            return Task.FromResult(JsonDocument.Parse(path.StartsWith("history?") ? History(["good"]) : Message("654321")));
        }
        var poller = new GmailPoller(store, _ => { }, Request);
        await Assert.ThrowsAsync<System.IO.IOException>(() => poller.PollOnceAsync("100", "TEST-token", "Privat", CancellationToken.None));
        Assert.Null(store.Latest());
        fail = false;
        Assert.Equal("200", await poller.PollOnceAsync("100", "TEST-token", "Privat", CancellationToken.None));
        Assert.Equal("654321", store.Latest()?.Code);
    }

    private sealed class OversizedResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(new byte[2_000_001]);
            content.Headers.ContentLength = null;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }
    }

    [Fact]
    public async Task CancellationPreventsCursorCommit()
    {
        var poller = new GmailPoller(new OtpStore(), _ => { }, (p, t, stop) =>
        {
            stop.ThrowIfCancellationRequested();
            return Task.FromResult(JsonDocument.Parse(History([])));
        });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancellation = false;
        try
        {
            await poller.PollOnceAsync("200", "TEST-token", "Privat", cancelled.Token);
        }
        catch (OperationCanceledException) { cancellation = true; }
        Check(cancellation, "cancellation prevents cursor commit");
    }
}
