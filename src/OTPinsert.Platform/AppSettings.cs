using System.IO;

namespace OTPinsert.Platform;

internal sealed record AppSettings(int CountdownSeconds = 60, bool RemoveAfterInsert = true, bool OnlyCodeFields = false, bool StartWithWindows = true, bool StartInTray = true, bool CloseToTray = true, string Language = "system")
{
    public bool PlaySoundOnNewCode { get; init; } = true;
    public int SoundVolumePercent { get; init; } = 35;

    public bool AutoCopyCodes
    {
        get; init;
    }

    public void Validate()
    {
        if (SoundVolumePercent is < 0 or > 100)
        {
            throw new InvalidDataException("Sound volume must be between 0 and 100 percent.");
        }

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
