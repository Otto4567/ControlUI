using System.Windows;
using System.Windows.Controls;

namespace ControlUI.Forms.UI.Units
{
    public class FolderTreeView : TreeView
    {
        static FolderTreeView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(FolderTreeView),
                new FrameworkPropertyMetadata(typeof(FolderTreeView)));
        }
    }
}