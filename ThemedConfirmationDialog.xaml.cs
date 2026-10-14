using System.Windows;
using System.Windows.Input;

namespace SpriteStudio;

public partial class ThemedConfirmationDialog : Window
{
    private ThemedConfirmationDialog(string title, string message, string yesText, string noText)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        YesButton.Content = yesText;
        NoButton.Content = noText;
    }

    public static bool Confirm(Window owner, string title, string message, string yesText, string noText,
        bool defaultNo = false)
    {
        var dialog = new ThemedConfirmationDialog(title, message, yesText, noText)
        {
            Owner = owner
        };
        dialog.YesButton.IsDefault = !defaultNo;
        dialog.NoButton.IsDefault = defaultNo;
        return dialog.ShowDialog() == true;
    }

    private void YesButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void NoButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
