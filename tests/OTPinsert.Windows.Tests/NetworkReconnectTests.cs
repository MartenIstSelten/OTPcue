using OTPinsert.Platform;
using Xunit;

public sealed class NetworkReconnectTests
{
    [Fact]
    public async Task CountdownReportsRemainingSecondsAndStopsOnRecovery()
    {
        using var network = new NetworkReconnectSignal(observeNetwork: false);
        var reported = new List<int>();
        var wait = NetworkReconnectSignal.WaitWithCountdownAsync(network.Capture(), 30, seconds =>
        {
            reported.Add(seconds);
            network.UpdateInternetAccess(true);
        }, CancellationToken.None);

        await wait.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { 30 }, reported);
    }

    [Fact]
    public async Task CountdownDecreasesBeforeRetry()
    {
        using var network = new NetworkReconnectSignal(observeNetwork: false);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reported = new List<int>();
        await NetworkReconnectSignal.WaitWithCountdownAsync(network.Capture(), 2, reported.Add, stop.Token);
        Assert.Equal(new[] { 2, 1 }, reported);
    }

    [Fact]
    public async Task RecoveryWakesEveryWaitingAccount()
    {
        using var first = new NetworkReconnectSignal(observeNetwork: false);
        using var second = new NetworkReconnectSignal(observeNetwork: false);
        var firstWait = NetworkReconnectSignal.WaitAsync(first.Capture(), TimeSpan.FromSeconds(30), CancellationToken.None);
        var secondWait = NetworkReconnectSignal.WaitAsync(second.Capture(), TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.False(firstWait.IsCompleted);
        Assert.False(secondWait.IsCompleted);

        first.UpdateInternetAccess(true);
        second.UpdateInternetAccess(true);

        await Task.WhenAll(firstWait, secondWait).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RecoveryBeforeWaitIsRememberedAndRepeatedOnlineEventsAreIgnored()
    {
        using var network = new NetworkReconnectSignal(observeNetwork: false);
        var beforeRequest = network.Capture();
        network.UpdateInternetAccess(true);
        await NetworkReconnectSignal.WaitAsync(beforeRequest, TimeSpan.FromSeconds(30), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));

        var nextRequest = network.Capture();
        network.UpdateInternetAccess(true);
        Assert.False(nextRequest.IsCompleted);
        network.UpdateInternetAccess(false);
        Assert.False(nextRequest.IsCompleted);
        network.UpdateInternetAccess(true);
        Assert.True(nextRequest.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task RetryTimerStillExpiresWithoutRecovery()
    {
        using var network = new NetworkReconnectSignal(observeNetwork: false);
        var recovery = network.Capture();
        await NetworkReconnectSignal.WaitAsync(recovery, TimeSpan.Zero, CancellationToken.None);
        Assert.False(recovery.IsCompleted);
    }

    [Fact]
    public async Task StoppingCancelsRetryWait()
    {
        using var network = new NetworkReconnectSignal(observeNetwork: false);
        using var stop = new CancellationTokenSource();
        var wait = NetworkReconnectSignal.WaitAsync(network.Capture(), TimeSpan.FromSeconds(30), stop.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }
}
