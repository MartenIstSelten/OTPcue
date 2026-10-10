using OTPcue.Platform;
using Microsoft.UI.Xaml;

namespace OTPcue.WinUI;

public sealed partial class MainWindow
{
    private readonly UpdateService _updates = new(new VelopackUpdateBackend(applicationAssembly: typeof(MainWindow).Assembly));
    private readonly CancellationTokenSource _updateStop = new();
    private bool _updating;
    private LocalizedText? _updateActionError;

    private void InitializeUpdates()
    {
        VersionLabel.Text = "Version " + _updates.CurrentVersion;
        _updates.Changed += _ => OnUi(UpdateUpdateUi);
        UpdateUpdateUi();
        _ = _updates.RunAsync(_updateStop.Token);
    }

    private void UpdateUpdateUi()
    {
        var state = _updates.State;
        UpdateStatusLabel.Text = (_updateActionError ?? state.Message)?.ToString() ?? "";
        CheckUpdateButton.IsEnabled = state.CanCheck && !state.Busy && !_updating && !_exiting;
        InstallUpdateButton.Visibility = state.AvailableVersion is null ? Visibility.Collapsed : Visibility.Visible;
        InstallUpdateButton.IsEnabled = !state.Busy && !_updating && !_busy && !_autofill.IsFilling && !_exiting;
        InstallUpdateButton.Content = state.Ready ? Localization.Text(TextId.InstallAndRestart) : Localization.Text(TextId.UpdateNow);
        MainUpdateNotice.Visibility = state.AvailableVersion is not null && SettingsScreen.Visibility != Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        MainUpdateLabel.Text = UpdateStatusLabel.Text;
        MainInstallUpdateButton.Content = InstallUpdateButton.Content;
        MainInstallUpdateButton.IsEnabled = InstallUpdateButton.IsEnabled;
        UpdateProgress.Visibility = state.Progress is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateProgress.Value = state.Progress ?? 0;
    }

    private async void CheckUpdateClicked(object sender, RoutedEventArgs e)
    {
        _updateActionError = null;
        try
        {
            await _updates.CheckAsync(_updateStop.Token);
        }
        catch (OperationCanceledException) when (_updateStop.IsCancellationRequested) { }
    }

    private async void InstallUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (_updating || _busy || _autofill.IsFilling || _exiting)
        {
            return;
        }

        _updating = true;
        _updateActionError = null;
        UpdateDetails();
        UpdateUpdateUi();
        try
        {
            if (!await _updates.DownloadAsync(_updateStop.Token) || _exiting)
            {
                return;
            }

            _updates.ScheduleRestart();
            await ExitAsync();
        }
        catch (OperationCanceledException) when (_updateStop.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppDiagnostics.ReportFailure("Updates.Restart", exception);
            _updateActionError = new(TextId.RestartForTheUpdateFailedPleaseTryAgain);
            UpdateStatusLabel.Text = _updateActionError.ToString();
        }
        finally
        {
            _updating = false;
            if (!_exiting)
            {
                UpdateDetails();
                UpdateUpdateUi();
            }
        }
    }
}
