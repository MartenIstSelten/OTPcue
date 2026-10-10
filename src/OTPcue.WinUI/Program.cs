using OTPcue.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace OTPcue.WinUI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Installer hooks must run before WinUI and before the single-instance mutex.
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        StartWinUI();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void StartWinUI()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
}
