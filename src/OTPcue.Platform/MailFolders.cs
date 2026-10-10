using System.Text.Json;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

namespace OTPcue.Platform;

internal sealed record MailFolder(string Id, string Name);

internal static class MailFolders
{
    internal static Task<IMailFolder> GetImapFolderAsync(ImapClient client, string id, CancellationToken stop)
        => string.Equals(id, "INBOX", StringComparison.OrdinalIgnoreCase)
            ? Task.FromResult(client.Inbox) : client.GetFolderAsync(id, stop);

    internal static async Task<IMailFolder> OpenImapFolderAsync(ImapClient client, string id, CancellationToken stop)
    {
        var folder = await GetImapFolderAsync(client, id, stop);
        await folder.OpenAsync(FolderAccess.ReadOnly, stop);
        return folder;
    }

    internal static async Task<IReadOnlyList<MailFolder>> ListAsync(MailAccount account, CancellationToken stop)
    {
        account.Validate();
        if (account.UsesOAuth)
        {
            var token = await GoogleMailAuth.GetTokenAsync(account, false, stop);
            using var json = await GmailApi.GetAsync("labels", token, stop);
            return GoogleLabels(json.RootElement);
        }

        using var client = new ImapClient { Timeout = 15_000 };
        await client.ConnectAsync(account.Host, account.Port,
            account.StartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect, stop);
        await client.AuthenticateAsync(account.Username, account.Password, stop);
        var folders = new List<IMailFolder> { client.Inbox };
        foreach (var space in client.PersonalNamespaces)
        {
            folders.AddRange(await client.GetFoldersAsync(space, false, stop));
        }
        var result = folders.Where(f => (f.Attributes & (FolderAttributes.NoSelect | FolderAttributes.NonExistent)) == 0)
            .Select(f => new MailFolder(f.FullName, f.FullName)).DistinctBy(f => f.Id).ToArray();
        await client.DisconnectAsync(true, stop);
        return Sort(result);
    }

    internal static IReadOnlyList<MailFolder> GoogleLabels(JsonElement root)
        => Sort(root.GetProperty("labels").EnumerateArray()
            .Select(label => new MailFolder(label.GetProperty("id").GetString()!, label.GetProperty("name").GetString()!)));

    private static IReadOnlyList<MailFolder> Sort(IEnumerable<MailFolder> folders)
        => folders.OrderBy(f => f.Id == "INBOX" ? 0 : 1).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

    internal static async Task VerifyAsync(MailAccount account, CancellationToken stop)
    {
        account.Validate();
        if (!account.UsesOAuth)
        {
            await AccountConnection.VerifyAsync(account, stop);
            return;
        }
        var token = await GoogleMailAuth.GetTokenAsync(account, false, stop);
        using var label = await GmailApi.GetAsync("labels/" + Uri.EscapeDataString(account.FolderId), token, stop);
    }
}
