using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using OTPcue.Core;
using OTPcue.Platform;
using Windows.ApplicationModel.DataTransfer;

namespace OTPcue.WinUI;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _copyFeedbackTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly AutomaticCodeCopy _automaticCodeCopy = new();
    private Guid? _displayedCodeId;
    private bool _codeCopied, _copyHovered, _copyFocused;

    private void InitializeCodeCopy()
    {
        _copyFeedbackTimer.Tick += (_, _) =>
        {
            _copyFeedbackTimer.Stop();
            _codeCopied = false;
            UpdateCopyFeedback();
        };
        AutomationProperties.SetLiveSetting(CopyCodeButton, AutomationLiveSetting.Polite);
    }

    private void ApplyCodeCopyLanguage()
    {
        ToolTipService.SetToolTip(CopyCodeButton, Localization.Text(TextId.CopyCode));
        UpdateCopyFeedback();
    }

    private void UpdateCodeCopy(Otp? code)
    {
        if (_displayedCodeId != code?.Id)
        {
            _copyFeedbackTimer.Stop();
            _codeCopied = false;
        }
        _displayedCodeId = code?.Id;
        CopyCodeButton.Visibility = code is null ? Visibility.Collapsed : Visibility.Visible;
        NoCodePlaceholder.Visibility = code is null ? Visibility.Visible : Visibility.Collapsed;
        if (code is null)
        {
            _copyHovered = _copyFocused = false;
        }
        UpdateCopyFeedback();
    }

    private void UpdateCopyFeedback()
    {
        CopyCodeIcon.Glyph = _codeCopied ? "\uE73E" : "\uE8C8";
        CopyCodeFeedbackLabel.Text = _codeCopied ? Localization.Text(TextId.CodeCopied) : "";
        CopyCodeIcon.Opacity = _codeCopied || _copyHovered || _copyFocused ? 1 : 0;
        AutomationProperties.SetName(CopyCodeButton, Localization.Text(_codeCopied ? TextId.CodeCopied : TextId.CopyCode));
    }

    private void CopyCodeClicked(object sender, RoutedEventArgs e)
    {
        var code = _store.Latest();
        if (code is null || code.Id != _displayedCodeId)
        {
            RenderCode(code);
            return;
        }
        CopyCodeToClipboard(code);
    }

    private void CopyCodeToClipboard(Otp code)
    {
        if (_exiting || !_store.Contains(code.Id))
        {
            return;
        }
        try
        {
            var content = new DataPackage();
            content.SetText(code.Code);
            Clipboard.SetContent(content);
            _codeCopied = true;
            UpdateCopyFeedback();
            if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            {
                var peer = FrameworkElementAutomationPeer.FromElement(CopyCodeButton)
                    ?? FrameworkElementAutomationPeer.CreatePeerForElement(CopyCodeButton);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
            _copyFeedbackTimer.Stop();
            _copyFeedbackTimer.Start();
        }
        catch (Exception exception)
        {
            AppDiagnostics.ReportFailure("Clipboard.CopyCode", exception);
            SetStatus(new(TextId.CopyCodeFailed));
        }
    }

    private void CopyCodePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _copyHovered = true;
        UpdateCopyFeedback();
    }

    private void CopyCodePointerExited(object sender, PointerRoutedEventArgs e)
    {
        _copyHovered = false;
        UpdateCopyFeedback();
    }

    private void CopyCodeGotFocus(object sender, RoutedEventArgs e)
    {
        _copyFocused = true;
        UpdateCopyFeedback();
    }

    private void CopyCodeLostFocus(object sender, RoutedEventArgs e)
    {
        _copyFocused = false;
        UpdateCopyFeedback();
    }
}
