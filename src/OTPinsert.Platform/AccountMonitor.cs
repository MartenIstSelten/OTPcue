namespace OTPinsert.Platform;

// Owned by the UI thread. Status callbacks may originate on a background thread.
internal sealed class AccountMonitor(Func<MailAccount, Action<LocalizedText>, CancellationToken, Task> run)
{
    private sealed record Worker(CancellationTokenSource Stop, Task Task);
    private readonly Dictionary<Guid, Worker> workers = new();

    internal void Start(MailAccount account, Action<LocalizedText, CancellationToken> status)
    {
        if (workers.ContainsKey(account.Id))
        {
            throw new InvalidOperationException("Das Konto wird bereits überwacht.");
        }

        account.Validate();
        var stop = new CancellationTokenSource();
        var token = stop.Token;
        void Report(LocalizedText message)
        {
            if (!token.IsCancellationRequested)
            {
                status(message, token);
            }
        }

        var task = Task.Run(() => run(account, Report, token));
        workers.Add(account.Id, new Worker(stop, task));
    }

    internal Task StopAsync(Guid id)
    {
        if (!workers.Remove(id, out var worker))
        {
            return Task.CompletedTask;
        }

        return StopWorkersAsync([worker]);
    }

    internal Task StopAllAsync()
    {
        // Detach first, then cancel every worker before waiting for any of them.
        var snapshot = workers.Values.ToArray();
        workers.Clear();
        return StopWorkersAsync(snapshot);
    }

    private static async Task StopWorkersAsync(Worker[] snapshot)
    {
        var failures = new List<Exception>();
        foreach (var worker in snapshot)
        {
            try
            {
                worker.Stop.Cancel();
            }
            catch (Exception exception) { failures.Add(exception); }
        }
        foreach (var worker in snapshot)
        {
            try
            {
                await worker.Task;
            }
            catch (OperationCanceledException) when (worker.Stop.IsCancellationRequested) { }
            catch (Exception exception) { failures.Add(exception); }
            finally { worker.Stop.Dispose(); }
        }
        if (failures.Count > 0)
        {
            throw new AggregateException("Account workers failed while stopping.", failures);
        }
    }
}
