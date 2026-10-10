using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OTPcue.Platform;

internal static class GmailApi
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    internal static async Task<JsonDocument> GetAsync(string relative, string token, CancellationToken stop)
        => await GetAsync(relative, token, stop, Client);

    internal static async Task<JsonDocument> GetAsync(string relative, string token, CancellationToken stop, HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://gmail.googleapis.com/gmail/v1/users/me/" + relative);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new MailAuthenticationRequiredException(new HttpRequestException("Google-Token abgelehnt."));
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(stop);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, stop)) > 0)
        {
            if (buffer.Length + count > 2_000_000)
            {
                throw new GmailResponseTooLargeException();
            }

            buffer.Write(chunk, 0, count);
        }
        buffer.Position = 0;
        return await JsonDocument.ParseAsync(buffer, cancellationToken: stop);
    }
}

// Only individual message processing may skip this deterministic content failure.
internal sealed class GmailResponseTooLargeException : Exception
{
    internal GmailResponseTooLargeException() : base("Gmail-Antwort zu groß.") { }
}
