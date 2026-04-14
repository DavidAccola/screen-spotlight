using System.Windows;
using System.Windows.Interop;

namespace SpotlightOverlay.Windows;

public partial class ConfirmResetDialog : Window
{
    public ConfirmResetDialog()
    {
        InitializeComponent();
    }

    private void Border_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
