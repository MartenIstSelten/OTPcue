using OTPcue.Platform;
using System.ComponentModel;
using Microsoft.UI.Xaml.Media;

namespace OTPcue.WinUI;

internal sealed class AccountView : INotifyPropertyChanged
{
    public MailAccount Account
    {
        get; private set;
    }
    private LocalizedText _status = new(TextId.StartingConnection);
    public string DisplayName => Account.DisplayName;
    public string Description => Account.Username + " · " + Account.Host;
    public string Status => _status.ToString();
    public string? StatusToolTip => _status.Id is TextId.EmailConnectionFailedRetrying or TextId.GoogleConnectionFailedRetrying
        ? _status.Arguments[0].ToString() : null;
    public Brush StatusForeground => new SolidColorBrush(_status.Id switch
    {
        TextId.StartingConnection => Windows.UI.Color.FromArgb(255, 240, 213, 167),
        TextId.EmailConnectionFailedRetrying or TextId.GoogleConnectionFailedRetrying
            or TextId.MailMonitoringFailed or TextId.GoogleMonitoringFailed
            or TextId.OAuthSignInRequired or TextId.GoogleSignInRequired
            => Windows.UI.Color.FromArgb(255, 255, 180, 180),
        TextId.MailConnected or TextId.GoogleConnectedOnlyNewINBOXEmailsAreChecked
            or TextId.NewEmailCodeDetected or TextId.NewGoogleEmailCodeDetected
            => Windows.UI.Color.FromArgb(255, 139, 226, 205),
        _ => Windows.UI.Color.FromArgb(255, 179, 192, 202)
    });
    internal void SetStatus(LocalizedText status)
    {
        _status = status;
        Changed(nameof(Status));
        Changed(nameof(StatusForeground));
        Changed(nameof(StatusToolTip));
    }
    public string EnabledLabel => Account.Enabled ? Localization.Text(TextId.Enabled) : Localization.Text(TextId.Disabled);
    public AccountView(MailAccount account)
    {
        Account = account;
        if (!account.Enabled)
        {
            _status = new(TextId.Disabled);
        }
    }
    public void Replace(MailAccount account)
    {
        Account = account;
        Changed(nameof(DisplayName));
        Changed(nameof(Description));
        Changed(nameof(EnabledLabel));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void RefreshLanguage()
    {
        Changed(nameof(Status));
        Changed(nameof(EnabledLabel));
    }
    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
