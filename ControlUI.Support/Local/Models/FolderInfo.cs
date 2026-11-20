using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace ControlUI.Support.Local.Models
{

    public partial class FolderInfo : FileInfoBase
    {
        [ObservableProperty]
        private bool _isFolderExpanded;
        [ObservableProperty]
        private bool _isFolderSelected;

        public ObservableCollection<FolderInfo> Children { get; set; } = new();
    }
}