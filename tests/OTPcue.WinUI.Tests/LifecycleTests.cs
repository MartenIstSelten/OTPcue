using System.IO;
using System.Globalization;
using OTPcue.Platform;
using Xunit;

public sealed class LifecycleTests
{
    [Fact]
    public async Task ShutdownReleasesEverythingAfterWorkerAndCleanupFailures()
    {
        var calls = new List<string>();
        var workerFailure = new IOException("synthetic worker failure");
        var cleanupFailure = new InvalidOperationException("synthetic cleanup failure");
        var failures = await Shutdown.RunAsync(
            [() => calls.Add("stop")],
            () => { calls.Add("workers"); return Task.FromException(workerFailure); },
            [() => { calls.Add("first"); throw cleanupFailure; }, () => calls.Add("last")]);

        Assert.Equal(new[] { "stop", "workers", "first", "last" }, calls);
        Assert.Equal(new Exception[] { workerFailure, cleanupFailure }, failures);
    }

    [Fact]
    public async Task CancellationCallbackFailureStillStopsOtherAccountsAndAllowsRestart()
    {
        var first = new MailAccount(Guid.NewGuid(), "Test", "imap.example.test", 993, "test", "test", false, true);
        var second = first with
        {
            Id = Guid.NewGuid()
        };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        var monitor = new AccountMonitor(async (account, _, stop) =>
        {
            using var registration = stop.Register(() =>
            {
                if (account.Id == first.Id)
                {
                    throw new InvalidOperationException("synthetic callback failure");
                }
            });
            if (Interlocked.Increment(ref starts) == 2)
            {
                started.TrySetResult();
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stop);
            }
            finally
            {
                if (account.Id == second.Id)
                {
                    stopped.TrySetResult();
                }
            }
        });
        try
        {
            monitor.Start(first, (_, _) => { });
            monitor.Start(second, (_, _) => { });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<AggregateException>(() => monitor.StopAllAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            monitor.Start(second, (_, _) => { });
            await monitor.StopAllAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { await monitor.StopAllAsync(); }
    }

    [Fact]
    public async Task FaultedWorkersAreObservedAndDoNotPreventOtherWorkersStopping()
    {
        var account = new MailAccount(Guid.NewGuid(), "Test", "imap.example.test", 993, "test", "test", false, true);
        var fault = new IOException("synthetic worker failure");
        var monitor = new AccountMonitor((_, _, _) => Task.FromException(fault));
        monitor.Start(account, (_, _) => { });
        var failure = await Assert.ThrowsAsync<AggregateException>(() => monitor.StopAllAsync());
        Assert.Contains(fault, failure.InnerExceptions);
        await monitor.StopAllAsync();
    }

    [Fact]
    public void LanguageChangesPreserveMessageArguments()
    {
        var message = new LocalizedText(TextId.MailConnected, 3);
        Localization.Configure("en");
        Assert.Equal("Connected · New emails are checked every 3 seconds.", message.ToString());
        Localization.Configure("de");
        Assert.Equal("Verbunden · Neue E-Mails werden alle 3 Sekunden geprüft.", message.ToString());
    }

    [Fact]
    public void EveryResourceHasMatchingFormatArgumentsInBothLanguages()
    {
        var manager = new System.Resources.ResourceManager("OTPcue.Platform.Resources.Strings", typeof(LocalizedText).Assembly);
        var englishResources = manager.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false);
        Assert.NotNull(englishResources);
        foreach (var id in Enum.GetValues<TextId>())
        {
            var german = Localization.Get(id, CultureInfo.GetCultureInfo("de"));
            var english = Localization.Get(id, CultureInfo.GetCultureInfo("en"));
            Assert.False(string.IsNullOrWhiteSpace(german), id.ToString());
            Assert.False(string.IsNullOrWhiteSpace(english), id.ToString());
            Assert.Equal(english, englishResources.GetString(id.ToString()));
            Assert.Equal(System.Text.CompositeFormat.Parse(german).MinimumArgumentCount,
                System.Text.CompositeFormat.Parse(english).MinimumArgumentCount);
        }
    }
}
