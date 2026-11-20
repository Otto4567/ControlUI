using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ControlUI.Support.Services;

namespace ControlUI.Settings.Local.ViewModels;

public enum LimitLevel { Keep = -1, Low, High, Unused, Used, LowStopOnly, HighStopOnly }
public enum InputLevel { Keep = -1, Low, High, Unused, Used }
public enum HomeLevel { Keep = -1, Low, High }
public enum StopBehavior { Keep = -1, Abrupt, Decelerate }
public enum HomeDirection { Keep = -1, Negative, Positive }
public enum HomeSignal { Keep = -1, PositiveLimit = 0, NegativeLimit = 1, HomeSensor = 4, EncoderZ = 5, Input2 = 6, Input3 = 7, Input4 = 8, Input5 = 9 }
public enum HomeZPhase { Keep = -1, Disabled, OppositeDirection, SameDirection }
public enum HomeInterlock { Keep = -1, Unused, CheckSensor, CheckDistance }

public sealed class MotionIoEditor(MotionAxisIoSettings? original = null)
{
    private readonly Dictionary<string, object> _edits = new();
    internal MotionAxisIoSettings? Original { get; } = original;

    internal MotionIoEditor WithReadback(MotionAxisIoSettings? current)
    {
        var refreshed = new MotionIoEditor(current);
        foreach (var (name, value) in _edits)
            refreshed._edits.Add(name, value);
        return refreshed;
    }

    internal MotionAxisIoSettings Merge(MotionAxisIoSettings current) => current with
    {
        PositiveLimit = (uint)ReadChoice(nameof(PositiveLimit), current.PositiveLimit),
        NegativeLimit = (uint)ReadChoice(nameof(NegativeLimit), current.NegativeLimit),
        LimitStop = (uint)ReadChoice(nameof(LimitStop), current.LimitStop),
        HomeLevel = (uint)ReadChoice(nameof(HomeLevel), current.HomeLevel),
        AlarmLevel = (uint)ReadChoice(nameof(AlarmLevel), current.AlarmLevel),
        InPositionLevel = (uint)ReadChoice(nameof(InPositionLevel), current.InPositionLevel),
        EmergencyLevel = (uint)ReadChoice(nameof(EmergencyLevel), current.EmergencyLevel),
        EmergencyStop = (uint)ReadChoice(nameof(EmergencyStop), current.EmergencyStop),
        HomeDirection = (int)ReadChoice(nameof(HomeDirection), current.HomeDirection),
        HomeSignal = (uint)ReadChoice(nameof(HomeSignal), current.HomeSignal),
        ZPhase = (uint)ReadChoice(nameof(ZPhase), current.ZPhase),
        InterlockMode = (uint)ReadChoice(nameof(InterlockMode), current.InterlockMode),
        InterlockDistance = ReadNumber(nameof(InterlockDistance), current.InterlockDistance),
        Offset = ReadNumber(nameof(Offset), current.Offset),
        ClearTime = ReadNumber(nameof(ClearTime), current.ClearTime),
        FirstSpeed = ReadNumber(nameof(FirstSpeed), current.FirstSpeed),
        SecondSpeed = ReadNumber(nameof(SecondSpeed), current.SecondSpeed),
        ThirdSpeed = ReadNumber(nameof(ThirdSpeed), current.ThirdSpeed),
        LastSpeed = ReadNumber(nameof(LastSpeed), current.LastSpeed),
        FirstAccel = ReadNumber(nameof(FirstAccel), current.FirstAccel),
        SecondAccel = ReadNumber(nameof(SecondAccel), current.SecondAccel),
    };

    private int ReadChoice(string name, long current)
    {
        if (_edits.TryGetValue(name, out var value))
        {
            int choice = System.Convert.ToInt32(value);
            if (choice >= 0)
                return choice;
        }
        return (int)current;
    }

    private double ReadNumber(string name, double current)
    {
        if (!_edits.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace((string)value))
            return current;
        if (!double.TryParse((string)value, NumberStyles.Float, CultureInfo.CurrentCulture, out double number) || !double.IsFinite(number))
            throw new ArgumentException($"'{value}' is not a valid number for {name}.");
        return number;
    }

    private T Choice<T>(T fallback, [CallerMemberName] string name = "") where T : struct, Enum =>
        _edits.TryGetValue(name, out var value) ? (T)value : fallback;

    private string Number(double? fallback, [CallerMemberName] string name = "") =>
        _edits.TryGetValue(name, out var value) ? (string)value : fallback?.ToString("G", CultureInfo.CurrentCulture) ?? "";

    private void Edit(object value, [CallerMemberName] string name = "") => _edits[name] = value;

    [Category("I/O"), DisplayName("+ Limit sensor")]
    public LimitLevel PositiveLimit
    {
        get => Choice(Original is { } board ? (LimitLevel)board.PositiveLimit : LimitLevel.Keep);
        set => Edit(value);
    }

    [Category("I/O"), DisplayName("− Limit sensor")]
    public LimitLevel NegativeLimit
    {
        get => Choice(Original is { } board ? (LimitLevel)board.NegativeLimit : LimitLevel.Keep);
        set => Edit(value);
    }

    [Category("I/O"), DisplayName("Limit stop")]
    public StopBehavior LimitStop
    {
        get => Choice(Original is { } board ? (StopBehavior)board.LimitStop : StopBehavior.Keep);
        set => Edit(value);
    }

    [Category("I/O"), DisplayName("Home sensor level")]
    public HomeLevel HomeLevel
    {
        get => Choice(Original is { } board ? (HomeLevel)board.HomeLevel : HomeLevel.Keep);
        set => Edit(value);
    }

    [Category("I/O"), DisplayName("Servo alarm input")]
    public InputLevel AlarmLevel
    {
        get => Choice(Original is { } board ? (InputLevel)board.AlarmLevel : InputLevel.Keep);
        set => Edit(value);
    }

    [Category("I/O"), DisplayName("In-position input")]
    public InputLevel InPositionLevel
    {
        get => Choice(Original is { } board ? (InputLevel)board.InPositionLevel : InputLevel.Keep);
        set => Edit(value);
    }

    [Category("I/O"), DisplayName("Emergency input")]
    public InputLevel EmergencyLevel
    {
        get => Choice(Original is { } board ? (InputLevel)board.EmergencyLevel : InputLevel.Keep);
        set => Edit(value);
    }

    [Category("I/O"), DisplayName("Emergency stop mode")]
    public StopBehavior EmergencyStop
    {
        get => Choice(Original is { } board ? (StopBehavior)board.EmergencyStop : StopBehavior.Keep);
        set => Edit(value);
    }

    [Category("Homing"), DisplayName("Direction")]
    public HomeDirection HomeDirection
    {
        get => Choice(Original is { } board ? (HomeDirection)board.HomeDirection : HomeDirection.Keep);
        set => Edit(value);
    }

    [Category("Homing"), DisplayName("Signal source")]
    public HomeSignal HomeSignal
    {
        get => Choice(Original is { } board ? (HomeSignal)board.HomeSignal : HomeSignal.Keep);
        set => Edit(value);
    }

    [Category("Homing"), DisplayName("Z-phase detection")]
    public HomeZPhase ZPhase
    {
        get => Choice(Original is { } board ? (HomeZPhase)board.ZPhase : HomeZPhase.Keep);
        set => Edit(value);
    }

    [Category("Homing"), DisplayName("Interlock mode")]
    public HomeInterlock InterlockMode
    {
        get => Choice(Original is { } board ? (HomeInterlock)board.InterlockMode : HomeInterlock.Keep);
        set => Edit(value);
    }

    [Category("Homing"), DisplayName("Interlock distance (controller unit)")]
    public string InterlockDistance
    {
        get => Number(Original?.InterlockDistance);
        set => Edit(value);
    }

    [Category("Homing"), DisplayName("Offset (controller unit)")]
    public string Offset
    {
        get => Number(Original?.Offset);
        set => Edit(value);
    }

    [Category("Homing"), DisplayName("Clear time (ms)")]
    public string ClearTime
    {
        get => Number(Original?.ClearTime);
        set => Edit(value);
    }

    [Category("Homing profile"), DisplayName("Speed 1 (controller unit/s)")]
    public string FirstSpeed
    {
        get => Number(Original?.FirstSpeed);
        set => Edit(value);
    }

    [Category("Homing profile"), DisplayName("Speed 2 (controller unit/s)")]
    public string SecondSpeed
    {
        get => Number(Original?.SecondSpeed);
        set => Edit(value);
    }

    [Category("Homing profile"), DisplayName("Speed 3 (controller unit/s)")]
    public string ThirdSpeed
    {
        get => Number(Original?.ThirdSpeed);
        set => Edit(value);
    }

    [Category("Homing profile"), DisplayName("Final speed (controller unit/s)")]
    public string LastSpeed
    {
        get => Number(Original?.LastSpeed);
        set => Edit(value);
    }

    [Category("Homing profile"), DisplayName("Acceleration 1 (controller setting)")]
    public string FirstAccel
    {
        get => Number(Original?.FirstAccel);
        set => Edit(value);
    }

    [Category("Homing profile"), DisplayName("Acceleration 2 (controller setting)")]
    public string SecondAccel
    {
        get => Number(Original?.SecondAccel);
        set => Edit(value);
    }
}
