using OTPinsert.Platform;
using Microsoft.UI.Xaml;

namespace OTPinsert.WinUI;

public partial class App : Application
{
    private Window? _window;
    private Mutex? _singleInstance;
    private EventWaitHandle? _showWindow;
    private RegisteredWaitHandle? _showWindowWait;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var launchArguments = Environment.GetCommandLineArgs();
        var autostart = LaunchBehavior.IsAutostart(launchArguments);
        _showWindow = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\OTPinsert-WinUI-ShowWindow");
        _singleInstance = new Mutex(true, "Local\\OTPinsert-WinUI", out var first);
        if (!first)
        {
            if (!autostart)
            {
                _showWindow.Set();
            }

            _showWindow.Dispose();
            _singleInstance.Dispose();
            Exit();
            return;
        }
        var main = new MainWindow(launchArguments.Contains("--demo", StringComparer.OrdinalIgnoreCase));
        _window = main;
        _showWindowWait = ThreadPool.RegisterWaitForSingleObject(_showWindow,
            (_, _) => main.DispatcherQueue.TryEnqueue(main.ShowFromTray), null, Timeout.Infinite, executeOnlyOnce: false);
        main.Closed += (_, _) =>
        {
            _showWindowWait?.Unregister(null);
            _showWindow?.Dispose();
            _singleInstance?.Dispose();
        };
        if (LaunchBehavior.ShouldStartInTray(launchArguments, main.StartInTray))
        {
            main.HideToTray();
        }
        else
        {
            main.Activate();
        }
    }
}
