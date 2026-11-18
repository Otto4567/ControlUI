using CommunityToolkit.Mvvm.ComponentModel;

namespace ControlUI.Operation.Local.Models
{
    public partial class AxisStatusInfo : ObservableObject
    {
        public string Axis { get; }

        [ObservableProperty]
        private bool? _isConnected;

        [ObservableProperty]
        private double? _position;

        [ObservableProperty]
        private double? _speed;

        // 현재 이동에 적용된 가속·감속 설정값입니다.
        [ObservableProperty]
        private double? _acceleration;

        [ObservableProperty]
        private double? _deceleration;

        [ObservableProperty]
        private bool? _isHomed;

        public AxisStatusInfo(string axis)
        {
            Axis = axis;
        }
    }
}
