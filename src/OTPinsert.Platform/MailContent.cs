using System.Text.RegularExpressions;
using MimeKit;
using OTPinsert.Core;

namespace OTPinsert.Platform;

internal static class MailContent
{
    internal const int MaximumMessageSize = 1_000_000;
    internal const int MaximumRawLength = 1_400_000;
    private const int MaximumBodyLength = 100_000;
    private const int MaximumSenderLength = 80;
    private static readonly Regex HtmlTags = new("<[^>]*>",
        RegexOptions.Singleline | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
    private static readonly Regex HtmlComments = new("<!--.*?-->",
        RegexOptions.Singleline | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
    private static readonly Regex HtmlWhitespace = new(@"\s+",
        RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));

    internal static bool IsRecent(DateTimeOffset received, TimeSpan countdown, TimeProvider time)
    {
        var now = time.GetUtcNow();
        return received >= now - countdown && received <= now.AddMinutes(1);
    }

    internal static string GetSource(MimeMessage message, string? accountLabel)
    {
        var sender = message.From.Mailboxes.FirstOrDefault();
        var source = !string.IsNullOrWhiteSpace(sender?.Name)
            ? sender.Name
            : sender?.Address ?? "Unbekannt";
        source = new string(source.Where(c => !char.IsControl(c)).Take(MaximumSenderLength).ToArray());
        return string.IsNullOrWhiteSpace(accountLabel) ? source : source + " · " + accountLabel;
    }

    internal static string? ExtractCode(MimeMessage message)
    {
        var subject = message.Subject ?? "";
        var plain = message.TextBody;
        var code = plain is null ? null : OtpDetector.Extract(subject, plain);
        if (code is not null)
        {
            return code;
        }

        // Alternative plain-text parts can lose the separators around a visually
        // distinct HTML code. Try HTML when plain text has no unambiguous result.
        var html = message.HtmlBody;
        if (html is null)
        {
            return null;
        }

        html = html[..Math.Min(html.Length, MaximumBodyLength)];
        // Linear matching avoids backtracking on unmatched HTML delimiters.
        html = HtmlComments.Replace(html, " ");
        var body = HtmlTags.Replace(html, " ");
        // HTML indentation is visual whitespace, not distance from the code label.
        // Normalize only HTML; plain-text proximity checks retain their meaning.
        body = HtmlWhitespace.Replace(System.Net.WebUtility.HtmlDecode(body), " ");
        return OtpDetector.Extract(subject, body);
    }
}
