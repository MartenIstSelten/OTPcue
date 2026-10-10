using MailKit.Net.Imap;
using MailKit.Security;

namespace OTPcue.Platform;

internal static class AccountConnection
{
    internal static async Task VerifyAsync(MailAccount account, CancellationToken stop, IProgress<string>? progress = null)
    {
        account.Validate();
        if (account.Provider == MailProvider.Google)
        {
            await GoogleMailAuth.GetTokenAsync(account, true, stop, progress);
            return;
        }

        using var client = new ImapClient { Timeout = 15_000 };

        await client.ConnectAsync(account.Host, account.Port,
            account.StartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect, stop);
        await client.AuthenticateAsync(account.Username, account.Password, stop);

        await MailFolders.OpenImapFolderAsync(client, account.FolderId, stop);
        await client.DisconnectAsync(true, stop);
    }

    internal static bool MatchesVerifiedAccount(MailAccount account, MailAccount? verified)
        => verified is not null
            && verified.Provider == account.Provider
            && verified.OAuthClientId == account.OAuthClientId
            && verified.OAuthClientSecret == account.OAuthClientSecret
            && string.Equals(verified.Username, account.Username, StringComparison.OrdinalIgnoreCase);
}
