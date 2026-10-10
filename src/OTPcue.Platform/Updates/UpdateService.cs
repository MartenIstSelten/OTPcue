namespace OTPcue.Platform;

internal interface IUpdateBackend
{
    bool IsInstalled
    {
        get;
    }
    string CurrentVersion
    {
        get;
    }
    string? AvailableVersion
    {
        get;
    }
    bool IsDownloaded
    {
        get;
    }
    Task CheckAsync();
    Task DownloadAsync(Action<int> progress, CancellationToken stop);
    void ScheduleRestart();
}

internal sealed record UpdateState(LocalizedText? Message, bool CanCheck, string? AvailableVersion = null,
    bool Busy = false, bool Ready = false, int? Progress = null);

internal sealed class UpdateService(IUpdateBackend backend)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    internal string CurrentVersion => backend.CurrentVersion;
    internal UpdateState State { get; private set; } = new(null, backend.IsInstalled);
    internal event Action<UpdateState>? Changed;

    internal async Task RunAsync(CancellationToken stop)
    {
        try
        {
            await CheckAsync(stop);
            if (!backend.IsInstalled)
            {
                return;
            }

            using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
            while (await timer.WaitForNextTickAsync(stop))
            {
                await CheckAsync(stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    internal async Task CheckAsync(CancellationToken stop)
    {
        if (!await gate.WaitAsync(0, stop))
        {
            return;
        }

        try
        {
            if (!backend.IsInstalled)
            {
                Publish(new(new LocalizedText(TextId.UpdatesAreAvailableAfterInstallingThroughSetup), false));
                return;
            }
            Publish(State with
            {
                Message = new LocalizedText(TextId.CheckingForUpdates),
                Busy = true
            });
            await backend.CheckAsync();
            stop.ThrowIfCancellationRequested();
            Publish(AvailableState());
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { State = AvailableState(); }
        catch (Exception exception)
        {
            AppDiagnostics.ReportFailure("Updates.Check", exception);
            if (!stop.IsCancellationRequested)
            {
                Publish(AvailableState() with
                {
                    Message = new LocalizedText(TextId.CouldNotCheckForUpdatesPleaseTryAgainLater)
                });
            }
        }
        finally { gate.Release(); }
    }

    internal async Task<bool> DownloadAsync(CancellationToken stop)
    {
        if (!await gate.WaitAsync(0, stop))
        {
            return false;
        }

        try
        {
            if (!backend.IsInstalled || backend.AvailableVersion is null)
            {
                return false;
            }

            if (!backend.IsDownloaded)
            {
                Publish(State with
                {
                    Message = new LocalizedText(TextId.DownloadingUpdate),
                    Busy = true,
                    Progress = 0
                });
                await backend.DownloadAsync(percent =>
                {
                    if (!stop.IsCancellationRequested)
                    {
                        Publish(State with
                        {
                            Progress = Math.Clamp(percent, 0, 100)
                        });
                    }
                }, stop);
            }
            stop.ThrowIfCancellationRequested();
            Publish(AvailableState());
            return backend.IsDownloaded;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            State = AvailableState();
            return false;
        }
        catch (Exception exception)
        {
            AppDiagnostics.ReportFailure("Updates.Download", exception);
            if (!stop.IsCancellationRequested)
            {
                Publish(AvailableState() with
                {
                    Message = new LocalizedText(TextId.UpdateCouldNotBeDownloadedPleaseTryAgain)
                });
            }

            return false;
        }
        finally { gate.Release(); }
    }

    internal void ScheduleRestart()
    {
        if (State.Busy || !backend.IsDownloaded)
        {
            throw new InvalidOperationException("Kein Update zur Installation bereit.");
        }

        backend.ScheduleRestart();
    }

    private UpdateState AvailableState()
        => new(backend.AvailableVersion is null ? new LocalizedText(TextId.TheAppIsUpToDate)
            : backend.IsDownloaded ? new LocalizedText(TextId.UpdateReadyTheAppWillRestartDuringInstallation)
            : new LocalizedText(TextId.UpdateAvailable, backend.AvailableVersion),
            backend.IsInstalled, backend.AvailableVersion, Ready: backend.IsDownloaded);

    private void Publish(UpdateState state)
    {
        State = state;
        Changed?.Invoke(state);
    }
}
