using System.IO;

namespace OTPcue.Platform;

internal sealed record AppSettings(int CountdownSeconds = 60, bool RemoveAfterInsert = true, bool OnlyCodeFields = false, bool StartWithWindows = true, bool StartInTray = true, bool CloseToTray = true, string Language = "system")
{
    public bool AutoCopyCodes
    {
        get; init;
    }

    public void Validate()
    {
        if (Language is not ("system" or "de" or "en"))
        {
            throw new InvalidDataException("Unsupported language preference.");
        }

        if (CountdownSeconds is < 15 or > 600)
        {
            throw new InvalidDataException("Countdown muss zwischen 15 und 600 Sekunden liegen.");
        }
    }
}
