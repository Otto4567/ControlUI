using CommunityToolkit.Mvvm.ComponentModel;
using ControlUI.Support.Services;

namespace ControlUI.Manual.Local.Models;

public partial class MotionAxisProfile(
    string axis, int axisNumber, double micrometersPerPulse, double? maxSpeed,
    JogPositiveDirection jogDirection) : ObservableObject
{
    public string Axis { get; } = axis;
    public bool IsJogHorizontal => jogDirection is JogPositiveDirection.Left or JogPositiveDirection.Right;
    public bool IsFirstJogPositive => jogDirection is JogPositiveDirection.Up or JogPositiveDirection.Left;
    public string FirstJogLabel => $"{(IsJogHorizontal ? "←" : "↑")} Jog {(IsFirstJogPositive ? "+" : "−")}";
    public string SecondJogLabel => $"{(IsJogHorizontal ? "→" : "↓")} Jog {(IsFirstJogPositive ? "−" : "+")}";

    public int AxisNumber { get; } = axisNumber;
    public string Label => $"{Axis} ({AxisNumber})";
    public double MicrometersPerPulse { get; } = micrometersPerPulse;
    public AxisSignalValue ServoOn { get; } = new();
    public AxisSignalValue Alarm { get; } = new();
    public AxisSignalValue Moving { get; } = new();
    public AxisSignalValue InPosition { get; } = new();
    public AxisSignalValue HomeSignal { get; } = new();
    public AxisSignalValue Homed { get; } = new();
    public AxisSignalValue PositiveLimit { get; } = new();
    public AxisSignalValue NegativeLimit { get; } = new();
    public AxisSignalValue Emergency { get; } = new();
    public double MillimetersPerNativeUnit { get; internal set; }
    public bool JogHeld { get; set; }
    public System.Threading.CancellationTokenSource? Repeat { get; set; }

    [ObservableProperty] private string? _alarmCode;
    [ObservableProperty] private double? _commandPositionMm;
    [ObservableProperty] private double? _actualPositionMm;
    [ObservableProperty] private double? _actualVelocityMmPerSecond;


    [ObservableProperty]
    private double? _maxSpeedMillimetersPerSecond = maxSpeed;

    [ObservableProperty]
    private double _targetPositionMm;

    [ObservableProperty]
    private double _velocityMmPerSecond;

    [ObservableProperty]
    private double _accelMmPerSecondSquared;

    [ObservableProperty]
    private double _decelMmPerSecondSquared;

    [ObservableProperty]
    private int _moveModeIndex;

    [ObservableProperty]
    private string _sdkSettings = "—";

    [ObservableProperty] private int _repeatCount = 1;
    [ObservableProperty] private int _repeatDelayMilliseconds = 500;
}
