namespace OTPinsert.Platform;

internal static class GoogleOAuthConfiguration
{
    // Shared desktop OAuth client: these values identify the app and cannot be
    // confidential in an installed application. They are not user access tokens.
    // Forks should use their own Desktop app client; see CONTRIBUTING.md.
    internal const string ClientId = "831424962559-569rmidc6ac6cudp4v0hr4cjc03sc1dg.apps.googleusercontent.com";
    internal const string ClientSecret = "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";
    internal static string DefaultClientId => Environment.GetEnvironmentVariable("OTP_GOOGLE_CLIENT_ID") ?? ClientId;
    internal static string DefaultClientSecret => Environment.GetEnvironmentVariable("OTP_GOOGLE_CLIENT_SECRET")
        ?? (DefaultClientId == ClientId ? ClientSecret : "");
}
