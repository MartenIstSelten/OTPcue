using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using OTPinsert.Platform;
using OTPinsert.Core;
using MailKit;
using MimeKit;
using System.Text.RegularExpressions;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--mail-tests"))
        {
            RunMailTests().GetAwaiter().GetResult();
            return;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // WPF is only an external target fixture; the application itself uses WinUI 3.
        if (args.Contains("--fixture"))
        {
            var stack = new StackPanel { Margin = new Thickness(20) };
            var first = new TextBox { Height = 32 };
            AutomationProperties.SetAutomationId(first, "CodeOne");
            var second = new TextBox { Height = 32, Margin = new Thickness(0, 12, 0, 0) };
            AutomationProperties.SetAutomationId(second, "CodeTwo");
            var rich = new RichTextBox { Height = 100, Margin = new Thickness(0, 12, 0, 0) };
            AutomationProperties.SetAutomationId(rich, "Fallback");
            var readOnly = new TextBox { Height = 32, IsReadOnly = true };
            AutomationProperties.SetAutomationId(readOnly, "Readonly");
            var password = new PasswordBox { Height = 32 };
            AutomationProperties.SetAutomationId(password, "Password");
            foreach (var control in new Control[] { first, second, rich, readOnly, password })
            {
                stack.Children.Add(control);
            }

            var window = new Window { Title = "OTPinsert Test Fixture", Width = 500, Height = 420, Content = stack };
            window.Closed += (_, _) => app.Shutdown();
            window.Show();
            first.Focus();
            app.Run();
            return;
        }
        app.Startup += async (_, _) =>
        {
            try
            {
                await Task.Run(() => Run(app));
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { app.Shutdown(); }
        };
        app.Run();
    }
    private static void Check(bool condition, string name)
    {
        if (!condition)
        {
            throw new Exception("FAIL " + name);
        }

        Console.WriteLine("PASS " + name);
    }
    private static async Task WaitUntil(Func<bool> condition,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string? description = null)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("Timed out waiting for " + description);
            }

            await Task.Delay(100);
        }
    }
    private static async Task Run(Application app)
    {
        await RunMailTests();
        Check(MailPoller.NewUids(new[] { new UniqueId(42) }, 42).Length == 0, "IMAP n:* response cannot reprocess highest old UID");
        Check(MailPoller.NewUids(new[] { new UniqueId(43), new UniqueId(41), new UniqueId(42), new UniqueId(43) }, 42).Select(u => u.Id).SequenceEqual(new uint[] { 43 }), "IMAP cursor filters old and duplicate UIDs");
        var fixtureStart = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            fixtureStart.ArgumentList.Add(typeof(Program).Assembly.Location);
        }
        fixtureStart.ArgumentList.Add("--fixture");
        using var fixture = Process.Start(fixtureStart)!;
        try
        {
            await WaitUntil(() => { fixture.Refresh(); return fixture.MainWindowHandle != IntPtr.Zero; });
            var hwnd = fixture.MainWindowHandle;
            app.Dispatcher.Invoke(() => { using var tracker = new InputClickTracker(); Check(tracker.IsActive, "Native input-click hook installs on UI message-loop thread"); });
            var root = AutomationElement.FromHandle(hwnd);
            AutomationElement Find(string id) => root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id)) ?? throw new Exception("Missing " + id);
            var first = Find("CodeOne");
            var second = Find("CodeTwo");
            var fallback = Find("Fallback");
            var store = new OtpStore();
            store.Add("123456", "Test", DateTimeOffset.UtcNow);
            using var focus = new FocusService(store);
            FocusTarget? target = null;
            focus.Changed += value => Volatile.Write(ref target, value);
            focus.Status += message => Console.WriteLine(message);
            SetForegroundWindow(hwnd);
            first.SetFocus();
            await WaitUntil(() => Volatile.Read(ref target) is { Element: { } element } && Automation.Compare(element, first));
            var selected = Volatile.Read(ref target)!;
            Check(selected.Bounds.Width > 0, "Unlabelled text input has global focus and bounds");
            var otp = store.Latest()!;
            var inserted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            focus.Fill(selected, otp, ok => inserted.TrySetResult(ok));
            Check(await inserted.Task.WaitAsync(TimeSpan.FromSeconds(8)), "ValuePattern fill succeeds");
            Check(((ValuePattern)first.GetCurrentPattern(ValuePattern.Pattern)).Current.Value == "123456", "ValuePattern wrote code");
            Check(store.Latest() is null, "Successful fill consumes code");
            Check(Native.GetForegroundWindow() == hwnd && first.Current.HasKeyboardFocus, "Fill preserves target focus");
            store.RetryLatest();
            var nonempty = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            focus.Fill(selected, otp, ok => nonempty.TrySetResult(ok));
            Check(!await nonempty.Task.WaitAsync(TimeSpan.FromSeconds(8)) && ((ValuePattern)first.GetCurrentPattern(ValuePattern.Pattern)).Current.Value == "123456", "Existing user text is not overwritten");
            var clicksInField = 0;
            focus.FieldClicked += _ => Interlocked.Increment(ref clicksInField);
            focus.NotifyClick(new Point(selected.Bounds.Right + 100, selected.Bounds.Bottom + 100));
            await Task.Delay(300);
            Check(Volatile.Read(ref clicksInField) == 0, "Click outside input is ignored");
            // NotifyClick simulates a mouse-up only; unlike a real click it cannot activate
            // the fixture. Establish focus again before testing the positive path.
            SetForegroundWindow(hwnd);
            first.SetFocus();
            await WaitUntil(() => Native.GetForegroundWindow() == hwnd && first.Current.HasKeyboardFocus);
            var clickBounds = first.Current.BoundingRectangle;
            var clickPosition = new Point(clickBounds.Left + clickBounds.Width / 2, clickBounds.Top + clickBounds.Height / 2);
            focus.NotifyClick(clickPosition);
            try
            {
                await WaitUntil(() => Volatile.Read(ref clicksInField) == 1);
            }
            catch (TimeoutException ex)
            {
                var hit = AutomationElement.FromPoint(clickPosition);
                throw new TimeoutException($"Field click failed at {clickPosition}; hit process {hit.Current.ProcessId}, "
                    + $"automation ID '{hit.Current.AutomationId}', focused: {first.Current.HasKeyboardFocus}, "
                    + $"fixture foreground: {Native.GetForegroundWindow() == hwnd}.", ex);
            }
            Check(true, "Click in focused input is detected");
            store.Add("654321", "Test", DateTimeOffset.UtcNow);
            second.SetFocus();
            var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            focus.Fill(selected, store.Latest()!, ok => changed.TrySetResult(ok));
            Check(!await changed.Task.WaitAsync(TimeSpan.FromSeconds(8)), "Stale target rejected");
            Check(((ValuePattern)second.GetCurrentPattern(ValuePattern.Pattern)).Current.Value == "", "Other field untouched");
            fallback.SetFocus();
            await WaitUntil(() => Volatile.Read(ref target) is { Element: { } element } && Automation.Compare(element, fallback));
            Check(!fallback.TryGetCurrentPattern(ValuePattern.Pattern, out _), "Fallback fixture has no ValuePattern");
            var typed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            focus.Fill(Volatile.Read(ref target)!, store.Latest()!, ok => typed.TrySetResult(ok));
            Check(await typed.Task.WaitAsync(TimeSpan.FromSeconds(8)), "SendInput accepted Unicode batch");
            await WaitUntil(() => ((TextPattern)fallback.GetCurrentPattern(TextPattern.Pattern)).DocumentRange.GetText(-1).Contains("654321"));
            Check(true, "SendInput text arrived in target");
            store.Add("111111", "Test", DateTimeOffset.UtcNow);
            var readonlyField = Find("Readonly");
            SetForegroundWindow(hwnd);
            readonlyField.SetFocus();
            await WaitUntil(() => readonlyField.Current.HasKeyboardFocus && Volatile.Read(ref target) is null);
            Check(Volatile.Read(ref target) is null, "Readonly field excluded");
            var passwordField = Find("Password");
            SetForegroundWindow(hwnd);
            passwordField.SetFocus();
            await WaitUntil(() => passwordField.Current.HasKeyboardFocus && Volatile.Read(ref target) is null);
            Check(Volatile.Read(ref target) is null, "Password field excluded");
            Console.WriteLine("Windows integration checks passed.");
        }
        finally
        {
            fixture.CloseMainWindow();
            if (!fixture.WaitForExit(2000))
            {
                fixture.Kill();
            }
        }
    }
    internal static async Task RunMailTests()
    {
        var tests = new MailProcessingTests();
        tests.ReceiveAgeAndClockSkewBoundaries();
        tests.ExtractsPlainAndHtmlWithoutBacktrackingStarvation();
        tests.SenderLabelIsSanitizedAndBounded();
        await tests.MalformedContentDoesNotBlockLaterMessages();
        await tests.TransportFailurePreservesCursorForRetry();
        await tests.CancellationBeforeCommitPreservesCursor();
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
}
