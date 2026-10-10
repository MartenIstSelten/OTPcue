using OTPcue.Core;

namespace OTPcue.Platform;

internal static class AccountPolling
{
    internal static Task RunAsync(OtpStore store, MailAccount account, Action<LocalizedText> status, CancellationToken stop)
    {
        if (account.Provider == MailProvider.Google)
        {
            return new GmailPoller(store, status).RunAsync(account, stop);
        }

        return new MailPoller(store, status).RunAsync(
            account.Host, account.Port, account.Username, account.Password, account.StartTls,
            stop, account.DisplayName, account.PollingIntervalSeconds);
    }
}
