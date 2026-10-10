namespace OTPcue.Platform;

internal static class Shutdown
{
    internal static async Task<IReadOnlyList<Exception>> RunAsync(
        IEnumerable<Action> stopProducers, Func<Task> stopWorkers, IEnumerable<Action> cleanup)
    {
        var failures = new List<Exception>();
        void Try(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception) { failures.Add(exception); }
        }
        foreach (var stop in stopProducers)
        {
            Try(stop);
        }

        try
        {
            await stopWorkers();
        }
        catch (Exception exception) { failures.Add(exception); }
        finally
        {
            foreach (var release in cleanup)
            {
                Try(release);
            }
        }
        return failures;
    }
}
