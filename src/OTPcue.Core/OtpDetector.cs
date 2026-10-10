using System.Net;
using System.Text.RegularExpressions;

namespace OTPcue.Core;

public static class OtpDetector
{
    private static readonly Regex Keywords = new(@"\b(?:verification\s+code|security\s+code|login\s+code|sign[ -]in\s+code|authentication\s+code|one[ -]time\s+(?:code|password|passcode)|passcode|OTP|Einmal[- ]?(?:code|passwort)|Anmelde[- ]?code|Verifizierungs?[- ]?code|Sicherheitscode|Bestätigungscode|Authentifizierungscode)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Digits = new(@"(?<![\p{L}\p{N}])(?:[0-9]{3}[- \u00a0\u202f][0-9]{3}|[0-9]{4,8})(?![\p{L}\p{N}])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static bool IsCodeLabel(string text) => Keywords.IsMatch(text) || Regex.IsMatch(text, @"\b(?:verification|verifizierung|one[ -]?time|2fa|mfa|authentifizierung|code)\b", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    public static string? Extract(string subject, string body)
    {
        var limitedBody = body[..Math.Min(body.Length, 100_000)];
        // Some HTML mail clients serialize visual padding as long runs of encoded spaces.
        // Decode those only after shortening the runs, so formatting cannot push the OTP
        // outside the proximity window. Keep ordinary whitespace intact to retain the
        // detector's protection against unrelated, distant numbers.
        limitedBody = Regex.Replace(limitedBody, @"(?:(?:&#x0*20;|&#0*32;|&nbsp;)[\t ]*){2,}", " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        limitedBody = WebUtility.HtmlDecode(limitedBody);
        var text = WebUtility.HtmlDecode(subject) + "\n" + limitedBody;
        var keywords = Keywords.Matches(text).Cast<Match>().ToArray();
        if (keywords.Length == 0)
        {
            return null;
        }
        // Only accept a code near a known phrase; reject ambiguous messages.
        var candidates = Digits.Matches(text).Cast<Match>()
            .Where(code => keywords.Any(key => Math.Abs(code.Index - key.Index) <= 120))
            .Select(code => code.Value.Replace("-", "").Replace(" ", "").Replace("\u00a0", "").Replace("\u202f", ""))
            .Distinct().ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }
}
