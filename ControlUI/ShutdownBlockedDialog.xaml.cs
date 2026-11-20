using System.Windows;

namespace ControlUI;

public partial class ShutdownBlockedDialog : Window
{
    internal ShutdownBlockedDialog(Window owner, string reason)
    {
        InitializeComponent();
        Owner = owner;
        FailureReason.Text = reason;
    }

    private void KeepOpen_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void ForceExit_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
