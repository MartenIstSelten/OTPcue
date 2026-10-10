using OTPcue.Platform;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using WinRT;
using WinRT.Interop;

namespace OTPcue.WinUI;

internal sealed class AcrylicPopupWindow : Window
{
    private const int PopupWidth = 350;
    private const int PopupHeight = 64;
    private readonly AppWindow _appWindow;
    private readonly nint _hwnd;
    private readonly DesktopAcrylicController? _acrylic;
    private readonly SystemBackdropConfiguration? _backdropConfiguration;
    private bool _visible;
    private readonly SubclassProc _subclass;

    public AcrylicPopupWindow()
    {
        Title = Localization.Text(TextId.InsertCode);
        Content = new PopupCard();

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        _appWindow.SetPresenter(presenter);
        _appWindow.Resize(new Windows.Graphics.SizeInt32(PopupWidth, PopupHeight));
        _subclass = HandleWindowMessage;
        SetWindowSubclass(_hwnd, _subclass, 1, 0);

        ApplyPopupStyle();

        if (DesktopAcrylicController.IsSupported())
        {
            // A non-activating popup stays inactive. Keep the transient backdrop active.
            _backdropConfiguration = new SystemBackdropConfiguration
            {
                IsInputActive = true,
                Theme = SystemBackdropTheme.Dark
            };
            _acrylic = new DesktopAcrylicController();
            _acrylic.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
            _acrylic.SetSystemBackdropConfiguration(_backdropConfiguration);
            _acrylic.TintColor = Windows.UI.Color.FromArgb(255, 21, 34, 47);
            _acrylic.TintOpacity = 0.20f;
            _acrylic.LuminosityOpacity = 0.20f;
            _acrylic.FallbackColor = Windows.UI.Color.FromArgb(255, 26, 37, 51);
        }

        if (Content is PopupCard card)
        {
            card.InsertRequested += () => InsertRequested?.Invoke();
            card.DismissRequested += () => DismissRequested?.Invoke();
        }
        // WinUI can show a newly created top-level window before Activate() is called.
        // Keep the native window hidden until the demo explicitly requests a suggestion.
        ShowWindow(_hwnd, SwHide);
    }

    public event Action? InsertRequested;
    public event Action? DismissRequested;

    public void SetCode(OTPcue.Core.Otp code) => ((PopupCard)Content).SetCode(code);

    public void Show()
    {
        if (_visible)
        {
            return;
        }

        ApplyPopupStyle();
        _visible = true;
        SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    public void Hide()
    {
        _visible = false;
        ShowWindow(_hwnd, SwHide);
        ((PopupCard)Content).ClearCode();
    }

    public void DisposeBackdrop()
    {
        _acrylic?.Dispose();
        RemoveWindowSubclass(_hwnd, _subclass, 1);
    }

    private nint HandleWindowMessage(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x0083 && wParam != 0)
        {
            return 0; // No native nonclient frame.
        }

        if (message == 0x0021)
        {
            return 3; // MA_NOACTIVATE: keep keyboard focus in the target input.
        }

        if (message == 0x0084)
        {
            return 1; // HTCLIENT; no resize/title hit zones.
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    public void Position(System.Windows.Rect bounds)
    {
        var rect = new OTPcue.Platform.Native.Rect { Left = (int)bounds.Left, Top = (int)bounds.Top, Right = (int)bounds.Right, Bottom = (int)bounds.Bottom };
        var monitor = OTPcue.Platform.Native.MonitorFromRect(ref rect, 2);
        var info = new OTPcue.Platform.Native.MonitorInfo { Size = Marshal.SizeOf<OTPcue.Platform.Native.MonitorInfo>() };
        if (!OTPcue.Platform.Native.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var scale = OTPcue.Platform.Native.GetDpiForWindow(_hwnd) / 96.0;
        var width = Math.Min((int)Math.Ceiling(PopupWidth * scale), info.Work.Right - info.Work.Left);
        var height = (int)Math.Ceiling(PopupHeight * scale);
        var x = Math.Clamp(rect.Left, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - width));
        var gap = (int)Math.Round(5 * scale);
        var y = rect.Bottom + gap;
        if (y + height > info.Work.Bottom)
        {
            y = rect.Top - height - gap;
        }

        y = Math.Clamp(y, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - height));
        SetWindowPos(_hwnd, HwndTopmost, x, y, width, height, SwpNoActivate);
    }

    private void ApplyPopupStyle()
    {
        var exStyle = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(_hwnd, GwlExStyle, new nint(exStyle | WsExToolWindow | WsExNoActivate));
        var style = GetWindowLongPtr(_hwnd, GwlStyle).ToInt64();
        SetWindowLongPtr(_hwnd, GwlStyle, new nint(style & ~WsThickFrame & ~WsMaximizeBox & ~WsMinimizeBox & ~WsCaption & ~WsSysMenu));
        var cornerPreference = DwmwcpRound;
        DwmSetWindowAttribute(_hwnd, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));
        var borderColor = DwmColorNone;
        DwmSetWindowAttribute(_hwnd, DwmwaBorderColor, ref borderColor, sizeof(uint));
        SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
    }

    private const int GwlExStyle = -20;
    private const int GwlStyle = -16;
    private const long WsThickFrame = 0x00040000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsCaption = 0x00C00000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpRound = 2;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const int SwHide = 0;
    private static readonly nint HwndTopmost = new(-1);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref uint value, int size);
    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
}
