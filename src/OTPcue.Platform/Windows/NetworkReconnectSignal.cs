using Windows.Networking.Connectivity;

namespace OTPcue.Platform;

// Each polling worker observes network recovery independently. A generation captured
// before a request also catches recovery that happens before its retry wait starts.
internal sealed class NetworkReconnectSignal : IDisposable
{
    private readonly object gate = new();
    private readonly bool observeNetwork;
    private bool internetAccess;
    private TaskCompletionSource recovered = NewSignal();

    internal NetworkReconnectSignal(bool observeNetwork = true)
    {
        this.observeNetwork = observeNetwork;
        if (observeNetwork)
        {
            NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
            OnNetworkStatusChanged(null!);
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Capture()
    {
        lock (gate)
        {
            return recovered.Task;
        }
    }

    private void OnNetworkStatusChanged(object sender)
    {
        lock (gate)
        {
            UpdateInternetAccess(NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel()
                == NetworkConnectivityLevel.InternetAccess);
        }
    }

    internal void UpdateInternetAccess(bool available)
    {
        lock (gate)
        {
            if (available && !internetAccess)
            {
                var previous = recovered;
                recovered = NewSignal();
                previous.TrySetResult();
            }
            internetAccess = available;
        }
    }

    internal static async Task WaitAsync(Task recovery, TimeSpan delay, CancellationToken stop)
    {
        try
        {
            await recovery.WaitAsync(delay, stop);
        }
        catch (TimeoutException) { } // Keep periodic retries when Windows reports no recovery.
        stop.ThrowIfCancellationRequested();
    }

    internal static async Task WaitWithCountdownAsync(Task recovery, int seconds, Action<int> report, CancellationToken stop)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var duration = TimeSpan.FromSeconds(seconds);
        var lastReportedSeconds = -1;
        while (!recovery.IsCompleted && elapsed.Elapsed < duration)
        {
            stop.ThrowIfCancellationRequested();
            var remaining = duration - elapsed.Elapsed;
            var remainingSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
            if (remainingSeconds != lastReportedSeconds)
            {
                report(remainingSeconds);
                lastReportedSeconds = remainingSeconds;
            }
            await WaitAsync(recovery, remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1), stop);
        }
        stop.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        if (observeNetwork)
        {
            NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
        }
    }
}
