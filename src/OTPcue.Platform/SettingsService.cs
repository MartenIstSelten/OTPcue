namespace OTPcue.Platform;

internal sealed class SettingsService(
    Action<AppSettings> save,
    Func<string?> getStartupCommand,
    Action<bool> setStartupEnabled,
    Action<string?> restoreStartupCommand)
{
    internal void Save(AppSettings settings)
    {
        settings.Validate();
        // Read before changing anything. A failed read must never delete an existing entry.
        var previousCommand = getStartupCommand();
        try
        {
            setStartupEnabled(settings.StartWithWindows);
            save(settings);
        }
        catch (Exception saveError)
        {
            try
            {
                restoreStartupCommand(previousCommand);
            }
            catch (Exception restoreError)
            {
                throw new AggregateException("Speichern und Wiederherstellen des Autostarts fehlgeschlagen.",
                    saveError, restoreError);
            }

            throw;
        }
    }
}
