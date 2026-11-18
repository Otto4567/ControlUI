using System.Collections.ObjectModel;
using Jamesnet.Wpf.Mvvm;
using ControlUI.Operation.Local.Models;

namespace ControlUI.Operation.Local.ViewModels
{
    public class OperationContentViewModel : ObservableBase
    {
        // null은 아직 장비에서 읽지 않은 값입니다. 임의의 정상 상태를 표시하지 않습니다.
        public ObservableCollection<AxisStatusInfo> Axes { get; } =
        [
            new("X"),
            new("Y"),
            new("Z")
        ];
    }
}
