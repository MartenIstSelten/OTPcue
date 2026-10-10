using System.IO;
using OTPcue.Platform;
using Xunit;

public sealed class AccountFolderSelectionTests
{
    private static MailAccount Account() => new(Guid.NewGuid(), "Test", "imap.example.test", 993,
        "user@example.test", "synthetic-password", false, true);

    private static readonly MailFolder Codes = new("INBOX/Codes", "INBOX/Codes");

    [Fact]
    public async Task CredentialAndConnectionEditsPreserveTheUnsavedSelection()
    {
        var account = Account();
        var selection = new AccountFolderSelection(account, (_, _) => Task.FromResult<IReadOnlyList<MailFolder>>([new("INBOX", "INBOX"), Codes]));
        await selection.LoadAsync(account);
        selection.Select(Codes);
        var edited = account with
        {
            Password = "new-password",
            Port = 143,
            StartTls = true
        };
        Assert.False(selection.UpdateMailbox(edited.Provider, edited.Host, edited.Username));
        Assert.Equal(Codes.Id, selection.Apply(edited).FolderId);
        Assert.Equal(2, selection.Choices.Count);
        Assert.True(selection.NeedsVerification(selection.Apply(edited)));
    }

    [Theory]
    [InlineData("host")]
    [InlineData("username")]
    [InlineData("provider")]
    public void ChangingMailboxDropsFoldersFromThePreviousMailbox(string field)
    {
        var account = Account() with
        {
            FolderId = Codes.Id,
            FolderName = Codes.Name
        };
        var selection = new AccountFolderSelection(account);
        var other = field switch
        {
            "host" => account with { Host = "other.example.test" },
            "username" => account with { Username = "other@example.test" },
            _ => account with { Provider = MailProvider.Gmail }
        };
        Assert.True(selection.UpdateMailbox(other.Provider, other.Host, other.Username));
        Assert.Equal("INBOX", selection.Selected.Id);
        Assert.Single(selection.Choices);
        Assert.True(selection.UpdateMailbox(account.Provider, account.Host, account.Username));
        Assert.Equal(Codes, selection.Selected);
    }

    [Fact]
    public async Task ReloadUsesStableIdsAndDoesNotReplaceAMissingSelection()
    {
        var account = Account() with
        {
            FolderId = "Label_42",
            FolderName = "Old name"
        };
        IReadOnlyList<MailFolder> available = [new("Label_42", "New name")];
        var selection = new AccountFolderSelection(account, (_, _) => Task.FromResult(available));
        Assert.True(await selection.LoadAsync(account));
        Assert.Equal("New name", selection.Apply(account).FolderName);
        available = [new("INBOX", "INBOX")];
        Assert.True(await selection.LoadAsync(account));
        Assert.Equal("Label_42", selection.Selected.Id);
        Assert.Equal(2, selection.Choices.Count);
    }

    [Fact]
    public async Task FailedOrEmptyReloadPreservesSelectionAndAllowsRetry()
    {
        var account = Account() with
        {
            FolderId = Codes.Id,
            FolderName = Codes.Name
        };
        var fail = true;
        var selection = new AccountFolderSelection(account, (_, _) => fail
            ? Task.FromException<IReadOnlyList<MailFolder>>(new IOException("synthetic failure"))
            : Task.FromResult<IReadOnlyList<MailFolder>>([]));
        await Assert.ThrowsAsync<IOException>(() => selection.LoadAsync(account));
        Assert.Equal(Codes, selection.Selected);
        fail = false;
        Assert.False(await selection.LoadAsync(account));
        Assert.Equal(Codes, selection.Selected);
        Assert.Single(selection.Choices);
    }

    [Fact]
    public async Task CanceledLoadCannotCommitEvenIfProviderIgnoresCancellation()
    {
        var account = Account();
        var hold = new TaskCompletionSource<IReadOnlyList<MailFolder>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var selection = new AccountFolderSelection(account, (_, _) => hold.Task);
        var pending = selection.LoadAsync(account);
        selection.Cancel();
        hold.SetResult([Codes]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal("INBOX", selection.Selected.Id);
        Assert.Single(selection.Choices);
        selection.Cancel();
        Assert.True(await selection.LoadAsync(account));
    }

    [Fact]
    public async Task MailboxChangeCancelsAnInFlightFolderList()
    {
        var account = Account();
        var hold = new TaskCompletionSource<IReadOnlyList<MailFolder>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var selection = new AccountFolderSelection(account, (_, _) => hold.Task);
        var pending = selection.LoadAsync(account);
        selection.UpdateMailbox(account.Provider, "other.example.test", account.Username);
        hold.SetResult([Codes]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.DoesNotContain(selection.Choices, folder => folder.Id == Codes.Id);
    }

    [Fact]
    public async Task VerificationRejectsConcurrencyAndCancellationAndCanRetryAfterFailure()
    {
        var account = Account();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fail = false;
        MailAccount? verified = null;
        var selection = new AccountFolderSelection(account, verify: (candidate, _) =>
        {
            verified = candidate;
            return fail ? Task.FromException(new IOException("synthetic failure")) : hold.Task;
        });
        var selected = account with
        {
            FolderId = Codes.Id,
            FolderName = Codes.Name
        };
        var pending = selection.VerifyAsync(selected);
        await Assert.ThrowsAsync<InvalidOperationException>(() => selection.LoadAsync(account));
        selection.Cancel();
        hold.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        fail = true;
        await Assert.ThrowsAsync<IOException>(() => selection.VerifyAsync(selected));
        fail = false;
        await selection.VerifyAsync(selected);
        Assert.Equal(selected, verified);
    }

    [Fact]
    public void UnchangedFolderOnlyNeedsReverificationWhenConnectionChanges()
    {
        var account = Account() with
        {
            FolderId = Codes.Id,
            FolderName = Codes.Name
        };
        var selection = new AccountFolderSelection(account);
        Assert.False(selection.NeedsVerification(account with
        {
            DisplayName = "Renamed",
            PollingIntervalSeconds = 10
        }));
        Assert.True(selection.NeedsVerification(account with
        {
            Password = "changed"
        }));
        Assert.True(selection.NeedsVerification(account with
        {
            FolderId = "INBOX",
            FolderName = "INBOX"
        }));
    }
}
