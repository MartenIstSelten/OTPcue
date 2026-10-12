using OTPinsert.Core;
using Xunit;

public sealed class OtpDetectorTests
{
    [Theory]
    [InlineData("Discord verification code", "Your verification code is 123456.", "123456")]
    [InlineData("Anmeldung", "Dein Sicherheitscode: 87654321", "87654321")]
    [InlineData("Rechnung", "123456 EUR", null)]
    [InlineData("OTP", "123456789", null)]
    [InlineData("OTP", "A123456B", null)]
    [InlineData("OTP", "1234 oder 5678", null)]
    [InlineData("OTP 123456", "Code: 123456", "123456")]
    [InlineData("Sign-in code", "123 456", "123456")]
    [InlineData("One-time password", "123-456", "123456")]
    [InlineData("Your passcode", "123\u00a0456", "123456")]
    [InlineData("Authentication code", "123\u202f456", "123456")]
    [InlineData("Einmalpasswort", "00123456", "00123456")]
    [InlineData("Authentifizierungscode", "123456", "123456")]
    [InlineData("OTP 123456", "Your OTP: 123-456", "123456")]
    [InlineData("OTP", "123-456 or 654321", null)]
    [InlineData("Order confirmation", "Order 123-456", null)]
    [InlineData("Login", "Invoice 123456", null)]
    [InlineData("OTP", "A123-456B", null)]
    public void ExtractsOnlyUnambiguousCodes(string subject, string body, string? expected)
        => Assert.Equal(expected, OtpDetector.Extract(subject, body));

    [Fact]
    public void RejectsNumbersFarFromTheCodeLabel()
        => Assert.Null(OtpDetector.Extract("", "OTP" + new string(' ', 200) + "123456"));

    [Theory]
    [InlineData(14)]
    [InlineData(601)]
    public void RejectsInvalidCountdownWithoutChangingState(int seconds)
    {
        var store = new OtpStore();
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Countdown = TimeSpan.FromSeconds(seconds));
        Assert.Equal(OtpStore.Lifetime, store.Countdown);
    }
}
