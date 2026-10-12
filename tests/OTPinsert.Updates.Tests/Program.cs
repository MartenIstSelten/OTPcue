using OTPinsert.Platform;
using Velopack;
using Velopack.Locators;
using System.Text.Json;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var tests = new UpdatesOfflineTests();
        tests.LocalTokenIsEncryptedBoundToRepositoryAndToleratesCorruption();
        await tests.DeveloperBuildDoesNotCheckForUpdates();
        await tests.UpdateRequiresDownloadAndExplicitRestartAndRemainsRetryable();
        await tests.ConcurrentChecksAreCoalescedAndBlockDownload();
        await tests.ShutdownSuppressesLateNotifications();
        tests.RealBackendRecognizesUninstalledBuild();

        if (args.Length == 2 && args[0] == "--package-feed")
        {
            var directory = Path.GetFullPath(Path.Combine("artifacts", "update-tests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory);
            var locator = new TestVelopackLocator("OTPinsert", "0.0.1", directory);
            var manager = new UpdateManager(Path.GetFullPath(args[1]), new UpdateOptions { ExplicitChannel = "win-x64" }, locator);
            var real = new VelopackUpdateBackend(manager);
            var realService = new UpdateService(real);
            await realService.CheckAsync(CancellationToken.None);
            UpdatesOfflineTests.Check(realService.State.AvailableVersion is not null, "Real release feed exposes a newer installer version");
            UpdatesOfflineTests.Check(await realService.DownloadAsync(CancellationToken.None) && realService.State.Ready,
                "Real update package downloads and passes Velopack checksum validation");
            UpdatesOfflineTests.Check(Directory.GetFiles(directory, "*-full.nupkg").Length == 1, "Package stored in isolated test directory");
            var corruptFeed = Path.Combine(directory, "corrupt-feed");
            Directory.CreateDirectory(corruptFeed);
            var feedFile = Path.Combine(Path.GetFullPath(args[1]), "releases.win-x64.json");
            File.Copy(feedFile, Path.Combine(corruptFeed, "releases.win-x64.json"));
            using var feed = JsonDocument.Parse(File.ReadAllText(feedFile));
            foreach (var asset in feed.RootElement.GetProperty("Assets").EnumerateArray())
            {
                var filename = asset.GetProperty("FileName").GetString()!;
                File.WriteAllText(Path.Combine(corruptFeed, filename), "corrupt test payload");
            }
            var rejectedDirectory = Path.Combine(directory, "rejected-download");
            Directory.CreateDirectory(rejectedDirectory);
            var badManager = new UpdateManager(corruptFeed, new UpdateOptions { ExplicitChannel = "win-x64" },
                new TestVelopackLocator("OTPinsert", "0.0.1", rejectedDirectory));
            var badService = new UpdateService(new VelopackUpdateBackend(badManager));
            await badService.CheckAsync(CancellationToken.None);
            UpdatesOfflineTests.Check(!await badService.DownloadAsync(CancellationToken.None) && !badService.State.Ready,
                "Corrupt real package fails validation and cannot become installable");
            // No updater is launched and no existing app or user data is modified.
        }
    }
}

sealed class FakeBackend : IUpdateBackend
{
    public bool IsInstalled { get; set; } = true;
    public string CurrentVersion => "0.1.0";
    public string? AvailableVersion
    {
        get; private set;
    }
    public bool IsDownloaded
    {
        get; private set;
    }
    internal string? NextVersion
    {
        get; set;
    }
    internal bool FailCheck
    {
        get; set;
    }
    internal bool FailDownload
    {
        get; set;
    }
    internal TaskCompletionSource? CheckHold
    {
        get; set;
    }
    internal int Checks
    {
        get; private set;
    }
    internal int Downloads
    {
        get; private set;
    }
    internal int Restarts
    {
        get; private set;
    }

    public async Task CheckAsync()
    {
        Checks++;
        if (CheckHold is not null)
        {
            await CheckHold.Task;
        }

        if (FailCheck)
        {
            throw new IOException("PRIVATE token or network detail");
        }

        AvailableVersion = NextVersion;
    }
    public Task DownloadAsync(Action<int> progress, CancellationToken stop)
    {
        Downloads++;
        stop.ThrowIfCancellationRequested();
        if (FailDownload)
        {
            throw new IOException("PRIVATE download detail");
        }

        progress(50);
        IsDownloaded = true;
        return Task.CompletedTask;
    }
    public void ScheduleRestart() => Restarts++;
}
