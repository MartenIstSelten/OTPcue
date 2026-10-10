using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using MimeKit;
using OTPcue.Core;

namespace OTPcue.Platform;

internal sealed class GmailPoller(OtpStore store, Action<LocalizedText> status, Func<string, string, CancellationToken, Task<JsonDocument>>? request = null, TimeProvider? time = null)
{
    private readonly TimeProvider time = time ?? TimeProvider.System;
    private readonly HashSet<string> pendingIds = new(StringComparer.Ordinal);
    private Task<JsonDocument> GetAsync(string relative, string token, CancellationToken stop)
        => request is null ? GmailApi.GetAsync(relative, token, stop) : request(relative, token, stop);
    internal async Task RunAsync(MailAccount account, CancellationToken stop)
    {
        using var network = new NetworkReconnectSignal();
        string? cursor = null;
        var failures = 0;
        while (!stop.IsCancellationRequested)
        {
            var recovery = network.Capture();
            try
            {
                var token = await GoogleMailAuth.GetTokenAsync(account, false, stop);
                if (cursor is null)
                {
                    using var profile = await GetAsync("profile", token, stop);
                    cursor = profile.RootElement.GetProperty("historyId").GetString() ?? throw new InvalidDataException("Gmail-Verlauf fehlt.");
                    status(new(TextId.GoogleConnectedOnlyNewINBOXEmailsAreChecked, account.PollingIntervalSeconds));
                }
                else
                {
                    cursor = await PollOnceAsync(cursor, token, account.DisplayName, stop, account.FolderId);
                    if (failures > 0)
                    {
                        status(new(TextId.GoogleConnectedOnlyNewINBOXEmailsAreChecked, account.PollingIntervalSeconds));
                    }
                }

                failures = 0;
                await Task.Delay(TimeSpan.FromSeconds(account.PollingIntervalSeconds), stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (MailAuthenticationRequiredException)
            {
                status(new(TextId.GoogleSignInRequired));
                break;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                cursor = null;
                pendingIds.Clear();
                status(new(TextId.GmailHistoryExpiredEstablishingANewBaseline));
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException
                or Google.Apis.Auth.OAuth2.Responses.TokenResponseException)
            {
                try
                {
                    failures = Math.Min(failures + 1, 10);
                    await NetworkReconnectSignal.WaitWithCountdownAsync(recovery, 3 * failures,
                        seconds => status(new(TextId.GoogleConnectionFailedRetrying, ex.GetType().Name, seconds)), stop);
                    status(new(TextId.StartingConnection));
                }
                catch (OperationCanceledException) { break; }
            }
            catch (Exception ex)
            {
                AppDiagnostics.ReportFailure("GmailPolling.Run", ex);
                status(new(TextId.GoogleMonitoringFailed, ex.GetType().Name));
                break;
            }
        }
    }

    internal async Task<string> PollOnceAsync(string cursor, string token, string accountLabel, CancellationToken stop, string folderId = "INBOX")
    {
        string? page = null;
        string? nextCursor = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            var query = "history?startHistoryId=" + Uri.EscapeDataString(cursor)
                + "&historyTypes=messageAdded&historyTypes=labelAdded&labelId=" + Uri.EscapeDataString(folderId) + "&maxResults=100";
            if (page is not null)
            {
                query += "&pageToken=" + Uri.EscapeDataString(page);
            }

            using var history = await GetAsync(query, token, stop);
            var root = history.RootElement;
            if (root.TryGetProperty("history", out var entries))
            {
                foreach (var entry in entries.EnumerateArray())
                {
                    foreach (var changeType in new[] { "messagesAdded", "labelsAdded" })
                    {
                        if (!entry.TryGetProperty(changeType, out var added))
                        {
                            continue;
                        }

                        foreach (var item in added.EnumerateArray())
                        {
                            var message = item.GetProperty("message");
                            // Label events list the added labels on the event itself.
                            var labelSource = changeType == "labelsAdded" ? item : message;
                            if (!labelSource.TryGetProperty("labelIds", out var labels) || !labels.EnumerateArray().Any(l => l.GetString() == folderId))
                            {
                                continue;
                            }

                            var id = message.GetProperty("id").GetString();
                            if (!string.IsNullOrEmpty(id) && seen.Add(id) && !pendingIds.Contains(id))
                            {
                                await ProcessAsync(id, token, accountLabel, stop, folderId);
                                pendingIds.Add(id);
                            }
                        }
                    }
                }
            }

            page = root.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
            nextCursor = root.GetProperty("historyId").GetString();
        } while (page is not null);
        // Commit only after every page and message succeeds; interrupted downloads retry.
        stop.ThrowIfCancellationRequested();
        pendingIds.Clear();
        return nextCursor ?? cursor;
    }

    private async Task ProcessAsync(string id, string token, string label, CancellationToken stop, string folderId)
    {
        try
        {
            var messagePath = "messages/" + Uri.EscapeDataString(id);
            // Do not download a large body merely to discover that it is unsuitable.
            using var metadata = await GetAsync(messagePath + "?format=metadata&fields=labelIds,sizeEstimate,internalDate", token, stop);
            if (!IsSuitable(metadata.RootElement, folderId, out _))
            {
                return;
            }

            using var json = await GetAsync(messagePath + "?format=raw", token, stop);
            var root = json.RootElement;
            // Recheck the actual response: labels may change and sizeEstimate is an estimate.
            if (!IsSuitable(root, folderId, out var received))
            {
                return;
            }

            var raw = root.GetProperty("raw").GetString() ?? "";
            if (raw.Length > MailContent.MaximumRawLength)
            {
                return;
            }

            var bytes = DecodeRaw(raw);
            if (bytes.Length > MailContent.MaximumMessageSize)
            {
                return;
            }
            using var stream = new MemoryStream(bytes);
            using var mail = await MimeMessage.LoadAsync(stream, stop);
            var code = MailContent.ExtractCode(mail);
            if (code is null)
            {
                return;
            }

            var source = MailContent.GetSource(mail, label);
            stop.ThrowIfCancellationRequested();
            store.Add(code, source, received);
            status(new(TextId.NewGoogleEmailCodeDetected));
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { /* Deleted since history entry. */ }
        catch (Exception ex) when (ex is GmailResponseTooLargeException or FormatException or ParseException or RegexMatchTimeoutException)
        {
            status(new(TextId.GoogleEmailContentCouldNotBeProcessedMessageSkipped));
        }
    }

    private bool IsSuitable(JsonElement root, string folderId, out DateTimeOffset received)
    {
        received = default;
        if (!root.GetProperty("labelIds").EnumerateArray().Any(l => l.GetString() == folderId)
            || root.GetProperty("sizeEstimate").GetInt64() > MailContent.MaximumMessageSize)
        {
            return false;
        }
        received = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(root.GetProperty("internalDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        return MailContent.IsRecent(received, store.Countdown, time);
    }

    internal static byte[] DecodeRaw(string raw)
    {
        var value = raw.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(value.PadRight((value.Length + 3) / 4 * 4, '='));
    }
}
