namespace OTPcue.Platform;

// Owned by the dialog's UI thread. The form only needs to coordinate presentation;
// this object owns verification, cancellation and the verified account snapshot.
internal sealed class AccountVerification(
    MailAccount? verified = null,
    Func<MailAccount, CancellationToken, IProgress<string>?, Task>? verify = null)
{
    private MailAccount? verified = verified;
    private CancellationTokenSource? pending;

    internal bool Matches(MailAccount account) => AccountConnection.MatchesVerifiedAccount(account, verified);

    internal void Cancel() => pending?.Cancel();

    internal async Task VerifyAsync(MailAccount account, IProgress<string>? progress = null)
    {
        if (pending is not null)
        {
            throw new InvalidOperationException("An account verification is already running.");
        }

        verified = null;
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(account.UsesOAuth ? 3 : 1));
        pending = stop;
        try
        {
            await (verify ?? AccountConnection.VerifyAsync)(account, stop.Token, progress);
            stop.Token.ThrowIfCancellationRequested();
            verified = account;
        }
        finally
        {
            pending = null;
        }
    }
}
