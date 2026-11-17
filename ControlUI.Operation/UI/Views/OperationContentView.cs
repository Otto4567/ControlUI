using Jamesnet.Wpf.Controls;
using System.Windows;

namespace ControlUI.Operation.UI.Views
{
    public class OperationContentView : JamesContent
    {
        static OperationContentView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(OperationContentView),
                new FrameworkPropertyMetadata(typeof(OperationContentView)));
        }
    }
}