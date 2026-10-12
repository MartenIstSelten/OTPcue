using System.Runtime.InteropServices;
using System.Windows;
using Accessibility;

namespace OTPinsert.Platform;

// Native MSAA/IAccessible2 boundary. All callers run on FocusService's MTA worker.
internal static class MsaaNative
{
    internal const int MaximumTraversalDepth = 16;
    private const uint ClientObjectId = 0xFFFFFFFC; // OBJID_CLIENT
    private const uint RootAncestor = 2; // GA_ROOT
    private const int UniqueIdSlot = 41;
    private const int WindowHandleSlot = 42;
    private static readonly Guid Accessible2Id = new("E89F726E-C4F4-4C19-BB19-B647D7FA8478");

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint(int x, int y)
    {
        public int X = x, Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Native.Rect CaretBounds;
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAccessibleServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid iid, out IntPtr result);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetUniqueId(IntPtr self, out int id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetWindowHandle(IntPtr self, out IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible? accessible);
    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromPoint(ScreenPoint point,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible? accessible,
        [MarshalAs(UnmanagedType.Struct)] out object child);

    internal static IAccessible? ReadFocusedClient(IntPtr foreground)
    {
        var thread = Native.GetWindowThreadProcessId(foreground, out var process);
        if (foreground == IntPtr.Zero || thread == 0 || process == Environment.ProcessId)
        {
            return null;
        }
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(thread, ref info) || info.Focus == IntPtr.Zero
            || GetAncestor(info.Focus, RootAncestor) != foreground)
        {
            return null;
        }
        Native.GetWindowThreadProcessId(info.Focus, out var focusProcess);
        if (focusProcess != process)
        {
            return null;
        }
        var iid = typeof(IAccessible).GUID;
        return AccessibleObjectFromWindow(info.Focus, ClientObjectId, ref iid, out var root) >= 0 ? root : null;
    }

    internal static MsaaField? HitTest(Point position)
    {
        return AccessibleObjectFromPoint(new((int)position.X, (int)position.Y), out var hit, out var child) >= 0
            && hit is not null && child is int id ? new(hit, id) : null;
    }

    internal static MsaaIdentity? ReadIdentity(IAccessible accessible)
    {
        if (accessible is not IAccessibleServiceProvider provider)
        {
            return null;
        }
        var service = Accessible2Id;
        var iid = Accessible2Id;
        var pointer = IntPtr.Zero;
        try
        {
            if (provider.QueryService(ref service, ref iid, out pointer) < 0 || pointer == IntPtr.Zero)
            {
                return null;
            }
            // IAccessible2 extends IAccessible's 28-slot vtable. The published IDL
            // fixes these two property slots; isolate this ABI detail here.
            // https://github.com/LinuxA11y/IAccessible2/blob/master/api/Accessible2.idl
            var table = Marshal.ReadIntPtr(pointer);
            var getId = Marshal.GetDelegateForFunctionPointer<GetUniqueId>(Marshal.ReadIntPtr(table, UniqueIdSlot * IntPtr.Size));
            var getWindow = Marshal.GetDelegateForFunctionPointer<GetWindowHandle>(Marshal.ReadIntPtr(table, WindowHandleSlot * IntPtr.Size));
            return getId(pointer, out var id) == 0 && id != 0
                && getWindow(pointer, out var window) == 0 && window != IntPtr.Zero
                ? new(window, id) : null;
        }
        finally
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.Release(pointer);
            }
        }
    }

    internal static bool SameComObject(IAccessible left, IAccessible right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }
        // Distinct managed providers cannot share a COM wrapper identity.
        if (!Marshal.IsComObject(left) || !Marshal.IsComObject(right))
        {
            return false;
        }
        var leftPointer = Marshal.GetIUnknownForObject(left);
        try
        {
            var rightPointer = Marshal.GetIUnknownForObject(right);
            try
            {
                return leftPointer == rightPointer;
            }
            finally { Marshal.Release(rightPointer); }
        }
        finally { Marshal.Release(leftPointer); }
    }
}

internal readonly record struct MsaaIdentity(IntPtr Window, int Node);
