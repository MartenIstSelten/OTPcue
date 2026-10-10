using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OTPcue.WinUI;

internal sealed class AccountFormErrors
{
    private readonly Dictionary<Control, TextBlock> errors = new();

    internal void Add(StackPanel parent, Control field)
    {
        parent.Children.Add(field);
        var error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Salmon),
            Visibility = Visibility.Collapsed
        };
        errors.Add(field, error);
        parent.Children.Add(error);
        switch (field)
        {
            case TextBox text:
                text.TextChanged += (_, _) => Clear(error);
                break;
            case PasswordBox password:
                password.PasswordChanged += (_, _) => Clear(error);
                break;
            case NumberBox number:
                number.ValueChanged += (_, _) => Clear(error);
                break;
            case ComboBox selection:
                selection.SelectionChanged += (_, _) => Clear(error);
                break;
        }
    }

    internal void Show(Control field, string message)
    {
        errors[field].Text = message;
        errors[field].Visibility = Visibility.Visible;
        field.Focus(FocusState.Programmatic);
    }

    internal void Clear()
    {
        foreach (var error in errors.Values)
        {
            Clear(error);
        }
    }

    private static void Clear(TextBlock error)
    {
        error.Text = "";
        error.Visibility = Visibility.Collapsed;
    }
}
