namespace OTPinsert.Platform;

// Retain the message identity and arguments; render only when the UI reads it.
internal sealed record LocalizedText(TextId Id, params object[] Arguments)
{
    public override string ToString() => Localization.Format(Id, Arguments);
}
