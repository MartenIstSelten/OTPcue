using OTPinsert.Platform;
using Velopack;
using Velopack.Locators;
using System.Text.Json;
using Xunit;

public sealed class UpdatesOfflineTests
{
    internal static void Check(bool value, string name) => Assert.True(value, name);

    [Fact]
    public void LocalTokenIsEncryptedBoundToRepositoryAndToleratesCorruption()
    {
        var tokenDirectory = Path.Combine(Path.GetTempPath(), "OTPinsert-update-auth-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tokenDirectory);
        const string tokenRepo = "https://github.com/example/private-test";
        var tokenPath = LocalUpdateToken.TokenPath(tokenRepo, tokenDirectory);
        try
        {
            Check(LocalUpdateToken.Load(tokenRepo, tokenDirectory) is null, "Missing token permits anonymous updates");
            var encrypted = System.Security.Cryptography.ProtectedData.Protect(
                System.Text.Encoding.UTF8.GetBytes("synthetic-test-token"), LocalUpdateToken.RepositoryKey(tokenRepo),
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            File.WriteAllBytes(tokenPath, encrypted);
            Check(LocalUpdateToken.Load(tokenRepo.ToUpperInvariant() + "/", tokenDirectory) == "synthetic-test-token",
                "Local DPAPI token roundtrips and normalizes repository URL");
            Check(LocalUpdateToken.Load("https://github.com/example/other", tokenDirectory) is null,
                "Token is not used for another repository");
            File.Copy(tokenPath, LocalUpdateToken.TokenPath("https://github.com/example/other", tokenDirectory));
            Check(LocalUpdateToken.Load("https://github.com/example/other", tokenDirectory) is null,
                "DPAPI repository binding rejects copied credentials");
            File.WriteAllText(tokenPath, "invalid encrypted data");
            Check(LocalUpdateToken.Load(tokenRepo, tokenDirectory) is null, "Corrupt token does not prevent startup");
        }
        finally
        {
            foreach (var file in Directory.GetFiles(tokenDirectory))
            {
                File.Delete(file);
            }

            Directory.Delete(tokenDirectory);
        }
    }

    [Fact]
    public async Task DeveloperBuildDoesNotCheckForUpdates()
    {
        var backend = new FakeBackend { IsInstalled = false };
        var service = new UpdateService(backend);
        await service.RunAsync(CancellationToken.None);
        Check(!service.State.CanCheck && backend.Checks == 0, "Developer build never requests or installs updates");
    }

    [Fact]
    public async Task UpdateRequiresDownloadAndExplicitRestartAndRemainsRetryable()
    {
        var backend = new FakeBackend();
        var service = new UpdateService(backend);
        await service.CheckAsync(CancellationToken.None);
        Check(service.State.CanCheck && service.State.AvailableVersion is null && !service.State.Busy,
            "No update leaves manual checking available");
        backend.NextVersion = "0.2.0";
        await service.CheckAsync(CancellationToken.None);
        Check(service.State.AvailableVersion == "0.2.0" && !service.State.Ready && backend.Downloads == 0,
            "Discovering an update does not download or restart without a click");
        var blocked = false;
        try
        {
            service.ScheduleRestart();
        }
        catch (InvalidOperationException) { blocked = true; }
        Check(blocked && backend.Restarts == 0, "Cannot restart before a verified download");
        backend.FailDownload = true;
        Check(!await service.DownloadAsync(CancellationToken.None) && !service.State.Busy && service.State.AvailableVersion == "0.2.0",
            "Failed download remains retryable and cannot restart");
        backend.FailDownload = false;
        var progress = new List<int>();
        service.Changed += state => { if (state.Progress is { } percent) { progress.Add(percent); } };
        Check(await service.DownloadAsync(CancellationToken.None) && service.State.Ready && progress.Contains(50),
            "Successful download reports progress and becomes ready");
        service.ScheduleRestart();
        Check(backend.Restarts == 1, "Restart is scheduled only on explicit request");
        var downloads = backend.Downloads;
        Check(await service.DownloadAsync(CancellationToken.None) && backend.Downloads == downloads,
            "A prepared update is reused without another download");
        backend.FailCheck = true;
        await service.CheckAsync(CancellationToken.None);
        Check(service.State.Ready && !service.State.Busy && !(service.State.Message?.ToString().Contains("PRIVATE") ?? false),
            "Network failure preserves prepared update and hides exception details");
    }

    [Fact]
    public async Task ConcurrentChecksAreCoalescedAndBlockDownload()
    {
        var backend = new FakeBackend { NextVersion = "0.3.0", CheckHold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = new UpdateService(backend);
        var firstCheck = service.CheckAsync(CancellationToken.None);
        await service.CheckAsync(CancellationToken.None);
        Check(backend.Checks == 1 && service.State.Busy, "Concurrent update checks are coalesced");
        Check(!await service.DownloadAsync(CancellationToken.None), "Download cannot race an active check");
        backend.CheckHold.SetResult();
        await firstCheck;
    }

    [Fact]
    public async Task ShutdownSuppressesLateNotifications()
    {
        using (var stop = new CancellationTokenSource())
        {
            var backend = new FakeBackend { NextVersion = "0.4.0", CheckHold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            var service = new UpdateService(backend);
            var notifications = 0;
            service.Changed += _ => notifications++;
            var check = service.CheckAsync(stop.Token);
            var beforeStop = notifications;
            stop.Cancel();
            backend.CheckHold.SetResult();
            await check;
            Check(notifications == beforeStop, "Shutdown suppresses late update-check notifications");
        }
    }

    [Fact]
    public void RealBackendRecognizesUninstalledBuild()
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        var developer = new VelopackUpdateBackend(applicationAssembly: typeof(UpdatesOfflineTests).Assembly);
        Check(!developer.IsInstalled, "Real Velopack backend detects an uninstalled test build");
    }
}
