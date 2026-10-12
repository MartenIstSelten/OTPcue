using System.IO;
using OTPinsert.Platform;
using Xunit;

public sealed class AccountChangeTests
{
    [Fact]
    public async Task FailedSaveDoesNotStopOrChangeTheRunningAccount()
    {
        var calls = new List<string>();
        await Assert.ThrowsAsync<IOException>(() => AccountChange.CommitAsync(
            () => { calls.Add("save"); throw new IOException("synthetic storage failure"); },
            () => { calls.Add("stop"); return Task.CompletedTask; },
            () => calls.Add("apply"),
            () => calls.Add("cleanup")));
        Assert.Equal(new[] { "save" }, calls);
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("disable")]
    [InlineData("remove")]
    public async Task FaultedWorkerCannotLeaveDisplayBehindTheSavedChange(string operation)
    {
        var original = new MailAccount(Guid.NewGuid(), "Old", "imap.example.test", 993, "test", "test", false, true);
        MailAccount? updated = operation switch
        {
            "edit" => original with { DisplayName = "New" },
            "disable" => original with { Enabled = false },
            _ => null
        };
        MailAccount? saved = original;
        MailAccount? displayed = original;
        var cleared = false;
        var cleaned = false;
        var monitor = new AccountMonitor((account, _, stop) => account.DisplayName == "Old"
            ? Task.FromException(new IOException("synthetic worker failure"))
            : Task.Delay(Timeout.InfiniteTimeSpan, stop));
        monitor.Start(original, (_, _) => { });
        try
        {
            var clean = await AccountChange.CommitAsync(
                () => saved = updated,
                () => monitor.StopAsync(original.Id),
                () =>
                {
                    cleared = true;
                    displayed = updated;
                    if (updated?.Enabled == true)
                    {
                        monitor.Start(updated, (_, _) => { });
                    }
                },
                () => cleaned = true);

            Assert.False(clean);
            Assert.Equal(updated, saved);
            Assert.Equal(saved, displayed);
            Assert.True(cleared);
            Assert.True(cleaned);
        }
        finally { await monitor.StopAllAsync(); }
    }

    [Fact]
    public async Task DisplayIsUpdatedOnlyAfterWorkerTermination()
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        var pending = AccountChange.CommitAsync(
            () => calls.Add("save"),
            () => { calls.Add("stop"); return stopped.Task; },
            () => calls.Add("apply"),
            () => calls.Add("cleanup"));
        Assert.Equal(new[] { "save", "stop" }, calls);
        Assert.False(pending.IsCompleted);
        stopped.SetResult();
        Assert.True(await pending);
        Assert.Equal(new[] { "save", "stop", "apply", "cleanup" }, calls);
    }

    [Fact]
    public async Task CacheCleanupFailureDoesNotUndoTheCommittedDisplayChange()
    {
        var applied = false;
        var clean = await AccountChange.CommitAsync(
            () => { }, () => Task.CompletedTask, () => applied = true,
            () => throw new IOException("synthetic cache deletion failure"));
        Assert.False(clean);
        Assert.True(applied);
    }
}
