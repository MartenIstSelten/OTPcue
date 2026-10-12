using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace OTPinsert.Platform;

internal sealed class VelopackUpdateBackend : IUpdateBackend
{
    private readonly UpdateManager manager;
    private UpdateInfo? available;
    private VelopackAsset? downloaded;

    internal VelopackUpdateBackend(UpdateManager? updateManager = null, Assembly? applicationAssembly = null)
    {
        var assembly = applicationAssembly ?? Assembly.GetEntryAssembly() ?? typeof(VelopackUpdateBackend).Assembly;
        manager = updateManager ?? CreateManager(assembly);
        downloaded = manager.UpdatePendingRestart;
        CurrentVersion = manager.CurrentVersion?.ToString()
            ?? assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? "0.1.0";
    }

    private static UpdateManager CreateManager(Assembly assembly)
    {
        var repository = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "UpdateRepositoryUrl").Value!;
        return new UpdateManager(new GithubSource(repository, accessToken: LocalUpdateToken.Load(repository), prerelease: false),
            new UpdateOptions { ExplicitChannel = "win-x64" });
    }

    public bool IsInstalled => manager.IsInstalled;
    public string CurrentVersion
    {
        get;
    }
    public string? AvailableVersion => available?.TargetFullRelease.Version.ToString() ?? downloaded?.Version.ToString();
    public bool IsDownloaded => downloaded is not null
        && (available is null || downloaded.Version == available.TargetFullRelease.Version);

    public async Task CheckAsync()
    {
        var result = await manager.CheckForUpdatesAsync();
        available = result;
    }

    public async Task DownloadAsync(Action<int> progress, CancellationToken stop)
    {
        var update = available ?? throw new InvalidOperationException("Kein Update verfügbar.");
        await manager.DownloadUpdatesAsync(update, progress, stop);
        downloaded = update.TargetFullRelease;
    }

    public void ScheduleRestart()
    {
        if (!IsDownloaded)
        {
            throw new InvalidOperationException("Update ist nicht heruntergeladen.");
        }
        // Wait for normal cleanup, including mail connections, popup, tray and the mutex.
        manager.WaitExitThenApplyUpdates(downloaded, silent: false, restart: true);
    }
}
