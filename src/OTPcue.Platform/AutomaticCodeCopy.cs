using OTPcue.Core;

namespace OTPcue.Platform;

// Observe codes even while disabled, so enabling the setting only affects new codes.
// Keep the identity when the code disappears: reactivating a consumed code must not
// overwrite something the user subsequently copied to the clipboard.
internal sealed class AutomaticCodeCopy
{
    private Guid? lastObserved;

    internal bool ShouldCopy(Otp? code, bool enabled)
    {
        if (code is null || code.Id == lastObserved)
        {
            return false;
        }
        lastObserved = code.Id;
        return enabled;
    }
}
