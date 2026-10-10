using Microsoft.Win32;

namespace OTPcue.Platform;

internal static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OTPcue";
    public static string? GetCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) as string;
    }
    public static void SetEnabled(bool enabled)
    {
        var command = enabled
            ? GetEnabledCommand()
            : null;
        SetCommand(command);
    }

    internal static void SetCommand(string? command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Windows-Autostart konnte nicht geöffnet werden.");
        if (command is null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(ValueName, command);
        }
        // Remove the previous app's entry after applying the current preference.
        key.DeleteValue("OtpAutofill", throwOnMissingValue: false);
    }

    internal static string GetEnabledCommand()
    {
        if (Velopack.Locators.VelopackLocator.IsCurrentSet)
        {
            var locator = Velopack.Locators.VelopackLocator.Current;
            if (locator.CurrentlyInstalledVersion is not null && locator.UpdateExePath is { } updater)
            {
                return $"\"{updater}\" start \"OTPcue.WinUI.exe\" -- {LaunchBehavior.AutostartArgument}";
            }
        }
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Programmpfad konnte nicht ermittelt werden.");
        return $"\"{executable}\" {LaunchBehavior.AutostartArgument}";
    }
}
