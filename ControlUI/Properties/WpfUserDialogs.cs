using ControlUI.Support.Services;
using Microsoft.Win32;
using System.Windows;

namespace ControlUI.Properties;

internal sealed class WpfUserDialogs : IUserDialogs
{
    private static Window? Owner =>
        Application.Current.MainWindow is { IsVisible: true } window ? window : null;

    public string? OpenFile(string filter, string? title, string? directory, string? fileName)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter, CheckFileExists = true, Title = title ?? "",
            InitialDirectory = directory ?? "", FileName = fileName ?? ""
        };
        return Show(dialog) ? dialog.FileName : null;
    }

    public string? SaveFile(string filter, string extension, string fileName, string? title)
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter, DefaultExt = extension, FileName = fileName,
            Title = title ?? "", OverwritePrompt = true
        };
        return Show(dialog) ? dialog.FileName : null;
    }

    public string? OpenFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return Show(dialog) ? dialog.FolderName : null;
    }

    public bool Confirm(string message, string title)
    {
        var result = Owner is { } owner
            ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    private static bool Show(CommonDialog dialog) =>
        (Owner is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;
}
