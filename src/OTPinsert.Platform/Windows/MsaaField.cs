using System.Windows;
using Accessibility;

namespace OTPinsert.Platform;

// A provider object must stay alive for the lifetime of its cached node identity.
// Do not manually release these RCWs: another target can share the same wrapper.
internal sealed class MsaaField
{
    private const int ChildSelf = 0;
    private const int TextRole = 42; // ROLE_SYSTEM_TEXT
    private const int MinimumFieldSize = 2;
    private const MsaaState RejectedStates = MsaaState.Unavailable | MsaaState.ReadOnly
        | MsaaState.Invisible | MsaaState.Offscreen | MsaaState.Protected;

    [Flags]
    private enum MsaaState
    {
        Unavailable = 0x1,
        Focused = 0x4,
        ReadOnly = 0x40,
        Invisible = 0x8000,
        Offscreen = 0x10000,
        Protected = 0x20000000
    }

    private readonly IAccessible accessible;
    private readonly int childId;
    private readonly MsaaIdentity? identity;

    internal MsaaField(IAccessible accessible, int childId)
    {
        this.accessible = accessible;
        this.childId = childId;
        identity = MsaaNative.ReadIdentity(accessible);
    }

    // UIA remains the primary provider; this is called only when it has no target.
    internal static FocusTarget? ReadTarget()
    {
        var foreground = Native.GetForegroundWindow();
        var root = MsaaNative.ReadFocusedClient(foreground);
        if (root is null)
        {
            return null;
        }
        var field = Focused(root);
        if (field is null || !field.TryDescribe(out var bounds, out var label)
            || Native.GetForegroundWindow() != foreground)
        {
            return null;
        }
        return new(field, bounds, foreground, label);
    }

    internal bool SameAs(MsaaField other)
    {
        if (childId != other.childId)
        {
            return false;
        }
        // Chromium creates fresh COM proxies for the same node. IUnknown alone
        // cannot identify those; IA2 provides an ID scoped to its native window.
        if (identity is not null || other.identity is not null)
        {
            return identity is not null && identity == other.identity;
        }
        return MsaaNative.SameComObject(accessible, other.accessible);
    }

    internal bool ContainsHit(Point position)
    {
        var hit = MsaaNative.HitTest(position);
        // MSAA can return a text child inside the edit control, like UIA's hit test.
        for (var depth = 0; hit is not null && depth < MsaaNative.MaximumTraversalDepth; depth++)
        {
            if (SameAs(hit))
            {
                return true;
            }
            hit = hit.Parent();
        }
        return false;
    }

    private MsaaField? Parent()
    {
        if (childId != ChildSelf)
        {
            return new(accessible, ChildSelf);
        }
        return accessible.accParent is IAccessible parent ? new(parent, ChildSelf) : null;
    }

    internal static MsaaField? Focused(IAccessible root)
    {
        var current = new MsaaField(root, ChildSelf);
        for (var depth = 0; depth < MsaaNative.MaximumTraversalDepth; depth++)
        {
            var focus = current.accessible.accFocus;
            if (focus is IAccessible next)
            {
                var nextField = new MsaaField(next, ChildSelf);
                if (current.SameAs(nextField))
                {
                    return current;
                }
                current = nextField;
                continue;
            }
            // A null focus means this accessible subtree does not own keyboard focus.
            return focus is int id ? new(current.accessible, id) : null;
        }
        return null;
    }

    internal bool TryDescribe(out Rect bounds, out string label)
    {
        bounds = Rect.Empty;
        label = string.Empty;
        var role = Convert.ToInt32(accessible.get_accRole(childId));
        var state = Convert.ToInt32(accessible.get_accState(childId));
        if (!IsEditable(role, state))
        {
            return false;
        }
        accessible.accLocation(out var x, out var y, out var width, out var height, childId);
        if (width < MinimumFieldSize || height < MinimumFieldSize)
        {
            return false;
        }
        bounds = new(x, y, width, height);
        label = accessible.get_accName(childId) ?? string.Empty;
        return true;
    }

    internal static bool IsEditable(int role, int state)
    {
        var flags = (MsaaState)state;
        return role == TextRole && (flags & MsaaState.Focused) != 0 && (flags & RejectedStates) == 0;
    }
}
