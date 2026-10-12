using System.IO;
using OTPinsert.Platform;
using Xunit;

public sealed class AccountVerificationTests
{
    private static MailAccount Account() => new(Guid.NewGuid(), "Test", "gmail.googleapis.com", 443, "user@example.test", "", false, true)
    {
        Provider = MailProvider.Google,
        OAuthClientId = "test.apps.googleusercontent.com",
        OAuthClientSecret = "synthetic-client-secret"
    };

    [Fact]
    public async Task SuccessfulVerificationOnlyAppliesToMatchingCredentials()
    {
        var account = Account();
        var verification = new AccountVerification(verify: (_, _, _) => Task.CompletedTask);
        Assert.False(verification.Matches(account));
        await verification.VerifyAsync(account);
        Assert.True(verification.Matches(account));
        Assert.True(verification.Matches(account with
        {
            DisplayName = "Renamed"
        }));
        Assert.False(verification.Matches(account with
        {
            Username = "other@example.test"
        }));
        Assert.False(verification.Matches(account with
        {
            OAuthClientSecret = "changed-secret"
        }));
    }

    [Fact]
    public async Task FailedVerificationInvalidatesPreviousSuccessAndCanBeRetried()
    {
        var account = Account();
        var fail = true;
        var verification = new AccountVerification(account, (_, _, _) => fail
            ? Task.FromException(new IOException("synthetic failure")) : Task.CompletedTask);
        Assert.True(verification.Matches(account));
        await Assert.ThrowsAsync<IOException>(() => verification.VerifyAsync(account));
        Assert.False(verification.Matches(account));
        fail = false;
        await verification.VerifyAsync(account);
        Assert.True(verification.Matches(account));
    }

    [Fact]
    public async Task CancellationRejectsEvenAProviderThatReturnsSuccessAndAllowsRetry()
    {
        var account = Account();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken providerToken = default;
        var verification = new AccountVerification(verify: (_, stop, _) =>
        {
            providerToken = stop;
            return hold.Task;
        });
        var pending = verification.VerifyAsync(account);
        verification.Cancel();
        Assert.True(providerToken.IsCancellationRequested);
        hold.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(verification.Matches(account));
        // Cancel after completion is harmless; no disposed token source is retained.
        verification.Cancel();
        await verification.VerifyAsync(account);
        Assert.True(verification.Matches(account));
    }

    [Fact]
    public async Task ConcurrentVerificationCannotReplaceTheActiveRequest()
    {
        var account = Account();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var verification = new AccountVerification(verify: (_, _, _) =>
        {
            calls++;
            return hold.Task;
        });
        var first = verification.VerifyAsync(account);
        await Assert.ThrowsAsync<InvalidOperationException>(() => verification.VerifyAsync(account));
        Assert.Equal(1, calls);
        hold.SetResult();
        await first;
        Assert.True(verification.Matches(account));
    }
}
