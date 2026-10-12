namespace OTPinsert.Platform;

internal static class LaunchBehavior
{
    internal const string AutostartArgument = "--autostart";

    internal static bool IsAutostart(IEnumerable<string> arguments) =>
        arguments.Contains(AutostartArgument, StringComparer.OrdinalIgnoreCase);

    internal static bool ShouldStartInTray(IEnumerable<string> arguments, bool startInTray) =>
        startInTray && IsAutostart(arguments);
}
