using System.IO;
using System.Text.Json;
using OTPcue.Core;
using OTPcue.Platform;
using Xunit;

public sealed class AutomaticCodeCopyTests
{
    [Fact]
    public void NewCodesCopyOnceWithoutRepeatedClipboardWrites()
    {
        var policy = new AutomaticCodeCopy();
        var first = Code("123456");
        Assert.True(policy.ShouldCopy(first, true));
        Assert.False(policy.ShouldCopy(first, true));
        Assert.True(policy.ShouldCopy(Code("654321"), true));
    }

    [Fact]
    public void EnablingOnlyCopiesCodesDetectedAfterward()
    {
        var policy = new AutomaticCodeCopy();
        var existing = Code("123456");
        Assert.False(policy.ShouldCopy(existing, false));
        Assert.False(policy.ShouldCopy(existing, true));
        Assert.True(policy.ShouldCopy(Code("654321"), true));
    }

    [Fact]
    public void ReactivatingAConsumedCodeDoesNotOverwriteClipboard()
    {
        var policy = new AutomaticCodeCopy();
        var code = Code("123456");
        Assert.True(policy.ShouldCopy(code, true));
        Assert.False(policy.ShouldCopy(null, true));
        Assert.False(policy.ShouldCopy(code, true));
    }

    [Fact]
    public void DisabledSettingDoesNotCopyNewCodes()
    {
        var policy = new AutomaticCodeCopy();
        Assert.False(policy.ShouldCopy(Code("123456"), false));
        Assert.False(policy.ShouldCopy(Code("654321"), false));
    }

    [Fact]
    public void OldSettingsKeepAutomaticCopyDisabled()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"CountdownSeconds\":90,\"Language\":\"de\"}");
        Assert.NotNull(settings);
        Assert.False(settings.AutoCopyCodes);
        Assert.Equal(90, settings.CountdownSeconds);
        Assert.False(new AppSettings().AutoCopyCodes);
    }

    [Fact]
    public void AutomaticCopyPreferenceSurvivesSavingAndLoading()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OTPcue-auto-copy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new SettingsRepository(directory);
            repository.Save(new AppSettings { AutoCopyCodes = true });
            Assert.True(repository.Load().AutoCopyCodes);
            repository.Save(new AppSettings());
            Assert.False(repository.Load().AutoCopyCodes);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static Otp Code(string value)
        => new(Guid.NewGuid(), value, "Test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1));
}
