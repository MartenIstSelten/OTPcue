using Microsoft.UI.Dispatching;
using OTPcue.Core;
using OTPcue.Platform;

namespace OTPcue.WinUI;

// Owned by the UI thread; provider callbacks are marshalled through dispatch.
// Owns the suggestion lifecycle, while MainWindow only renders the current code.
internal sealed class AutofillController
{
    private readonly AcrylicPopupWindow popup = new();
    private readonly OtpStore store;
    private readonly FocusService focus;
    private readonly InputClickTracker clicks;
    private readonly DispatcherQueueTimer timer;
    private readonly Action<Action> dispatch;
    private readonly Func<AppSettings> settings;
    private readonly Func<bool> suspended;
    private readonly Action<Otp?> render;
    private FocusTarget? target;
    private Guid? lastCodeId;
    private bool blocked;
    private bool stopped;
    internal bool IsFilling
    {
        get; private set;
    }
    internal bool IsPaused
    {
        get; private set;
    }

    internal AutofillController(OtpStore store, DispatcherQueue dispatcher, Action<Action> dispatch,
        Func<AppSettings> settings, Func<bool> suspended, Action<Otp?> render,
        Action<LocalizedText> status, Action discard)
    {
        this.store = store;
        this.dispatch = dispatch;
        this.settings = settings;
        this.suspended = suspended;
        this.render = render;
        focus = new FocusService(store);
        focus.Changed += field => OnUi(() => { target = field; Refresh(); });
        focus.FieldClicked += field => OnUi(() =>
        {
            if (IsFilling || IsPaused || suspended())
            {
                return;
            }
            target = field;
            blocked = false;
            store.RetryLatest();
            Refresh();
        });
        focus.Status += message => OnUi(() => status(message));
        clicks = new InputClickTracker();
        clicks.Clicked += focus.NotifyClick;
        popup.DismissRequested += discard;
        popup.InsertRequested += Insert;
        timer = dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += (_, _) => Refresh();
        timer.Start();
    }

    private void OnUi(Action action) => dispatch(() => { if (!stopped) { action(); } });

    private void Insert()
    {
        var code = store.Latest();
        if (stopped || suspended() || target is null || code is null || IsFilling || IsPaused || blocked)
        {
            return;
        }
        var expected = target;
        IsFilling = true;
        blocked = true;
        popup.Hide();
        // FocusService rechecks field identity, foreground and OTP expiry immediately
        // before insertion, including its keyboard fallback.
        focus.Fill(expected, code, _ => OnUi(() => { IsFilling = false; Refresh(); }), settings().RemoveAfterInsert);
    }

    internal void Refresh()
    {
        if (stopped)
        {
            return;
        }
        var code = store.Latest();
        if (code is not null && code.Id != lastCodeId)
        {
            lastCodeId = code.Id;
            blocked = false;
        }
        render(code);
        if (suspended() || IsPaused || blocked || IsFilling || target is null || code is null
            || (settings().OnlyCodeFields && !OtpDetector.IsCodeLabel(target.Label))
            || Native.GetForegroundWindow() != target.Foreground)
        {
            popup.Hide();
            return;
        }
        popup.SetCode(code);
        popup.Position(target.Bounds);
        popup.Show();
        popup.Position(target.Bounds);
    }

    internal void ResetTarget()
    {
        target = null;
        blocked = false;
        focus.Refresh();
    }

    internal void RefreshTarget()
    {
        blocked = false;
        focus.Refresh();
    }

    internal void Discard()
    {
        store.Clear();
        blocked = true;
        popup.Hide();
        Refresh();
    }

    internal void TogglePause()
    {
        IsPaused = !IsPaused;
        Refresh();
    }

    internal void ApplyLanguage() => popup.Title = Localization.Text(TextId.InsertCode);

    internal void Stop()
    {
        if (stopped)
        {
            return;
        }
        stopped = true;
        var failures = new List<Exception>();
        foreach (var release in new Action[] { popup.Hide, timer.Stop, clicks.Dispose, focus.Dispose })
        {
            try
            {
                release();
            }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (failures.Count > 0)
        {
            throw new AggregateException("Autofill cleanup failed.", failures);
        }
    }

    internal void ClosePopup()
    {
        try
        {
            popup.DisposeBackdrop();
        }
        finally { popup.Close(); }
    }
}
