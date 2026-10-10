using OTPcue.Platform;
using Xunit;

public sealed class LaunchBehaviorTests
{
    [Theory]
    [InlineData("--autostart", true, true)]
    [InlineData("--AUTOSTART", true, true)]
    [InlineData("--autostart", false, false)]
    [InlineData("--demo", true, false)]
    [InlineData("", true, false)]
    public void OnlyAutostartHonorsTrayPreference(string argument, bool preference, bool expected)
        => Assert.Equal(expected, LaunchBehavior.ShouldStartInTray([argument], preference));
}
