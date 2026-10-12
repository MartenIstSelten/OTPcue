namespace OTPinsert.Platform;

internal static class AccountChange
{
    // Persistence is the commit point. A failed save leaves the running account
    // untouched. After a successful save, cleanup failures must not leave the UI
    // showing the old account. stop must await worker termination even on failure,
    // as AccountMonitor.StopAsync does.
    internal static async Task<bool> CommitAsync(Action persist, Func<Task> stop, Action apply, Action? cleanup = null)
    {
        persist();
        var clean = true;
        try
        {
            await stop();
        }
        catch (Exception exception)
        {
            AppDiagnostics.ReportFailure("Accounts.StopAfterCommit", exception);
            clean = false;
        }

        apply();
        try
        {
            cleanup?.Invoke();
        }
        catch (Exception exception)
        {
            AppDiagnostics.ReportFailure("Accounts.CleanupAfterCommit", exception);
            clean = false;
        }

        return clean;
    }
}
