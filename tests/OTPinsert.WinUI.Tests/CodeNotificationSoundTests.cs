using System.IO;
using System.Text.Json;
using OTPinsert.Platform;
using Xunit;

public sealed class CodeNotificationSoundTests
{
    [Fact]
    public void SoundIsEnabledByDefaultIncludingForOldSettings()
    {
        Assert.True(new AppSettings().PlaySoundOnNewCode);
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"CountdownSeconds\":90,\"Language\":\"de\"}");
        Assert.NotNull(settings);
        Assert.True(settings.PlaySoundOnNewCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SoundPreferenceSurvivesSavingAndLoading(bool enabled)
    {
        var directory = Path.Combine(Path.GetTempPath(), "OTPinsert-sound-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new SettingsRepository(directory);
            repository.Save(new AppSettings { PlaySoundOnNewCode = enabled });
            Assert.Equal(enabled, repository.Load().PlaySoundOnNewCode);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void BundledChimeIsAReadableWaveFile()
    {
        using var stream = typeof(AppSettings).Assembly.GetManifestResourceStream(
            "OTPinsert.Platform.Resources.NewCode.wav");
        Assert.NotNull(stream);
        using var reader = new BinaryReader(stream);
        Assert.Equal("RIFF", new string(reader.ReadChars(4)));
        Assert.Equal(stream.Length - 8, (long)reader.ReadUInt32());
        Assert.Equal("WAVE", new string(reader.ReadChars(4)));
    }
}
