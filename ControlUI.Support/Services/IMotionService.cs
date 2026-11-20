using System;

namespace ControlUI.Support.Services;

public interface IMotionService : IDisposable
{
    bool IsSimulation => false;
    MotionPreferences Configuration { get; }
    event EventHandler<EventArgs>? ConfigurationChanged;
    void ApplyConfiguration(MotionPreferences configuration, Action save);
    bool IsReady { get; }
    bool IsOperationActive { get; }
    bool IsOperationStopRequested { get; }
    void BeginOperation();
    void EndOperation();
    void Initialize();
    
    void LoadParameters(string path);

    void SaveParameters(string path);

    int GetAxisCount();
    MotionAxisSettings GetAxisSettings(int axisNumber);
    MotionBoardReadback ReadMotionBoard(int axisNumber);

    MotionAxisState ReadAxis(int axisNumber);
    void MoveAxis(int axisNumber, double millimetersPerPulse, double maxSpeed, double target,
        bool relative, double speed, double accelMmPerSecondSquared, double decelMmPerSecondSquared);
    void JogAxis(int axisNumber, double millimetersPerPulse, double maxSpeed, double signedSpeed,
        double accelMmPerSecondSquared, double decelMmPerSecondSquared);
    void StopAxis(int axisNumber, bool abrupt);
    void StopAxes(int[] axisNumbers, bool abrupt);
    void MoveOperationAxes(MotionMove[] moves);
    void PauseOperationAxes(int[] axisNumbers);
    void HomeAxis(int axisNumber);
    void ResetAxisAlarm(int axisNumber);
    void SetServoEnabled(int axisNumber, bool enabled);
    MotionAxisIoSettings ReadAxisIoSettings(int axisNumber);
    void ApplyAxisIoSettings(int axisNumber, MotionAxisIoSettings original, MotionAxisIoSettings settings);
}

public sealed record MotionAxisIoSettings(uint PositiveLimit, uint NegativeLimit, uint LimitStop,
    uint HomeLevel, uint AlarmLevel, uint InPositionLevel, uint EmergencyLevel, uint EmergencyStop,
    int HomeDirection, uint HomeSignal, uint ZPhase, double FirstSpeed, double SecondSpeed,
    double ThirdSpeed, double LastSpeed, double FirstAccel, double SecondAccel, double Offset, double ClearTime)
{
    public uint InterlockMode { get; init; }
    public double InterlockDistance { get; init; }

    public void Validate()
    {
        if (PositiveLimit > 5 || NegativeLimit > 5 || LimitStop > 1 || HomeLevel > 1 ||
            AlarmLevel > 3 || InPositionLevel > 3 || EmergencyLevel > 3 || EmergencyStop > 1 ||
            HomeDirection is < 0 or > 1 || HomeSignal is not (<= 9 or 16 or >= 100 and <= 137 or 255) ||
            ZPhase > 2 || InterlockMode > 2)
            throw new ArgumentException("One or more I/O or homing choices are invalid.");
        double[] positive = [FirstSpeed, SecondSpeed, ThirdSpeed, LastSpeed, FirstAccel, SecondAccel];
        if (positive.Any(value => !double.IsFinite(value) || value <= 0) || !double.IsFinite(Offset) ||
            !double.IsFinite(ClearTime) || ClearTime < 0 || !double.IsFinite(InterlockDistance) ||
            InterlockDistance < 0 || (InterlockMode == 2 && InterlockDistance == 0))
            throw new ArgumentException("Check homing speeds, acceleration, offset, clear time and interlock distance.");
    }
}

public sealed record MotionMove(int Axis, double MillimetersPerPulse, double MaxSpeed,
    double Position, double Speed, double AccelMmPerSecondSquared, double DecelMmPerSecondSquared);

public sealed record MotionAxisState(
    double CommandPosition, double ActualPosition, double Velocity,
    bool ServoOn, bool Alarm, bool Moving, bool InPosition, bool HomeSignal,
    bool Homed, bool PositiveLimit, bool NegativeLimit, bool Emergency)
{
    public uint HomeResult { get; init; }
    public ushort? AlarmCode { get; init; }
}
