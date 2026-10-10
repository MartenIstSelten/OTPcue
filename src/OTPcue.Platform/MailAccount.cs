using System.IO;

namespace OTPcue.Platform;

internal sealed record MailAccount(Guid Id, string DisplayName, string Host, int Port, string Username, string Password, bool StartTls, bool Enabled)
{
    public string Transport => StartTls ? Localization.Text(TextId.STARTTLSRequired) : "SSL/TLS";
    public int PollingIntervalSeconds { get; init; } = 3;
    public MailProvider Provider { get; init; } = MailProvider.Custom;
    public string OAuthClientId { get; init; } = "";
    public string OAuthClientSecret { get; init; } = "";
    public bool UsesOAuth => Provider == MailProvider.Google;
    internal void Validate()
    {
        if (AccountValidation.Validate(Provider, PollingIntervalSeconds, OAuthClientId,
            Username, Host, Port, Password, StartTls) is { } error)
        {
            throw new InvalidDataException(error.Message);
        }
    }
}
