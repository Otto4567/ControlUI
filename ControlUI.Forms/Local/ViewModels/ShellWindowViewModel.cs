using Jamesnet.Wpf.Mvvm;
using ControlUI.Support.Local.Helpers;

namespace ControlUI.Forms.Local.ViewModels
{
    public class ShellWindowViewModel : ObservableBase
    {
        public string DownloadDirectory { get; init; }
        public string DocumentsDirectory { get; init; }
        public string PicturesDirectory { get; init; }

        public ShellWindowViewModel(DirectoryManager directoryManager)
        {
            DownloadDirectory = directoryManager.DownloadDirectory;
            DocumentsDirectory = directoryManager.DocumentsDirectory;
            PicturesDirectory = directoryManager.PicturesDirectory;
        }
    }
}