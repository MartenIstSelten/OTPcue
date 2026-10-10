namespace OTPcue.Platform;

// Owned by the dialog's UI thread. Keeps mailbox identity and selection independent
// of credential edits, and commits provider results only after cancellation checks.
internal sealed class AccountFolderSelection(
    MailAccount? existing = null,
    Func<MailAccount, CancellationToken, Task<IReadOnlyList<MailFolder>>>? list = null,
    Func<MailAccount, CancellationToken, Task>? verify = null)
{
    private sealed record Mailbox(MailProvider Provider, string Host, string Username)
    {
        internal static Mailbox From(MailProvider provider, string host, string username)
            => new(provider, host.Trim().ToUpperInvariant(), username.Trim().ToUpperInvariant());
    }

    private readonly Mailbox? originalMailbox = existing is null ? null : Mailbox.From(existing.Provider, existing.Host, existing.Username);
    private Mailbox? mailbox = existing is null ? null : Mailbox.From(existing.Provider, existing.Host, existing.Username);
    private CancellationTokenSource? pending;
    internal MailFolder Selected { get; private set; } = existing is null ? new("INBOX", "INBOX") : new(existing.FolderId, existing.FolderName);
    internal IReadOnlyList<MailFolder> Choices
    {
        get; private set;
    } = existing is null
        ? [new("INBOX", "INBOX")] : [new(existing.FolderId, existing.FolderName)];

    internal bool UpdateMailbox(MailProvider provider, string host, string username)
    {
        var next = Mailbox.From(provider, host, username);
        if (next == mailbox)
        {
            return false;
        }
        Cancel();
        mailbox = next;
        Selected = next == originalMailbox && existing is not null
            ? new(existing.FolderId, existing.FolderName) : new("INBOX", "INBOX");
        Choices = [Selected];
        return true;
    }

    internal void Select(MailFolder folder)
    {
        Selected = Choices.FirstOrDefault(choice => choice.Id == folder.Id)
            ?? throw new ArgumentException("The folder is not available.", nameof(folder));
    }

    internal MailAccount Apply(MailAccount account) => account with
    {
        FolderId = Selected.Id,
        FolderName = Selected.Name
    };

    internal async Task<bool> LoadAsync(MailAccount account)
    {
        var available = await RunAsync(stop => (list ?? MailFolders.ListAsync)(account, stop));
        if (available.Count == 0)
        {
            return false;
        }
        var selected = available.FirstOrDefault(folder => folder.Id == Selected.Id) ?? Selected;
        Choices = available.Any(folder => folder.Id == selected.Id) ? available.ToArray() : available.Prepend(selected).ToArray();
        Selected = selected;
        return true;
    }

    internal bool NeedsVerification(MailAccount account)
        => existing is null ? account.FolderId != "INBOX"
            : account.FolderId != existing.FolderId || account.Provider != existing.Provider
                || !string.Equals(account.Host, existing.Host, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(account.Username, existing.Username, StringComparison.OrdinalIgnoreCase)
                || account.Port != existing.Port || account.StartTls != existing.StartTls
                || account.Password != existing.Password || account.OAuthClientId != existing.OAuthClientId
                || account.OAuthClientSecret != existing.OAuthClientSecret;

    internal Task VerifyAsync(MailAccount account) => RunAsync(async stop =>
    {
        await (verify ?? MailFolders.VerifyAsync)(account, stop);
        return true;
    });

    internal void Cancel() => pending?.Cancel();

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action)
    {
        if (pending is not null)
        {
            throw new InvalidOperationException("A folder operation is already running.");
        }
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        pending = stop;
        try
        {
            var result = await action(stop.Token);
            stop.Token.ThrowIfCancellationRequested();
            return result;
        }
        finally { pending = null; }
    }
}
