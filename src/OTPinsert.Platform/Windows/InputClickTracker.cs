using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace OTPinsert.Platform;

// Installed on the UI message-loop thread. Callback only forwards mouse-up coordinates;
// all provider access happens later on FocusService's MTA worker. No click history is retained.
internal sealed class InputClickTracker : IDisposable
{
    private readonly HookProc callback;
    private IntPtr hook;
    internal bool IsActive => hook != IntPtr.Zero;
    internal event Action<Point>? Clicked;
    internal InputClickTracker()
    {
        callback = Handle;
        hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0); // WH_MOUSE_LL
    }
    private IntPtr Handle(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt64() == 0x0202) // WM_LBUTTONUP: focus has already changed on down.
        {
            var input = Marshal.PtrToStructure<MouseData>(data);
            if ((input.Flags & 1) == 0)
            {
                try
                {
                    Clicked?.Invoke(new Point(input.X, input.Y));
                }
                catch (Exception ex) { AppDiagnostics.ReportFailure("InputClickTracker.Callback", ex); }
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }
    public void Dispose()
    {
        if (hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }
    }
    private delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData
    {
        public int X, Y; public uint Mouse, Flags, Time; public UIntPtr Extra;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int type, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
}
