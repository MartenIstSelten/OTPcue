using System.Text.Json;
using OTPinsert.Platform;
using Xunit;

public sealed class OtpSuggestionSettingsTests
{
    [Fact]
    public void ExistingSettingsKeepSuggestionsEnabled()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"CountdownSeconds\":90}");
        Assert.NotNull(settings);
        Assert.True(settings.ShowOtpSuggestions);
        Assert.True(new AppSettings().ShowOtpSuggestions);
    }

    [Fact]
    public void DisabledSuggestionsSurviveSavingAndLoading()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OTPinsert-suggestions-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new SettingsRepository(directory);
            repository.Save(new AppSettings { ShowOtpSuggestions = false });
            Assert.False(repository.Load().ShowOtpSuggestions);
            repository.Save(new AppSettings());
            Assert.True(repository.Load().ShowOtpSuggestions);
        }
        finally
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, true);
            }
        }
    }
}
