using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using OTPinsert.Platform;

namespace OTPinsert.WinUI;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _soundPreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private bool _settingSoundVolume;
    private bool _soundVolumePointerDown, _soundVolumeChangedDuringDrag;

    private void InitializeSoundPreview()
    {
        _soundPreviewTimer.Tick += (_, _) =>
        {
            _soundPreviewTimer.Stop();
            PreviewSoundVolume();
        };
        // Slider's thumb handles pointer events itself; observe those events too.
        SoundVolumeInput.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(SoundVolumePointerPressed), true);
        SoundVolumeInput.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(SoundVolumePointerReleased), true);
        SoundVolumeInput.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(SoundVolumePointerReleased), true);
    }

    private void PreviewSoundVolume()
    {
        if (!_closed && !_exiting && !_soundVolumePointerDown &&
            SettingsScreen.Visibility == Visibility.Visible && PlaySoundOnNewCodeInput.IsOn)
        {
            _codeNotificationSound.Play((int)SoundVolumeInput.Value);
        }
    }

    private void SoundVolumePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _soundVolumePointerDown = true;
        // A click on the track may change Value before the event reaches the slider.
        _soundVolumeChangedDuringDrag = _soundPreviewTimer.IsEnabled;
        _soundPreviewTimer.Stop();
    }

    private void SoundVolumePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_soundVolumePointerDown)
        {
            return;
        }
        _soundVolumePointerDown = false;
        if (_soundVolumeChangedDuringDrag)
        {
            _soundVolumeChangedDuringDrag = false;
            PreviewSoundVolume();
        }
    }

    private void PopulateSoundSettings(AppSettings settings)
    {
        PlaySoundOnNewCodeInput.IsOn = settings.PlaySoundOnNewCode;
        // Loading or resetting settings must not trigger an audio preview.
        _soundPreviewTimer.Stop();
        _settingSoundVolume = true;
        try
        {
            SoundVolumeInput.Value = settings.SoundVolumePercent;
        }
        finally
        {
            _settingSoundVolume = false;
        }
        UpdateSoundVolumeSettings();
    }

    private void SoundSettingToggled(object sender, RoutedEventArgs e) => UpdateSoundVolumeSettings();

    private void UpdateSoundVolumeSettings()
    {
        if (SoundVolumeSettings is not null)
        {
            SoundVolumeSettings.Visibility = PlaySoundOnNewCodeInput.IsOn ? Visibility.Visible : Visibility.Collapsed;
            if (!PlaySoundOnNewCodeInput.IsOn)
            {
                _soundPreviewTimer.Stop();
            }
        }
    }

    private void SoundVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (SoundVolumeValue is not null)
        {
            SoundVolumeValue.Text = $"{(int)e.NewValue} %";
        }

        if (_settingSoundVolume || _closed || _exiting ||
            SettingsScreen?.Visibility != Visibility.Visible || PlaySoundOnNewCodeInput?.IsOn != true)
        {
            return;
        }

        if (_soundVolumePointerDown)
        {
            _soundVolumeChangedDuringDrag = true;
            return;
        }

        // Debounce repeated keyboard steps. Pointer dragging previews only on release.
        _soundPreviewTimer.Stop();
        _soundPreviewTimer.Start();
    }

}
