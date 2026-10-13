using OTPinsert.Platform;
using Xunit;

public sealed class StatusPresentationTests
{
    [Fact]
    public void ReceivingCodeKeepsAccountConnectedForBothProviders()
    {
        foreach (var id in new[] { TextId.NewEmailCodeDetected, TextId.NewGoogleEmailCodeDetected })
        {
            var message = new LocalizedText(id);
            Assert.True(StatusPresentation.IsCodeNotification(id));
            Assert.Equal(TextId.MailConnected, StatusPresentation.AccountStatus(message).Id);
        }
    }

    [Fact]
    public void ConnectionFailuresKeepTheirDetails()
    {
        var failure = new LocalizedText(TextId.EmailConnectionFailedRetrying, "Test failure");
        Assert.Same(failure, StatusPresentation.AccountStatus(failure));
        Assert.False(StatusPresentation.IsTransient(failure.Id));
        Assert.False(StatusPresentation.IsTransient(TextId.AccountStorageUnavailable));
        Assert.False(StatusPresentation.IsTransient(TextId.CopyCodeFailed));
    }

    [Fact]
    public void InformationalFooterFeedbackExpiresWhileDemoInstructionsRemain()
    {
        Assert.True(StatusPresentation.IsTransient(TextId.NewCodeDetected));
        Assert.True(StatusPresentation.IsTransient(TextId.AccountAdded));
        Assert.True(StatusPresentation.IsTransient(TextId.CurrentCodeDiscarded));
        Assert.True(StatusPresentation.IsTransient(TextId.CodeInsertedViaUIAutomation));
        Assert.False(StatusPresentation.IsTransient(TextId.DemoActiveClickAnEmptyTextFieldInAnotherApp));
    }
}
