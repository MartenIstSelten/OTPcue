namespace OTPinsert.Platform;

internal static class StatusPresentation
{
    internal static bool IsCodeNotification(TextId id) => id is
        TextId.NewEmailCodeDetected or TextId.NewGoogleEmailCodeDetected;

    internal static LocalizedText AccountStatus(LocalizedText message) =>
        IsCodeNotification(message.Id) ? new(TextId.MailConnected) : message;

    internal static bool IsTransient(TextId id) => IsCodeNotification(id) || id is
        TextId.NewCodeDetected or TextId.AccountAdded or TextId.AccountUpdated or TextId.AccountRemoved
        or TextId.CurrentCodeDiscarded or TextId.CodeInsertedViaUIAutomation or TextId.CodeSentViaKeyboardInput
        or TextId.TheCodeHasExpired or TextId.FocusChangedPleaseClickTheSuggestionAgain
        or TextId.TheFieldIsNotEmptyPleaseClearItFirst or TextId.ReleaseModifierKeys;
}
