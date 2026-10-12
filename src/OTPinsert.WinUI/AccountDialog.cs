using OTPinsert.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;

namespace OTPinsert.WinUI;

internal static class AccountDialog
{
    public static async Task<MailAccount?> ShowAsync(XamlRoot root, MailAccount? existing = null)
    {
        var folderSelection = new AccountFolderSelection(existing);
        var dialogClosed = false;
        var name = new TextBox { Header = Localization.Text(TextId.CustomAccountNameOptional), Text = existing?.DisplayName ?? "", PlaceholderText = Localization.Text(TextId.ForExamplePersonalOrWork) };
        var id = existing?.Id ?? Guid.NewGuid();
        var provider = new ComboBox { Header = Localization.Text(TextId.EmailProvider), HorizontalAlignment = HorizontalAlignment.Stretch };
        MailProvider[] providers = [MailProvider.Custom, MailProvider.Gmail, MailProvider.ICloud, MailProvider.Google];
        foreach (var label in new[] { Localization.Text(TextId.OtherProviderIMAP), Localization.Text(TextId.GmailAppPassword), Localization.Text(TextId.ICloudAppPassword), Localization.Text(TextId.GmailOAuthSignInWithGoogle) })
        {
            provider.Items.Add(label);
        }

        provider.SelectedIndex = Math.Max(0, Array.IndexOf(providers, existing?.Provider ?? MailProvider.Custom));
        var clientId = new TextBox { Header = Localization.Text(TextId.GoogleOAuthClientID), Text = existing?.Provider == MailProvider.Google ? existing.OAuthClientId : GoogleOAuthConfiguration.DefaultClientId, PlaceholderText = Localization.Text(TextId.PublicClientIDOfTheAppRegistration) };
        var googleSecret = new PasswordBox { Header = Localization.Text(TextId.GoogleDesktopClientSecret), Password = existing?.OAuthClientSecret ?? GoogleOAuthConfiguration.DefaultClientSecret };
        var googleAdvanced = new CheckBox { Content = Localization.Text(TextId.CustomGoogleAppConfigurationForForks), IsChecked = existing?.Provider == MailProvider.Google && (existing.OAuthClientId != GoogleOAuthConfiguration.DefaultClientId || existing.OAuthClientSecret != GoogleOAuthConfiguration.DefaultClientSecret) };
        var muted = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 179, 192, 202));
        var success = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 139, 226, 205));
        var working = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 240, 213, 167));
        var failure = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 180, 180));
        var providerHelp = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = muted };
        var username = new TextBox { Header = Localization.Text(TextId.IMAPUsernameEmail), Text = existing?.Username ?? "" };
        var host = new TextBox { Header = Localization.Text(TextId.IMAPServer), Text = existing?.Host ?? "" };
        var port = new NumberBox { Header = "Port", Value = existing?.Port ?? 993, Minimum = 1, Maximum = 65535 };
        var transport = new ComboBox { Header = Localization.Text(TextId.EncryptionLabel), HorizontalAlignment = HorizontalAlignment.Stretch };
        transport.Items.Add(Localization.Text(TextId.SSLTLSUsuallyPort));
        transport.Items.Add(Localization.Text(TextId.STARTTLSUsuallyPort));
        transport.SelectedIndex = existing?.StartTls == true ? 1 : 0;
        transport.SelectionChanged += (_, _) => port.Value = transport.SelectedIndex == 1 ? 143 : 993;
        var password = new PasswordBox { Header = Localization.Text(TextId.IMAPAppPassword), PlaceholderText = existing is null ? Localization.Text(TextId.EnterPassword) : Localization.Text(TextId.LeaveEmptyToKeepTheSavedPassword) };
        var folders = new ComboBox { Header = Localization.Text(TextId.AccountDetailsFolder), HorizontalAlignment = HorizontalAlignment.Stretch };
        var loadFolders = new Button { Content = Localization.Text(TextId.ReloadMailFolders) };
        void RenderFolders()
        {
            folders.Items.Clear();
            foreach (var folder in folderSelection.Choices)
            {
                var item = new ComboBoxItem { Content = folder.Name, Tag = folder };
                folders.Items.Add(item);
                if (folder.Id == folderSelection.Selected.Id)
                {
                    folders.SelectedItem = item;
                }
            }
        }
        RenderFolders();
        folders.SelectionChanged += (_, _) =>
        {
            if (folders.SelectedItem is ComboBoxItem { Tag: MailFolder folder })
            {
                folderSelection.Select(folder);
            }
        };
        var polling = new NumberBox
        {
            Header = Localization.Text(TextId.CheckEmailsEverySeconds),
            Value = existing?.PollingIntervalSeconds ?? 3,
            Minimum = 1,
            Maximum = 600,
            SmallChange = 1,
            LargeChange = 10,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Visibility = Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
        void SetFeedback(string message, Brush color)
        {
            if (dialogClosed)
            {
                return;
            }
            feedback.Text = message;
            feedback.Foreground = color;
            feedback.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }
        var saveHint = new TextBlock
        {
            Text = Localization.Text(TextId.GoogleSignInBeforeSaving),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = muted,
            Visibility = Visibility.Collapsed
        };
        var panel = new StackPanel { Spacing = 12, Width = 400 };
        var advancedPanel = new StackPanel { Spacing = 12 };
        var advanced = new Expander
        {
            Header = Localization.Text(TextId.AdvancedSettings),
            Content = advancedPanel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        var errors = new AccountFormErrors();
        MailAccount? Invalid(Control field, string message)
        {
            if (field == port || field == polling)
            {
                advanced.IsExpanded = true;
            }

            if (field == clientId && provider.SelectedIndex >= 0 && providers[provider.SelectedIndex] == MailProvider.Google)
            {
                googleAdvanced.IsChecked = true;
            }

            errors.Show(field, message);
            return null;
        }
        void UpdateProvider(bool preset)
        {
            var selected = (provider.SelectedIndex >= 0 ? providers[provider.SelectedIndex] : (MailProvider)(-1));
            var google = selected == MailProvider.Google;
            clientId.Header = Localization.Text(TextId.GoogleOAuthClientID);
            googleAdvanced.Visibility = google ? Visibility.Visible : Visibility.Collapsed;
            clientId.Visibility = google && googleAdvanced.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            googleSecret.Visibility = google && googleAdvanced.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            password.Visibility = google ? Visibility.Collapsed : Visibility.Visible;
            host.Visibility = port.Visibility = transport.Visibility = google ? Visibility.Collapsed : Visibility.Visible;
            host.IsEnabled = port.IsEnabled = transport.IsEnabled = !google;
            if (preset)
            {
                clientId.Text = existing?.Provider == selected ? existing.OAuthClientId : GoogleOAuthConfiguration.DefaultClientId;
                googleSecret.Password = existing?.Provider == selected ? existing.OAuthClientSecret : GoogleOAuthConfiguration.DefaultClientSecret;
            }
            if (preset && selected != MailProvider.Custom)
            {
                host.Text = selected switch
                {
                    MailProvider.Gmail => "imap.gmail.com",
                    MailProvider.ICloud => "imap.mail.me.com",
                    MailProvider.Google => "gmail.googleapis.com",
                    _ => host.Text
                };
                transport.SelectedIndex = 0;
                port.Value = google ? 443 : 993;
                password.Password = "";
            }
            providerHelp.Text = selected switch
            {
                MailProvider.Gmail => Localization.Text(TextId.GmailAppPasswordHelp),
                MailProvider.ICloud => Localization.Text(TextId.ICloudAppPasswordHelp),
                MailProvider.Google => Localization.Text(TextId.GoogleConnectionHelp),
                _ => Localization.Text(TextId.ImapConnectionHelp)
            };
        }
        UpdateProvider(false);
        provider.SelectionChanged += (_, _) => UpdateProvider(true);
        googleAdvanced.Checked += (_, _) => UpdateProvider(false);
        googleAdvanced.Unchecked += (_, _) =>
        {
            if (provider.SelectedIndex >= 0 && providers[provider.SelectedIndex] == MailProvider.Google)
            {
                clientId.Text = GoogleOAuthConfiguration.DefaultClientId;
                googleSecret.Password = GoogleOAuthConfiguration.DefaultClientSecret;
            }
            UpdateProvider(false);
        };
        errors.Add(panel, provider);
        panel.Children.Add(providerHelp);
        errors.Add(panel, name);
        panel.Children.Add(new TextBlock
        {
            Text = Localization.Text(TextId.AccountDisplayNameHelp),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = muted
        });
        errors.Add(panel, username);
        panel.Children.Add(googleAdvanced);
        errors.Add(panel, clientId);
        errors.Add(panel, googleSecret);
        errors.Add(panel, host);
        errors.Add(panel, password);
        panel.Children.Add(folders);
        panel.Children.Add(loadFolders);
        panel.Children.Add(new TextBlock { Text = Localization.Text(TextId.FolderSelectionHelp), TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = muted });
        advancedPanel.Children.Add(transport);
        errors.Add(advancedPanel, port);
        errors.Add(advancedPanel, polling);
        advancedPanel.Children.Add(new TextBlock { Text = Localization.Text(TextId.ToSecondsDefaultSeconds), TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = muted });
        panel.Children.Add(advanced);
        panel.Children.Add(new TextBlock
        {
            Text = Localization.Text(TextId.ReadOnlyMailboxNotice),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = muted
        });
        var content = new StackPanel { Spacing = 12, Width = 400 };
        var form = new ScrollViewer { Content = panel, MaxHeight = 450 };
        content.Children.Add(form);
        content.Children.Add(saveHint);
        content.Children.Add(feedback);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = existing is null ? Localization.Text(TextId.AddEmailAccount) : Localization.Text(TextId.EditAccount),
            Content = content,
            RequestedTheme = ElementTheme.Dark,
            PrimaryButtonText = Localization.Text(TextId.Save),
            SecondaryButtonText = Localization.Text(TextId.TestConnection),
            CloseButtonText = Localization.Text(TextId.Cancel)
        };
        MailAccount? result = null;
        var verification = new AccountVerification(existing is not null && existing.Provider == MailProvider.Google && GoogleMailAuth.HasCache(id) ? existing : null);
        void UpdateSaveHint()
        {
            var google = provider.SelectedIndex >= 0 && providers[provider.SelectedIndex] == MailProvider.Google;
            var candidate = new MailAccount(id, name.Text, host.Text, 443, username.Text.Trim(), "", false, existing?.Enabled ?? true)
            {
                Provider = MailProvider.Google,
                OAuthClientId = clientId.Text.Trim(),
                OAuthClientSecret = googleSecret.Password
            };
            saveHint.Visibility = google && !verification.Matches(candidate) ? Visibility.Visible : Visibility.Collapsed;
        }
        void FormChanged()
        {
            SetFeedback("", muted);
            var selectedProvider = provider.SelectedIndex >= 0 ? providers[provider.SelectedIndex] : (MailProvider)(-1);
            if (folderSelection.UpdateMailbox(selectedProvider, host.Text, username.Text))
            {
                RenderFolders();
            }
            UpdateSaveHint();
        }
        provider.SelectionChanged += (_, _) => FormChanged();
        username.TextChanged += (_, _) => FormChanged();
        host.TextChanged += (_, _) => FormChanged();
        password.PasswordChanged += (_, _) => FormChanged();
        clientId.TextChanged += (_, _) => FormChanged();
        googleSecret.PasswordChanged += (_, _) => FormChanged();
        transport.SelectionChanged += (_, _) => FormChanged();
        port.ValueChanged += (_, _) => FormChanged();
        UpdateSaveHint();
        Task? pendingOperation = null;
        dialog.Closed += (_, _) =>
        {
            dialogClosed = true;
            folderSelection.Cancel();
            verification.Cancel();
        };
        void UpdateButton() => dialog.SecondaryButtonText = (provider.SelectedIndex >= 0 ? providers[provider.SelectedIndex] : (MailProvider)(-1)) switch
        {
            MailProvider.Google => Localization.Text(TextId.SignInWithGoogle),
            _ => Localization.Text(TextId.TestConnection)
        };
        UpdateButton();
        provider.SelectionChanged += (_, _) => UpdateButton();
        MailAccount? Read()
        {
            SetFeedback("", muted);
            errors.Clear();
            var selectedProvider = (provider.SelectedIndex >= 0 ? providers[provider.SelectedIndex] : (MailProvider)(-1));
            var secret = string.IsNullOrEmpty(password.Password) && existing?.Provider == selectedProvider
                ? existing.Password
                : password.Password;
            var draft = new AccountDraft(id, name.Text, host.Text, port.Value, username.Text, secret,
                transport.SelectedIndex == 1, existing?.Enabled ?? true,
                polling.Value, selectedProvider, clientId.Text, googleSecret.Password);
            if (draft.Validate() is { } validationError)
            {
                var field = validationError.Field switch
                {
                    AccountField.Provider => (Control)provider,
                    AccountField.PollingInterval => polling,
                    AccountField.ClientId => clientId,
                    AccountField.Username => username,
                    AccountField.Host => host,
                    AccountField.Port => port,
                    AccountField.Password => password,
                    _ => throw new InvalidOperationException("Unbekanntes Kontofeld.")
                };
                return Invalid(field, validationError.Message);
            }
            return folderSelection.Apply(draft.ToAccount());
        }
        void SetBusy(bool busy)
        {
            if (!dialogClosed)
            {
                dialog.IsPrimaryButtonEnabled = dialog.IsSecondaryButtonEnabled = form.IsEnabled = !busy;
            }
        }
        async Task<bool> RunFormOperationAsync(Func<Task> action, TextId progress, TextId error, string diagnosticOperation)
        {
            SetBusy(true);
            SetFeedback(Localization.Text(progress), working);
            try
            {
                await action();
                return !dialogClosed;
            }
            catch (OperationCanceledException) when (dialogClosed) { return false; }
            catch (Exception ex)
            {
                AppDiagnostics.ReportFailure(diagnosticOperation, ex);
                SetFeedback(Localization.Text(error), failure);
                return false;
            }
            finally
            {
                SetBusy(false);
            }
        }
        async Task LoadFoldersAsync()
        {
            var account = Read();
            if (account is null)
            {
                return;
            }
            if (account.UsesOAuth && !verification.Matches(account))
            {
                SetFeedback(Localization.Text(TextId.ConnectionVerificationRequired), failure);
                return;
            }
            await RunFormOperationAsync(async () =>
            {
                var loaded = await folderSelection.LoadAsync(account);
                if (loaded)
                {
                    RenderFolders();
                }
                SetFeedback(loaded ? "" : Localization.Text(TextId.NoMailFolders), loaded ? muted : failure);
            }, TextId.LoadingMailFolders, TextId.MailFoldersLoadFailed, "MailFolders.List");
        }
        loadFolders.Click += async (_, _) =>
        {
            pendingOperation = LoadFoldersAsync();
            await pendingOperation;
        };
        async Task SaveAsync()
        {
            result = null;
            var account = Read();
            if (account is null)
            {
                return;
            }
            if (account.UsesOAuth && !verification.Matches(account))
            {
                SetFeedback(Localization.Text(TextId.ConnectionVerificationRequired), failure);
                return;
            }
            if (!folderSelection.NeedsVerification(account)
                || await RunFormOperationAsync(() => folderSelection.VerifyAsync(account),
                    TextId.TestingConnection, TextId.MailFolderVerificationFailed, "MailFolders.Verify"))
            {
                result = account;
            }
        }
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                pendingOperation = SaveAsync();
                await pendingOperation;
                args.Cancel = result is null;
            }
            finally { deferral.Complete(); }
        };
        async Task TestAsync()
        {
            var account = Read();
            if (account is null)
            {
                return;
            }

            SetBusy(true);

            SetFeedback(Localization.Text(TextId.TestingConnection), working);
            var reportingProgress = true;
            try
            {
                var progress = new Progress<string>(message =>
                {
                    if (reportingProgress && !dialogClosed)
                    {
                        SetFeedback(message, working);
                    }
                });
                await verification.VerifyAsync(account, progress);
                SetFeedback(account.Provider == MailProvider.Google
                    ? Localization.Text(TextId.GoogleSignInSuccessfulGmailIsReadableSaveNow)
                    : Localization.Text(TextId.ConnectionAndSignInSuccessfulINBOXIsReadable), success);
            }
            catch (GoogleAccountMismatchException)
            {
                SetFeedback(Localization.Text(TextId.GoogleAccountMismatch), failure);
            }
            catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException ex) when (account.Provider == MailProvider.Google)
            {
                SetFeedback(GoogleMailAuth.DescribeSignInFailure(ex), failure);
            }
            catch (HttpRequestException ex) when (account.Provider == MailProvider.Google && ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                SetFeedback(Localization.Text(TextId.GoogleAccessDenied), failure);
            }
            catch (OperationCanceledException)
            {
                SetFeedback(Localization.Text(TextId.SignInWasCanceledOrTimedOutPleaseTryAgain), failure);
            }
            catch (Exception ex)
            {
                AppDiagnostics.ReportFailure("AccountConnection.Verify", ex);
                SetFeedback(Localization.Text(TextId.ConnectionFailed) + ex.GetType().Name + Localization.Text(TextId.CheckCredentialsOAuthConfigurationAndTheSelectedAccount), failure);
            }
            finally
            {
                reportingProgress = false;
                SetBusy(false);
                if (!dialogClosed)
                {
                    UpdateProvider(false);
                    UpdateSaveHint();
                }
            }
        }
        dialog.SecondaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            args.Cancel = true;
            try
            {
                pendingOperation = TestAsync();
                await pendingOperation;
            }
            finally { deferral.Complete(); }
        };
        var saved = await dialog.ShowAsync() == ContentDialogResult.Primary;
        if (pendingOperation is not null)
        {
            await pendingOperation;
        }

        if (existing is null && (!saved || result?.Provider != MailProvider.Google))
        {
            GoogleMailAuth.DeleteCache(id);
        }

        return saved ? result : null;
    }
}
