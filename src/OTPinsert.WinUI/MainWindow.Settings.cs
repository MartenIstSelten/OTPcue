using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OTPinsert.Platform;

namespace OTPinsert.WinUI;

public sealed partial class MainWindow
{
    private void PopulateSettings(AppSettings settings)
    {
        LanguageInput.SelectedIndex = settings.Language switch
        {
            "de" => 1,
            "en" => 2,
            _ => 0
        };
        CountdownInput.Value = settings.CountdownSeconds;
        RemoveAfterInsertInput.IsOn = settings.RemoveAfterInsert;
        OnlyCodeFieldsInput.IsOn = settings.OnlyCodeFields;
        AutoCopyCodesInput.IsOn = settings.AutoCopyCodes;
        PopulateSoundSettings(settings);
        StartWithWindowsInput.IsOn = settings.StartWithWindows;
        StartInTrayInput.IsOn = settings.StartInTray;
        CloseToTrayInput.IsOn = settings.CloseToTray;
    }
    private bool _navigatingSettings;

    private async void SettingsClicked(object sender, RoutedEventArgs e)
    {
        if (_navigatingSettings || _exiting)
        {
            return;
        }

        _navigatingSettings = true;
        var show = SettingsScreen.Visibility != Visibility.Visible;
        UIElement outgoing = show ? AccountsScreen : SettingsScreen;
        UIElement incoming = show ? SettingsScreen : AccountsScreen;
        try
        {
            if (new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
            {
                await AnimateSettingsPageAsync(outgoing, entering: false, show);
            }

            if (_closed)
            {
                return;
            }

            SetSettingsScreen(show);

            if (!_closed && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
            {
                await AnimateSettingsPageAsync(incoming, entering: true, show);
            }

            if (!_closed)
            {
                (show ? BackButton : SettingsButton).Focus(FocusState.Programmatic);
            }
        }
        catch (Exception exception) when (!_closed)
        {
            AppDiagnostics.ReportFailure("Settings.Animate", exception);
            SetSettingsScreen(show);
            SetSettingsStatus(new(TextId.CouldNotAnimateTheSettingsPage));
        }
        catch (Exception) when (_closed) { }
        finally
        {
            if (!_closed)
            {
                ResetSettingsPageAnimation(outgoing);
                ResetSettingsPageAnimation(incoming);
            }
            _navigatingSettings = false;
        }
    }

    private void SetSettingsScreen(bool show)
    {
        if (!show)
        {
            _soundPreviewTimer.Stop();
        }
        SettingsScreen.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        AccountsScreen.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        SettingsFooterButtons.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        UpdateFooterStatus();
        UpdateUpdateUi();
    }
    private void BackClicked(object sender, RoutedEventArgs e) => SettingsClicked(sender, e);

    private static async Task AnimateSettingsPageAsync(UIElement page, bool entering, bool forward)
    {
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(page, true);
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(page);
        var compositor = visual.Compositor;
        // Short travel and a soft deceleration keep navigation calm and responsive.
        var direction = forward ? 1f : -1f;
        var from = entering ? direction * 24f : 0f;
        var to = entering ? 0f : -direction * 12f;
        var duration = TimeSpan.FromMilliseconds(entering ? 220 : 100);
        var easing = compositor.CreateCubicBezierEasingFunction(
            new System.Numerics.Vector2(0.2f, 0f), new System.Numerics.Vector2(0f, 1f));
        var slide = compositor.CreateScalarKeyFrameAnimation();
        slide.Duration = duration;
        slide.InsertKeyFrame(0f, from);
        slide.InsertKeyFrame(1f, to, easing);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = duration;
        fade.InsertKeyFrame(0f, entering ? 0f : 1f);
        fade.InsertKeyFrame(1f, entering ? 1f : 0f, easing);

        page.IsHitTestVisible = false;
        using var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        var completed = new TaskCompletionSource<bool>();
        batch.Completed += (_, _) => completed.TrySetResult(true);
        visual.StartAnimation("Translation.X", slide);
        visual.StartAnimation("Opacity", fade);
        batch.End();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static void ResetSettingsPageAnimation(UIElement page)
    {
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(page);
        visual.StopAnimation("Translation.X");
        visual.StopAnimation("Opacity");
        visual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);
        visual.Opacity = 1f;
        page.IsHitTestVisible = true;
    }
    private void ResetSettingsClicked(object sender, RoutedEventArgs e)
    {
        PopulateSettings(new AppSettings());
        SetSettingsStatus(new(TextId.DefaultsSelectedSaveToApply));
    }
    private void SaveSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (!double.IsFinite(CountdownInput.Value) || CountdownInput.Value < 15 || CountdownInput.Value > 600 || CountdownInput.Value != Math.Truncate(CountdownInput.Value))
        {
            SetSettingsStatus(new(TextId.InvalidCountdown));
            return;
        }
        var settings = new AppSettings((int)CountdownInput.Value, RemoveAfterInsertInput.IsOn, OnlyCodeFieldsInput.IsOn,
            StartWithWindowsInput.IsOn, StartInTrayInput.IsOn, CloseToTrayInput.IsOn,
            LanguageInput.SelectedIndex switch
            {
                1 => "de",
                2 => "en",
                _ => "system"
            })
        {
            AutoCopyCodes = AutoCopyCodesInput.IsOn,
            PlaySoundOnNewCode = PlaySoundOnNewCodeInput.IsOn,
            SoundVolumePercent = (int)SoundVolumeInput.Value
        };
        try
        {
            _settingsService.Save(settings);
            _settings = settings;
            Localization.Configure(settings.Language);
            ApplyLanguage();
            _store.Countdown = TimeSpan.FromSeconds(settings.CountdownSeconds);
            SetSettingsStatus(new(TextId.SettingsSaved));
            UpdatePopup();
        }
        catch (AggregateException exception)
        {
            AppDiagnostics.ReportFailure("Settings.SaveRollback", exception);
            SetSettingsStatus(new(TextId.SettingsRollbackFailed));
        }
        catch (Exception exception)
        {
            AppDiagnostics.ReportFailure("Settings.Save", exception);
            SetSettingsStatus(new(TextId.SettingsSaveFailed));
        }
    }

}
