using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OTPinsert.Platform;

namespace OTPinsert.WinUI;

public sealed partial class MainWindow
{
    private void StartAccount(AccountView view)
    {
        if (_demo || !view.Account.Enabled || _exiting)
        {
            view.SetStatus(new(_demo ? TextId.PausedInDemoMode : TextId.Disabled));
            return;
        }
        view.SetStatus(new(TextId.StartingConnection));
        _monitor.Start(view.Account, (message, token) => OnUi(() =>
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            view.SetStatus(message);
            if (Selected == view)
            {
                UpdateDetails();
            }
        }));
    }
    private void Persist(IEnumerable<MailAccount> accounts)
    {
        if (!_storageReady)
        {
            throw new InvalidOperationException("Kontodatei ist nicht verfügbar.");
        }

        _repository.Save(accounts);
    }

    private async Task ChangeAsync(Func<Task> action)
    {
        if (_busy || _updating || _exiting)
        {
            return;
        }

        _busy = true;
        UpdateDetails();
        DemoButton.IsEnabled = AddButton.IsEnabled = false;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AppDiagnostics.ReportFailure("Accounts.Change", ex);
            SetStatus(new(TextId.AccountChangeFailed, ex.GetType().Name));
        }
        finally { _busy = false; UpdateDetails(); DemoButton.IsEnabled = true; AddButton.IsEnabled = _storageReady; }
    }

    private bool IsDuplicate(MailAccount account) => _accounts.Any(view => view.Account.Id != account.Id
        && string.Equals(view.Account.Host, account.Host, StringComparison.OrdinalIgnoreCase)
        && view.Account.Port == account.Port && view.Account.StartTls == account.StartTls
        && string.Equals(view.Account.Username, account.Username, StringComparison.OrdinalIgnoreCase));

    private async void AddClicked(object sender, RoutedEventArgs e) => await ChangeAsync(async () =>
    {
        var account = await AccountDialog.ShowAsync(Content.XamlRoot);
        if (account is null)
        {
            return;
        }

        if (IsDuplicate(account))
        {
            GoogleMailAuth.DeleteCache(account.Id);
            SetStatus(new(TextId.ThisAccountAlreadyExists));
            return;
        }
        try
        {
            Persist(_accounts.Select(v => v.Account).Append(account));
        }
        catch { GoogleMailAuth.DeleteCache(account.Id); throw; }
        var view = new AccountView(account);
        _accounts.Add(view);
        StartAccount(view);
        AccountList.SelectedItem = view;
        SetStatus(new(TextId.AccountAdded));
    });

    private async void EditClicked(object sender, RoutedEventArgs e)
    {
        var view = Selected;
        if (view is null)
        {
            return;
        }

        await ChangeAsync(async () =>
        {
            var account = await AccountDialog.ShowAsync(Content.XamlRoot, view.Account);
            if (account is null)
            {
                return;
            }

            if (IsDuplicate(account))
            {
                SetStatus(new(TextId.ThisAccountAlreadyExists));
                return;
            }
            await CommitAccountChangeAsync(view, account, TextId.AccountUpdated,
                account.Provider != MailProvider.Google ? () => GoogleMailAuth.DeleteCache(account.Id) : null);
        });
    }

    private async void EnableClicked(object sender, RoutedEventArgs e)
    {
        var view = Selected;
        if (view is null)
        {
            return;
        }

        await ChangeAsync(async () =>
        {
            var account = view.Account with
            {
                Enabled = !view.Account.Enabled
            };
            await CommitAccountChangeAsync(view, account);
        });
    }

    private async void RemoveClicked(object sender, RoutedEventArgs e)
    {
        var view = Selected;
        if (view is null)
        {
            return;
        }

        await ChangeAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
                Title = Localization.Text(TextId.RemoveAccountConfirmation),
                Content = view.DisplayName + Localization.Text(TextId.WillBeRemovedAlongWithItsSavedCredentials),
                PrimaryButtonText = Localization.Text(TextId.Remove),
                CloseButtonText = Localization.Text(TextId.Cancel)
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await CommitAccountChangeAsync(view, null, TextId.AccountRemoved,
                () => GoogleMailAuth.DeleteCache(view.Account.Id));
        });
    }

    private async Task CommitAccountChangeAsync(AccountView view, MailAccount? account,
        TextId? success = null, Action? cleanup = null)
    {
        var snapshot = _accounts.Where(v => v != view).Select(v => v.Account).ToList();
        if (account is not null)
        {
            // Preserve the account order when replacing an existing entry.
            snapshot = _accounts.Select(v => v == view ? account : v.Account).ToList();
        }
        var clean = await AccountChange.CommitAsync(
            () => Persist(snapshot),
            () => _monitor.StopAsync(view.Account.Id),
            () =>
            {
                _store.Clear();
                if (account is null)
                {
                    _accounts.Remove(view);
                }
                else
                {
                    view.Replace(account);
                    StartAccount(view);
                }
            }, cleanup);
        if (!clean)
        {
            SetStatus(new(TextId.AccountChangeCompletedWithWarnings));
        }
        else if (success is { } message)
        {
            SetStatus(new(message));
        }
    }

    private void AccountSelected(object sender, SelectionChangedEventArgs e) => UpdateDetails();
    private void UpdateDetails()
    {
        if (EditButton is null)
        {
            return;
        }

        var view = Selected;
        EditButton.IsEnabled = EnableButton.IsEnabled = RemoveButton.IsEnabled = view is not null && !_busy && !_updating && _storageReady;
        AddButton.IsEnabled = !_busy && !_updating && _storageReady;
        DemoButton.IsEnabled = !_busy && !_updating;
        EmptyAccounts.Visibility = _accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailsLabel.Visibility = view is null ? Visibility.Visible : Visibility.Collapsed;
        SelectedAccountDetails.Visibility = view is null ? Visibility.Collapsed : Visibility.Visible;
        if (view is null)
        {
            DetailsLabel.Text = Localization.Text(TextId.SelectAnAccount);
            ConnectionLabel.Text = "";
            return;
        }
        var account = view.Account;
        DetailsAccountName.Text = account.DisplayName;
        DetailsEmailValue.Text = account.Username;
        DetailsServerValue.Text = account.UsesOAuth ? "gmail.googleapis.com" : account.Host + ":" + account.Port;
        DetailsEncryptionValue.Text = account.UsesOAuth ? "HTTPS / TLS" : account.Transport;
        DetailsFolderValue.Text = account.FolderName;
        DetailsIntervalValue.Text = Localization.Format(TextId.AccountDetailsIntervalValue, account.PollingIntervalSeconds);
        DetailsMonitoringValue.Text = view.EnabledLabel;
        ConnectionLabel.Text = view.Status;
        ConnectionLabel.Foreground = view.StatusForeground;
        ToolTipService.SetToolTip(ConnectionLabel, view.StatusToolTip);
        EnableButton.Content = account.Enabled ? Localization.Text(TextId.DisableAccount) : Localization.Text(TextId.EnableAccount);
    }

}
