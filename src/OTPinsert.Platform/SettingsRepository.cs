using System.IO;
using System.Text.Json;

namespace OTPinsert.Platform;

internal sealed class SettingsRepository
{
    private readonly string file;
    public bool Exists => File.Exists(file);
    public SettingsRepository(string? directory = null) => file = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OTPinsert"), "settings.json");
    public AppSettings Load()
    {
        if (!File.Exists(file))
        {
            return new();
        }

        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file)) ?? throw new InvalidDataException("Ungültige Einstellungen.");
        settings.Validate();
        return settings;
    }
    public void Save(AppSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
            File.Move(temporary, file, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
