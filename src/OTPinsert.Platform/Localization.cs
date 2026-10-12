using System.Globalization;
using System.Resources;

namespace OTPinsert.Platform;

internal static class Localization
{
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;
    private static readonly ResourceManager Resources = new("OTPinsert.Platform.Resources.Strings", typeof(Localization).Assembly);
    private static string language = ResolveLanguage("system", SystemCulture);
    internal static string Language => language;
    internal static CultureInfo Culture => CultureInfo.GetCultureInfo(language);
    internal static string ResolveLanguage(string preference, CultureInfo systemCulture) => preference switch
    {
        "de" => "de",
        "en" => "en",
        _ => systemCulture.TwoLetterISOLanguageName == "de" ? "de" : "en"
    };
    internal static void Configure(string preference) => language = ResolveLanguage(preference, SystemCulture);
    internal static string Text(TextId id) => Get(id, Culture);
    internal static string Get(TextId id, CultureInfo culture) => Resources.GetString(id.ToString(), culture)
        ?? throw new InvalidOperationException($"Missing translation: {id} ({culture.Name}).");
    internal static string Format(TextId id, params object[] arguments) => string.Format(Culture, Text(id), arguments);
}
