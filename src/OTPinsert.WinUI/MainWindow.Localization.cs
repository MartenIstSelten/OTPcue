using OTPinsert.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace OTPinsert.WinUI;

public sealed partial class MainWindow
{
    private LocalizedText? _statusMessage;
    private LocalizedText? _settingsMessage;
    private static void SetSettingText(ToggleSwitch toggle, TextBlock title, TextBlock help, TextId titleId, TextId helpId)
    {
        title.Text = Localization.Text(titleId);
        help.Text = Localization.Text(helpId);
        AutomationProperties.SetName(toggle, title.Text);
        AutomationProperties.SetHelpText(toggle, help.Text);
    }

    private void SetStatus(LocalizedText message)
    {
        _statusMessage = message;
        StatusLabel.Text = message.ToString();
    }
    private void SetSettingsStatus(LocalizedText message)
    {
        _settingsMessage = message;
        SettingsStatus.Text = message.ToString();
    }

    private void ApplyLanguage()
    {
        OfflineLabel.Text = Localization.Text(TextId.YouAreOffline);
        ToolTipService.SetToolTip(OfflineStatus, Localization.Text(TextId.OfflineHelp));
        ToolTipService.SetToolTip(PausedStatus, Localization.Text(TextId.PausedHelp));
        StatusLabel.Text = _statusMessage?.ToString() ?? "";
        SettingsStatus.Text = _settingsMessage?.ToString() ?? "";
        LocalizedElement1.Text = Localization.Text(TextId.AutofillPaused);
        DemoButton.Content = Localization.Text(TextId.StartDemo);
        PauseButton.Content = Localization.Text(TextId.PauseAutofill);
        SettingsButton.Content = Localization.Text(TextId.Settings);
        CodeLabel.Text = Localization.Text(TextId.NoCodeYet);
        NoCodePlaceholder.Text = Localization.Text(TextId.NoCodeYet);
        ApplyCodeCopyLanguage();
        SourceLabel.Text = Localization.Text(TextId.ConnectAnAccountThenRequestANewCodeEmail);
        GenerateButton.Content = Localization.Text(TextId.RefreshDemoCode);
        ClearButton.Content = Localization.Text(TextId.DiscardCode);
        LocalizedElement2.Text = Localization.Text(TextId.EmailAccounts);
        AddButton.Content = Localization.Text(TextId.AddAccount);
        EmptyAccounts.Text = Localization.Text(TextId.NoAccountsYetAddYourFirstEmailAccount);
        LocalizedElement3.Text = Localization.Text(TextId.AccountDetails);
        DetailsLabel.Text = Localization.Text(TextId.SelectAnAccount);
        DetailsEmailHeading.Text = Localization.Text(TextId.AccountDetailsEmail);
        DetailsServerHeading.Text = Localization.Text(TextId.AccountDetailsServer);
        DetailsEncryptionHeading.Text = Localization.Text(TextId.EncryptionLabel);
        DetailsFolderHeading.Text = Localization.Text(TextId.AccountDetailsFolder);
        DetailsIntervalHeading.Text = Localization.Text(TextId.AccountDetailsInterval);
        DetailsMonitoringHeading.Text = Localization.Text(TextId.AccountDetailsMonitoring);
        EditButton.Content = Localization.Text(TextId.EditAccount);
        EnableButton.Content = Localization.Text(TextId.DisableAccount);
        RemoveButton.Content = Localization.Text(TextId.RemoveAccount);
        LocalizedElement4.Text = Localization.Text(TextId.CredentialStorageNotice);
        LocalizedElement5.Text = Localization.Text(TextId.Settings);
        CountdownTitle.Text = Localization.Text(TextId.CountdownInSeconds);
        CountdownUnit.Text = Localization.Text(TextId.SecondsUnit);
        LocalizedElement6.Text = Localization.Text(TextId.CountdownHelp);
        AutomationProperties.SetName(CountdownInput, CountdownTitle.Text + " (" + CountdownUnit.Text + ")");
        AutomationProperties.SetHelpText(CountdownInput, LocalizedElement6.Text);
        SetSettingText(RemoveAfterInsertInput, RemoveAfterInsertTitle, RemoveAfterInsertHelp,
            TextId.RemoveAfterInsertingOnce, TextId.RemoveAfterInsertHelp);
        SetSettingText(AutoCopyCodesInput, AutoCopyCodesTitle, AutoCopyCodesHelp,
            TextId.AutoCopyCodes, TextId.AutoCopyCodesHelp);
        SetSettingText(PlaySoundOnNewCodeInput, PlaySoundOnNewCodeTitle, PlaySoundOnNewCodeHelp,
            TextId.PlaySoundOnNewCode, TextId.PlaySoundOnNewCodeHelp);
        SoundVolumeTitle.Text = Localization.Text(TextId.SoundVolume);
        AutomationProperties.SetName(SoundVolumeInput, SoundVolumeTitle.Text);
        SetSettingText(OnlyCodeFieldsInput, OnlyCodeFieldsTitle, LocalizedElement7,
            TextId.OnlyCodeFields, TextId.OnlyCodeFieldsSettingsHelp);
        SetSettingText(StartWithWindowsInput, StartWithWindowsTitle, StartWithWindowsHelp,
            TextId.StartWithWindows, TextId.StartWithWindowsHelp);
        SetSettingText(StartInTrayInput, StartInTrayTitle, StartInTrayHelp,
            TextId.StartInTheNotificationAreaAtWindowsSignIn, TextId.StartInTrayHelp);
        SetSettingText(CloseToTrayInput, CloseToTrayTitle, CloseToTrayHelp,
            TextId.MinimizeToTheNotificationAreaWhenClosing, TextId.CloseToTrayHelp);
        LocalizedElement9.Content = Localization.Text(TextId.Defaults);
        GeneralSettingsHeading.Text = Localization.Text(TextId.General);
        CodeSettingsHeading.Text = Localization.Text(TextId.CodesSuggestions);
        WindowSettingsHeading.Text = Localization.Text(TextId.StartupWindow);
        UpdatesHeading.Text = Localization.Text(TextId.AboutUpdates);
        CheckUpdateButton.Content = Localization.Text(TextId.CheckForUpdates);
        InstallUpdateButton.Content = Localization.Text(TextId.UpdateNow);
        BackButton.Content = Localization.Text(TextId.BackToAccounts);
        LanguageInput.Header = Localization.Text(TextId.Language);
        LanguageInput.Items.Clear();
        LanguageInput.Items.Add(Localization.Text(TextId.SystemLanguage));
        LanguageInput.Items.Add("Deutsch");
        LanguageInput.Items.Add("English");
        LanguageInput.SelectedIndex = _settings.Language switch
        {
            "de" => 1,
            "en" => 2,
            _ => 0
        };
        for (var i = 0; i < _tray.ContextMenuStrip!.Items.Count; i++)
        {
            _tray.ContextMenuStrip.Items[i].Text = Localization.Text(new[] { TextId.Open, TextId.PauseResumeAutofill, TextId.DiscardCode, TextId.Exit }[i]);
        }

        foreach (var account in _accounts)
        {
            account.RefreshLanguage();
        }

        UpdateModeUi();
        PauseButton.Content = Localization.Text(_autofill.IsPaused ? TextId.ResumeAutofill : TextId.PauseAutofill);
        _autofill.ApplyLanguage();
        UpdateDetails();
        UpdateUpdateUi();
        UpdatePopup();
    }
}
