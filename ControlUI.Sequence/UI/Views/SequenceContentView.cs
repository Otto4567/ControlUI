using Jamesnet.Wpf.Controls;
using System.Windows;

namespace ControlUI.Sequence.UI.Views
{
    public class SequenceContentView : JamesContent
    {
        static SequenceContentView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(SequenceContentView),
                new FrameworkPropertyMetadata(typeof(SequenceContentView)));
        }
    }
}