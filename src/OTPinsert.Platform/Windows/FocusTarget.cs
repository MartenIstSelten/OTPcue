using System.Windows;
using System.Windows.Automation;

namespace OTPinsert.Platform;

// Each target belongs to exactly one provider. An MSAA field does not need a UIA proxy.
internal sealed class FocusTarget
{
    internal AutomationElement? Element
    {
        get;
    }
    internal MsaaField? Msaa
    {
        get;
    }
    internal Rect Bounds
    {
        get;
    }
    internal IntPtr Foreground
    {
        get;
    }
    internal string Label
    {
        get;
    }

    internal FocusTarget(AutomationElement element, Rect bounds, IntPtr foreground, string label)
    {
        ArgumentNullException.ThrowIfNull(element);
        Element = element;
        Bounds = bounds;
        Foreground = foreground;
        Label = label;
    }

    internal FocusTarget(MsaaField field, Rect bounds, IntPtr foreground, string label)
    {
        ArgumentNullException.ThrowIfNull(field);
        Msaa = field;
        Bounds = bounds;
        Foreground = foreground;
        Label = label;
    }

    internal bool SameField(FocusTarget other)
    {
        if (Foreground != other.Foreground)
        {
            return false;
        }
        if (Msaa is not null)
        {
            return other.Msaa is not null && Msaa.SameAs(other.Msaa);
        }
        return other.Msaa is null && Element is not null && other.Element is not null
            && Automation.Compare(Element, other.Element);
    }
}
