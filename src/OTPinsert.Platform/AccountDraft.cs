namespace OTPinsert.Platform;

// Form values are validated before converting NumberBox doubles to integers.
internal sealed record AccountDraft(
    Guid Id, string DisplayName, string Host, double Port, string Username, string Password,
    bool StartTls, bool Enabled, double PollingInterval, MailProvider Provider,
    string ClientId, string ClientSecret)
{
    internal AccountValidationError? Validate()
        => AccountValidation.Validate(Provider, PollingInterval, ClientId?.Trim(), Username,
            Host?.Trim(), Port, Provider == MailProvider.Google ? "" : Password, StartTls);

    internal MailAccount ToAccount()
    {
        if (Validate() is { } error)
        {
            throw new ArgumentException(error.Message);
        }

        var oauth = Provider == MailProvider.Google;
        var username = Username.Trim();
        var account = new MailAccount(Id, string.IsNullOrWhiteSpace(DisplayName) ? username : DisplayName.Trim(),
            Host.Trim(), (int)Port, username, oauth ? "" : Password, StartTls, Enabled)
        {
            PollingIntervalSeconds = (int)PollingInterval,
            Provider = Provider,
            OAuthClientId = oauth ? ClientId.Trim() : "",
            OAuthClientSecret = Provider == MailProvider.Google ? ClientSecret : ""
        };
        account.Validate();
        return account;
    }
}
