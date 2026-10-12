using OTPinsert.Platform;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace OTPinsert.WinUI;

internal sealed class PopupCard : UserControl
{
    public event Action? InsertRequested;
    public event Action? DismissRequested;
    private readonly TextBlock _codeLabel = new() { FontSize = 21, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(255, 245, 250, 252)) };
    private readonly TextBlock _sourceLabel = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(Color.FromArgb(220, 191, 207, 215)) };
    private readonly Button _insertButton = new()
    {
        Content = Localization.Text(TextId.Insert),
        Padding = new Thickness(10, 4, 10, 4),
        FontSize = 12,
        MinHeight = 0,
        Height = 28,
        Background = new SolidColorBrush(Color.FromArgb(255, 139, 226, 205)),
        Foreground = new SolidColorBrush(Color.FromArgb(255, 17, 44, 39)),
        VerticalAlignment = VerticalAlignment.Center
    };
    private bool _hovered;
    private readonly SolidColorBrush _cardBackground = new(Color.FromArgb(32, 21, 34, 47));

    public void SetCode(OTPinsert.Core.Otp code)
    {
        _codeLabel.Text = code.Code.Length switch
        {
            6 => code.Code[..3] + " " + code.Code[3..],
            8 => code.Code[..4] + " " + code.Code[4..],
            _ => code.Code
        };
        _sourceLabel.Text = code.Source;
        UpdateRemaining(code.ExpiresAt);
    }
    private readonly TextBlock _countdown = new()
    {
        FontSize = 10,
        Foreground = new SolidColorBrush(Color.FromArgb(205, 216, 230, 235)),
        HorizontalAlignment = HorizontalAlignment.Right,
        IsHitTestVisible = false
    };

    public void UpdateRemaining(DateTimeOffset expiresAt)
    {
        _insertButton.Content = Localization.Text(TextId.Insert);
        if (_dismissButton is { } dismiss)
        {
            ToolTipService.SetToolTip(dismiss, Localization.Text(TextId.DiscardThisCode));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dismiss, Localization.Text(TextId.DiscardThisCode));
        }
        var seconds = Math.Clamp((int)Math.Ceiling((expiresAt - DateTimeOffset.UtcNow).TotalSeconds), 0, 60);
        _countdown.Text = $"{seconds / 60}:{seconds % 60:00}";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_countdown, Localization.Format(TextId.AccessibleCountdown, seconds));
    }

    public void ClearCode()
    {
        SetHovered(false);
        _codeLabel.Text = _sourceLabel.Text = _countdown.Text = "";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_countdown, "");
    }

    private Button? _dismissButton;
    public PopupCard()
    {
        var card = new Border
        {
            Margin = new Thickness(0),
            Padding = new Thickness(14, 5, 14, 5),
            CornerRadius = new CornerRadius(13),
            BorderThickness = new Thickness(0),
            Background = _cardBackground,
            Child = BuildContent()
        };
        Content = card;
        RequestedTheme = ElementTheme.Dark;
        Background = new SolidColorBrush(Colors.Transparent);
        card.PointerEntered += (_, _) => SetHovered(true);
        card.PointerExited += (_, _) => SetHovered(false);
        card.Tapped += (_, args) =>
        {
            // Buttons own their click actions: do not insert twice or insert on dismissal.
            if (IsButtonContent(args.OriginalSource as DependencyObject))
            {
                return;
            }

            args.Handled = true;
            InsertRequested?.Invoke();
        };
        _insertButton.Click += (_, _) => InsertRequested?.Invoke();
        _insertButton.PointerExited += (_, _) =>
        {
            // Leaving the button for the code/spacing still counts as hovering the card.
            if (_hovered)
            {
                VisualStateManager.GoToState(_insertButton, "PointerOver", true);
            }
        };
    }

    private void SetHovered(bool hovered)
    {
        _hovered = hovered;
        _cardBackground.Color = hovered
            ? Color.FromArgb(40, 52, 69, 80)
            : Color.FromArgb(32, 21, 34, 47);
        VisualStateManager.GoToState(_insertButton, hovered ? "PointerOver" : "Normal", true);
    }

    private static bool IsButtonContent(DependencyObject? element)
    {
        for (; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is Button)
            {
                return true;
            }
        }
        return false;
    }

    private UIElement BuildContent()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 0 };
        left.Children.Add(_codeLabel);
        left.Children.Add(_sourceLabel);
        Grid.SetColumn(left, 0);

        var dismiss = new Button
        {
            Content = "×",
            Width = 24,
            Height = 24,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            FontSize = 18,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromArgb(230, 216, 230, 235)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _dismissButton = dismiss;
        ToolTipService.SetToolTip(dismiss, Localization.Text(TextId.DiscardThisCode));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dismiss, Localization.Text(TextId.DiscardThisCode));
        dismiss.Click += (_, _) => DismissRequested?.Invoke();
        var right = new Grid { VerticalAlignment = VerticalAlignment.Stretch };
        right.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        right.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dismissAndTimer = new StackPanel
        {
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 0
        };
        _countdown.HorizontalAlignment = HorizontalAlignment.Center;
        _countdown.VerticalAlignment = VerticalAlignment.Center;
        dismissAndTimer.Children.Add(dismiss);
        dismissAndTimer.Children.Add(_countdown);
        Grid.SetColumn(dismissAndTimer, 1);
        right.Children.Add(_insertButton);
        right.Children.Add(dismissAndTimer);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }
}
