namespace OTPinsert.Platform;

internal enum AccountField
{
    Provider, PollingInterval, ClientId, Username, Host, Port, Password
}

internal sealed record AccountValidationError(AccountField Field, string Message);

internal static class AccountValidation
{
    // Also accepts NumberBox values, so the form and persisted model share the rules
    // before any floating-point value is converted to an integer.
    internal static AccountValidationError? Validate(MailProvider provider, double pollingInterval,
        string? clientId, string? username, string? host, double port, string? password, bool startTls)
    {
        if (!Enum.IsDefined(provider))
        {
            return Error(AccountField.Provider, TextId.PleaseSelectAnEmailProvider);
        }
        if (!IsWholeNumberInRange(pollingInterval, 1, 600))
        {
            return Error(AccountField.PollingInterval, TextId.InvalidPollingInterval);
        }
        if (provider == MailProvider.Google && (string.IsNullOrWhiteSpace(clientId)
            || !clientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal)))
        {
            return Error(AccountField.ClientId, TextId.InvalidGoogleClientId);
        }
        if (string.IsNullOrWhiteSpace(username))
        {
            return Error(AccountField.Username, TextId.UsernameRequired);
        }
        if (string.IsNullOrWhiteSpace(host) || Uri.CheckHostName(host) == UriHostNameType.Unknown
            || (provider == MailProvider.Google && host != "gmail.googleapis.com"))
        {
            return Error(AccountField.Host, TextId.InvalidImapHost);
        }
        if (!IsWholeNumberInRange(port, 1, 65535) || (provider == MailProvider.Google && port != 443))
        {
            return Error(AccountField.Port, TextId.InvalidPort);
        }
        if (provider != MailProvider.Google && string.IsNullOrEmpty(password))
        {
            return Error(AccountField.Password, TextId.PasswordRequired);
        }
        if (provider == MailProvider.Google && (startTls || !string.IsNullOrEmpty(password)))
        {
            return Error(AccountField.Provider, TextId.PleaseSelectAnEmailProvider);
        }
        return null;
    }

    private static AccountValidationError Error(AccountField field, TextId text)
        => new(field, Localization.Text(text));

    private static bool IsWholeNumberInRange(double value, int minimum, int maximum)
        => double.IsFinite(value) && value == Math.Truncate(value) && value >= minimum && value <= maximum;
}
