using CommunityToolkit.Mvvm.ComponentModel;

namespace ControlUI.Operation.Local.Models
{
    public partial class AxisStatusInfo : ObservableObject
    {
        public string Axis { get; }
        public int Number { get; }
        public double MillimetersPerPulse { get; }
        public double MaxSpeed { get; }

        [ObservableProperty] private double? _commandPosition;
        [ObservableProperty] private bool? _servoOn;
        [ObservableProperty] private bool? _alarm;
        [ObservableProperty] private bool? _moving;
        [ObservableProperty] private bool? _inPosition;
        [ObservableProperty] private bool? _homeSignal;
        [ObservableProperty] private bool? _positiveLimit;
        [ObservableProperty] private bool? _negativeLimit;
        [ObservableProperty] private bool? _emergency;

        [ObservableProperty]
        private bool? _isConnected;

        [ObservableProperty]
        private double? _position;

        [ObservableProperty]
        private double? _speed;

        [ObservableProperty]
        private bool? _isHomed;

        public AxisStatusInfo(string axis, int number, double millimetersPerPulse, double maxSpeed)
        {
            Axis = axis;
            Number = number;
            MillimetersPerPulse = millimetersPerPulse;
            MaxSpeed = maxSpeed;
        }
    }
}
