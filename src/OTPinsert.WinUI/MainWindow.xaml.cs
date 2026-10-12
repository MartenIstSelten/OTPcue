using OTPinsert.Platform;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OTPinsert.Core;
using System.Collections.ObjectModel;
using WinRT.Interop;
using Windows.Networking.Connectivity;
using Forms = System.Windows.Forms;

namespace OTPinsert.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly AutofillController _autofill;
    private readonly OtpStore _store = new();
    private readonly AccountRepository _repository = new();
    private readonly SettingsRepository _settingsRepository = new();
    private readonly SettingsService _settingsService;
    private readonly CodeNotificationSound _codeNotificationSound = new();
    private AppSettings _settings = new();
    private readonly bool _firstRun;
    private readonly ObservableCollection<AccountView> _accounts = new();
    private readonly AccountMonitor _monitor;
    private readonly AppWindow _appWindow;
    private readonly Forms.NotifyIcon _tray;
    private readonly System.Drawing.Icon _trayIcon;
    private bool _closed, _demo, _busy, _exiting;
    private bool _storageReady = true;
    private bool _offline;
    private AccountView? Selected => AccountList.SelectedItem as AccountView;

    public MainWindow(bool demo = false)
    {
        InitializeComponent();
        InitializeCodeCopy();
        _monitor = new AccountMonitor((account, status, stop) => AccountPolling.RunAsync(_store, account, status, stop));
        _settingsService = new SettingsService(_settingsRepository.Save, WindowsStartup.GetCommand,
            WindowsStartup.SetEnabled, WindowsStartup.SetCommand);
        SystemBackdrop = new MicaBackdrop();
        _demo = demo;
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this)));
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "OTPinsert.ico");
        _appWindow.SetIcon(iconPath);
        _trayIcon = new System.Drawing.Icon(iconPath, Forms.SystemInformation.SmallIconSize);
        _appWindow.TitleBar.PreferredTheme = TitleBarTheme.Dark;
        _appWindow.Resize(new Windows.Graphics.SizeInt32(1080, 760));
        AccountList.ItemsSource = _accounts;
        try
        {
            _firstRun = !_settingsRepository.Exists;
            _settings = _settingsRepository.Load();
            if (_firstRun && !_demo)
            {
                _settingsService.Save(_settings);
            }
        }
        catch (Exception exception)
        {
            AppDiagnostics.ReportFailure("Settings.Load", exception);
            SetSettingsStatus(new(TextId.SettingsReadFailed));
        }
        try
        {
            if (!_demo && !string.Equals(WindowsStartup.GetCommand(), _settings.StartWithWindows ? WindowsStartup.GetEnabledCommand() : null, StringComparison.Ordinal))
            {
                WindowsStartup.SetEnabled(_settings.StartWithWindows);
            }
        }
        catch (Exception exception)
        {
            AppDiagnostics.ReportFailure("Settings.SyncStartup", exception);
            SetSettingsStatus(new(TextId.StartupSyncFailed));
        }
        Localization.Configure(_settings.Language);
        _store.Countdown = TimeSpan.FromSeconds(_settings.CountdownSeconds);
        PopulateSettings(_settings);
        _store.CodeAdded += OnCodeAdded;
        try
        {
            foreach (var account in _repository.Load())
            {
                _accounts.Add(new AccountView(account));
            }
        }
        catch (Exception ex)
        {
            _storageReady = false;
            SetStatus(new(TextId.AccountStorageUnavailable, ex.GetType().Name));
        }
        _autofill = new AutofillController(_store, DispatcherQueue, OnUi,
            () => _settings, () => _updating || _exiting, RenderCode,
            SetStatus, ClearCode);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Localization.Text(TextId.Open), null, (_, _) => OnUi(ShowFromTray));
        menu.Items.Add(Localization.Text(TextId.PauseResumeAutofill), null, (_, _) => OnUi(TogglePause));
        menu.Items.Add(Localization.Text(TextId.DiscardCode), null, (_, _) => OnUi(ClearCode));
        menu.Items.Add(Localization.Text(TextId.Exit), null, (_, _) => OnUi(() => _ = ExitAsync()));
        _tray = new Forms.NotifyIcon { Icon = _trayIcon, Text = "OTPinsert", Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => OnUi(ShowFromTray);
        _appWindow.Closing += (_, args) =>
        {
            if (_exiting)
            {
                return;
            }

            args.Cancel = true;
            if (_settings.CloseToTray)
            {
                _appWindow.Hide();
            }
            else
            {
                BeginExit();
            }
        };
        UpdateModeUi();
        UpdateDetails();
        if (_demo)
        {
            GenerateCode();
        }
        else
        {
            foreach (var account in _accounts)
            {
                StartAccount(account);
            }
        }
        InitializeUpdates();
        ApplyLanguage();
        NetworkInformation.NetworkStatusChanged += NetworkStatusChanged;
        UpdateNetworkStatus();
    }

    private void NetworkStatusChanged(object sender) => OnUi(UpdateNetworkStatus);

    private void UpdateNetworkStatus()
    {
        var online = NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel()
            == NetworkConnectivityLevel.InternetAccess;
        _offline = !online;
        UpdateFooterStatus();
    }

    private void UpdateFooterStatus()
    {
        var settings = SettingsScreen.Visibility == Visibility.Visible;
        OfflineStatus.Visibility = _offline ? Visibility.Visible : Visibility.Collapsed;
        StatusLabel.Visibility = !_offline && !settings ? Visibility.Visible : Visibility.Collapsed;
        SettingsFooterButtons.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(SettingsFooterButtons, _offline ? 1 : 0);
    }

    private void OnUi(Action action) => DispatcherQueue.TryEnqueue(() => { if (!_closed) { action(); } });
    private void OnCodeAdded()
    {
        if (_settings.PlaySoundOnNewCode)
        {
            OnUi(() =>
            {
                if (!_exiting)
                {
                    _codeNotificationSound.Play();
                }
            });
        }
    }
    private void BeginExit() => _ = ExitAsync();
    internal bool StartInTray => !_firstRun && _settings.StartInTray;
    internal void HideToTray() => _appWindow.Hide();
    internal void ShowFromTray()
    {
        _appWindow.Show();
        if (_appWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }

        Activate();
    }

    private async void DemoClicked(object sender, RoutedEventArgs e) => await ChangeAsync(async () =>
    {
        await _monitor.StopAllAsync();
        _store.Clear();
        _autofill.ResetTarget();
        _demo = !_demo;
        UpdateModeUi();
        foreach (var view in _accounts)
        {
            StartAccount(view);
        }

        if (_demo)
        {
            GenerateCode();
        }
        else
        {
            _statusMessage = null;
            StatusLabel.Text = "";
        }
    });

    private void UpdateModeUi()
    {
        DemoButton.Content = _demo ? Localization.Text(TextId.StopDemo) : Localization.Text(TextId.StartDemo);
        GenerateButton.Visibility = _demo ? Visibility.Visible : Visibility.Collapsed;
    }
    private void GenerateCode()
    {
        if (!_demo)
        {
            return;
        }

        _store.Clear();
        _store.Add("482913", Localization.Text(TextId.DemoCode), DateTimeOffset.UtcNow);
        _autofill.RefreshTarget();
        SetStatus(new(TextId.DemoActiveClickAnEmptyTextFieldInAnotherApp));
        UpdatePopup();
    }
    private void GenerateClicked(object sender, RoutedEventArgs e) => GenerateCode();
    private void ClearClicked(object sender, RoutedEventArgs e) => ClearCode();
    private void ClearCode()
    {
        _autofill.Discard();
        SetStatus(new(TextId.CurrentCodeDiscarded));
        UpdatePopup();
    }
    private void PauseClicked(object sender, RoutedEventArgs e) => TogglePause();
    private void TogglePause()
    {
        _autofill.TogglePause();
        PauseButton.Content = _autofill.IsPaused ? Localization.Text(TextId.ResumeAutofill) : Localization.Text(TextId.PauseAutofill);
        PausedStatus.Visibility = _autofill.IsPaused ? Visibility.Visible : Visibility.Collapsed;
        UpdatePopup();
    }

    private void UpdatePopup() => _autofill.Refresh();

    private void RenderCode(Otp? code)
    {
        UpdateUpdateUi();
        CodeLabel.Text = code is null ? Localization.Text(TextId.NoCodeYet) : code.Code;
        var noAccounts = !_demo && _storageReady && _accounts.Count == 0;
        NoCodePlaceholder.Text = Localization.Text(noAccounts ? TextId.AddAnAccountToStart : TextId.NoCodeYet);
        UpdateCodeCopy(code);
        SourceLabel.Text = code?.Source ?? (_demo ? Localization.Text(TextId.RefreshTheDemoCodeToContinueTesting) : Localization.Text(TextId.NewCodeEmailsAppearHereAutomatically));
        if (code is null && noAccounts)
        {
            SourceLabel.Text = Localization.Text(TextId.AddAccountEmptyCodeHelp);
        }
        ExpiryLabel.Text = code is null ? "" : Localization.Format(TextId.CountdownRemaining, Math.Max(0, (int)Math.Ceiling((code.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds)));
        ClearButton.IsEnabled = code is not null;
        if (_automaticCodeCopy.ShouldCopy(code, _settings.AutoCopyCodes) && code is not null)
        {
            CopyCodeToClipboard(code);
        }
    }

    private async void ExitClicked(object sender, RoutedEventArgs e) => await ExitAsync();
    private async Task ExitAsync()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _closed = true;
        var failures = await Shutdown.RunAsync(
            [() => NetworkInformation.NetworkStatusChanged -= NetworkStatusChanged,
                () => _store.CodeAdded -= OnCodeAdded, _copyFeedbackTimer.Stop, _store.Clear, _updateStop.Cancel, _autofill.Stop],
            _monitor.StopAllAsync,
            [_codeNotificationSound.Dispose, _tray.Dispose, _trayIcon.Dispose, _autofill.ClosePopup, Close, Application.Current.Exit]);
        foreach (var failure in failures)
        {
            System.Diagnostics.Trace.WriteLine($"Shutdown cleanup failed ({failure.GetType().Name}).");
        }
    }
}
