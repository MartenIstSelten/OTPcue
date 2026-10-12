using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using OTPinsert.Core;

namespace OTPinsert.Platform;

internal sealed record FocusTarget(AutomationElement Element, Rect Bounds, IntPtr Foreground, string Label);

// All provider calls and subscription changes run on this dedicated MTA thread.
internal sealed class FocusService : IDisposable
{
    private readonly BlockingCollection<Action> jobs = new();
    private readonly Thread worker;
    private readonly OtpStore store;
    private int disposed;
    private volatile bool refresh = true;
    internal event Action<FocusTarget?>? Changed;
    internal event Action<FocusTarget>? FieldClicked;
    internal event Action<LocalizedText>? Status;
    internal FocusService(OtpStore store)
    {
        this.store = store;
        worker = new Thread(Run) { IsBackground = true, Name = "OTP UI Automation" };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
    }
    private void Run()
    {
        AutomationFocusChangedEventHandler handler = (_, _) => refresh = true;
        try
        {
            Automation.AddAutomationFocusChangedEventHandler(handler);
            while (!jobs.IsCompleted)
            {
                if (jobs.TryTake(out var action, 250))
                {
                    action();
                }
                // Refresh geometry even without focus events (window moves, scrolling, missed events).
                if (refresh || store.Latest() is not null)
                {
                    refresh = false;
                    Changed?.Invoke(ReadTarget());
                }
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.ReportFailure("FocusTracking.Run", ex);
            Status?.Invoke(new(TextId.FocusTrackingFailed, ex.GetType().Name));
        }
        finally
        {
            try
            {
                Automation.RemoveAutomationFocusChangedEventHandler(handler);
            }
            catch (Exception ex) { AppDiagnostics.ReportFailure("FocusTracking.Unsubscribe", ex); }
        }
    }
    private FocusTarget? ReadTarget()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null)
            {
                return null;
            }

            var current = element.Current;
            if (current.ProcessId == Environment.ProcessId || !current.IsEnabled || current.IsOffscreen || current.IsPassword || !current.HasKeyboardFocus)
            {
                return null;
            }

            var foreground = Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(foreground, out var pid);
            if (pid != current.ProcessId)
            {
                return null;
            }

            var writable = element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && !((ValuePattern)pattern).Current.IsReadOnly;
            var text = current.ControlType == ControlType.Edit || (current.ControlType == ControlType.Document && element.TryGetCurrentPattern(TextPattern.Pattern, out _));
            if (!writable && !text)
            {
                return null;
            }

            if (pattern is ValuePattern value && value.Current.IsReadOnly)
            {
                return null;
            }

            var label = current.Name + " " + current.AutomationId + " " + current.HelpText;
            var labelledBy = current.LabeledBy;
            if (labelledBy is not null)
            {
                label += " " + labelledBy.Current.Name;
            }

            var bounds = current.BoundingRectangle;
            if (bounds.IsEmpty || bounds.Width < 2 || bounds.Height < 2 || !double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top))
            {
                return null;
            }

            return new(element, bounds, foreground, label);
        }
        catch (Exception ex)
        {
            // Foreign providers can disappear at any moment; retain a safe diagnostic.
            AppDiagnostics.ReportFailure("FocusTracking.ReadTarget", ex);
            return null;
        }
    }
    internal void Fill(FocusTarget expected, Otp otp, Action<bool> done, bool removeAfterInsert = false)
    {
        Enqueue(() =>
        {
            bool success = false;
            try
            {
                var actual = ReadTarget();
                if (!store.Contains(otp.Id))
                {
                    Status?.Invoke(new(TextId.TheCodeHasExpired));
                    return;
                }
                if (actual is null || actual.Foreground != expected.Foreground || !Automation.Compare(actual.Element, expected.Element))
                {
                    Status?.Invoke(new(TextId.FocusChangedPleaseClickTheSuggestionAgain));
                    return;
                }
                if (actual.Element.TryGetCurrentPattern(ValuePattern.Pattern, out var obj) && obj is ValuePattern value && !value.Current.IsReadOnly)
                {
                    // SetValue replaces the field. Do not silently overwrite existing user text.
                    if (!string.IsNullOrEmpty(value.Current.Value))
                    {
                        Status?.Invoke(new(TextId.TheFieldIsNotEmptyPleaseClearItFirst));
                        return;
                    }
                    value.SetValue(otp.Code);
                    success = true;
                    Status?.Invoke(new(TextId.CodeInsertedViaUIAutomation));
                }
                else
                {
                    if (Native.ModifiersDown())
                    {
                        Status?.Invoke(new(TextId.ReleaseModifierKeys));
                        return;
                    }
                    // Last possible foreground/focus check before sending one Unicode input batch.
                    if (Native.GetForegroundWindow() != expected.Foreground || !expected.Element.Current.HasKeyboardFocus)
                    {
                        return;
                    }

                    var sent = Native.TypeUnicode(otp.Code);
                    success = sent == otp.Code.Length * 2;
                    // A partial batch must not be retried: it could duplicate digits.
                    if (sent > 0)
                    {
                        store.Discard(otp.Id);
                    }

                    Status?.Invoke(new(success ? TextId.CodeSentViaKeyboardInput : TextId.KeyboardInputBlockedOrIncompleteCheckTheField));
                }
                if (success)
                {
                    if (removeAfterInsert)
                    {
                        store.Discard(otp.Id);
                    }
                    else
                    {
                        store.Remove(otp.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                AppDiagnostics.ReportFailure("FocusTracking.Fill", ex);
                // Never fall back after SetValue throws: a provider may already have changed the field.
                Status?.Invoke(new(TextId.InsertionUnconfirmed, ex.GetType().Name));
            }
            finally { done(success); refresh = true; }
        });
    }
    internal void Refresh() => refresh = true;
    internal void NotifyClick(Point position) => Enqueue(() =>
    {
        try
        {
            var field = ReadTarget();
            if (field is null || !field.Bounds.Contains(position))
            {
                return;
            }
            // Especially for large documents: an overlay can lie inside the field's rectangle.
            // Verify the clicked UIA element actually belongs to the focused input.
            var hit = AutomationElement.FromPoint(position);
            for (var depth = 0; hit is not null && depth < 16; depth++)
            {
                if (hit.Current.ProcessId == Environment.ProcessId)
                {
                    return;
                }

                if (Automation.Compare(hit, field.Element))
                {
                    FieldClicked?.Invoke(field);
                    return;
                }
                hit = TreeWalker.ControlViewWalker.GetParent(hit);
            }
        }
        catch (Exception ex) { AppDiagnostics.ReportFailure("FocusTracking.HitTest", ex); }
        finally { refresh = true; }
    });
    private void Enqueue(Action action)
    {
        if (Volatile.Read(ref disposed) == 0)
        {
            try
            {
                jobs.Add(action);
            }
            catch (InvalidOperationException) { }
        }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        jobs.CompleteAdding();
        // A broken provider must not prevent process shutdown.
        worker.Join(500);
    }
}
