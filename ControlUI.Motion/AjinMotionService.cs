using System;
using System.IO;
using System.Runtime.InteropServices;
using ControlUI.Support.Services;

namespace ControlUI.Motion;

public sealed class AjinMotionService(MotionPreferences configuration) : IMotionService
{
    public MotionPreferences Configuration { get; private set; } = configuration;
    public event EventHandler<EventArgs>? ConfigurationChanged;

    public void ApplyConfiguration(MotionPreferences configuration, Action save)
    {
        _ = configuration.GetHomeAxes();
        _ = configuration.GetMotPath();
        bool disconnected = false;
        try
        {
            lock (_motionLock)
            {
                EnsureManualAvailable();
                if (_initialized)
                {
                    EnsureAxesStopped();
                    disconnected = true;
                    CloseSession();
                }
                save();
                Configuration = configuration;
            }
        }
        catch
        {
            if (disconnected)
                ConfigurationChanged?.Invoke(this, EventArgs.Empty);
            throw;
        }
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }
    private bool _initialized;
    private readonly object _motionLock = new();
    public bool IsReady { get; private set; }
    public bool IsOperationActive { get; private set; }
    private volatile bool _operationStopRequested;
    public bool IsOperationStopRequested => _operationStopRequested;

    private bool _shuttingDown;

    public void StopForShutdown()
    {
        lock (_motionLock)
        {
            _shuttingDown = true;
            _operationStopRequested = true;
            if (!_initialized)
                return;

            int[] axes = Enumerable.Range(0, GetAxisCount()).ToArray();
            if (axes.Length > 0)
                StopAxes(axes, abrupt: false);

            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (axes.Any(IsAxisMoving))
            {
                if (timeout.Elapsed >= TimeSpan.FromSeconds(10))
                    throw new TimeoutException("Motion did not stop within ten seconds.");
                System.Threading.Thread.Sleep(50);
            }
        }
    }

    private static bool IsAxisMoving(int axis)
    {
        uint moving = 0;
        Check(CAXM.AxmStatusReadInMotion(axis, ref moving), nameof(CAXM.AxmStatusReadInMotion), axis);
        return moving != 0;
    }

    public void BeginOperation()
    {
        lock (_motionLock)
        {
            if (_shuttingDown || !IsReady || IsOperationActive)
                throw new InvalidOperationException("Motion is unavailable.");
            EnsureAxesStopped();
            _operationStopRequested = false;
            IsOperationActive = true;
        }
    }

    public void EndOperation()
    {
        lock (_motionLock)
            IsOperationActive = false;
    }

    public static void VerifyNativeLibrary()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "AXL.dll");
        nint library = NativeLibrary.Load(path);

        try
        {
            _ = NativeLibrary.GetExport(library, "AxlOpen");
            _ = NativeLibrary.GetExport(library, "AxmInfoGetAxisCount");
            _ = NativeLibrary.GetExport(library, "AxlClose");
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    public void Initialize()
    {
        lock (_motionLock)
        {
            if (_shuttingDown)
                throw new InvalidOperationException("Motion is shutting down.");
            InitializeCore();
        }
    }

    private void InitializeCore()
    {
        if (_initialized)
            return;

        uint result = CAXL.AxlOpen(7);

        if (result != 0)
        {
            var failure = CreateAxlError(result, nameof(CAXL.AxlOpen));
            try
            {
                CloseSession();
            }
            catch (Exception recoveryError)
            {
                throw new InvalidOperationException($"{failure.Message} Recovery failed: {recoveryError.Message}", failure);
            }
            throw failure;
        }

        _initialized = true;
    }

    public void LoadParameters(string path)
    {
        lock (_motionLock)
        {
            EnsureManualAvailable();
            _ = Configuration.GetHomeAxes();
            foreach (var axis in Configuration.Axes)
                Check(CAXM.AxmInfoIsInvalidAxisNo(axis.Number), nameof(CAXM.AxmInfoIsInvalidAxisNo), axis.Number);
            if (IsReady)
                EnsureAxesStopped();
            IsReady = false;
            LoadParametersCore(path);
            IsReady = true;
        }
    }

    private void LoadParametersCore(string path)
    {
        if (!_initialized)
            throw new InvalidOperationException("The motion library is not initialized.");

        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Motion parameter file not found.", fullPath);

        uint result = CAXM.AxmMotLoadParaAll(fullPath);

        Check(result, nameof(CAXM.AxmMotLoadParaAll));
    }

    public void SaveParameters(string path)
    {
        if (!_initialized)
            throw new InvalidOperationException("The motion library is not initialized.");

        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        uint result = CAXM.AxmMotSaveParaAll(fullPath);

        Check(result, nameof(CAXM.AxmMotSaveParaAll));
    }

    public int GetAxisCount()
    {
        if (!_initialized)
            throw new InvalidOperationException("The motion library is not initialized.");

        int axisCount = 0;
        uint result = CAXM.AxmInfoGetAxisCount(ref axisCount);

        Check(result, nameof(CAXM.AxmInfoGetAxisCount));

        return axisCount;
    }

    public MotionAxisSettings GetAxisSettings(int axisNumber)
    {
        if (!_initialized)
            throw new InvalidOperationException("The motion library is not initialized.");

        double unit = 0;
        int pulses = 0;
        double maxVelocity = 0;
        uint accelUnit = 0;

        Check(CAXM.AxmMotGetMoveUnitPerPulse(axisNumber, ref unit, ref pulses),
            nameof(CAXM.AxmMotGetMoveUnitPerPulse), axisNumber);
        Check(CAXM.AxmMotGetMaxVel(axisNumber, ref maxVelocity),
            nameof(CAXM.AxmMotGetMaxVel), axisNumber);
        Check(CAXM.AxmMotGetAccelUnit(axisNumber, ref accelUnit),
            nameof(CAXM.AxmMotGetAccelUnit), axisNumber);

        return new MotionAxisSettings(unit, pulses, maxVelocity, accelUnit);

    }

    public MotionBoardReadback ReadMotionBoard(int axisNumber)
    {
        lock (_motionLock)
        {
            var units = GetAxisSettings(axisNumber);
            uint profile = 0;
            double minimum = 0;
            Check(CAXM.AxmMotGetProfileMode(axisNumber, ref profile), nameof(CAXM.AxmMotGetProfileMode), axisNumber);
            Check(CAXM.AxmMotGetMinVel(axisNumber, ref minimum), nameof(CAXM.AxmMotGetMinVel), axisNumber);
            return new(units, profile, minimum, ReadSoftLimits(axisNumber));
        }
    }

    private static MotionSoftLimits ReadSoftLimits(int axis)
    {
        uint enabled = 0, stopMode = 0, source = 0;
        double positive = 0, negative = 0;
        Check(CAXM.AxmSignalGetSoftLimit(axis, ref enabled, ref stopMode, ref source, ref positive, ref negative),
            nameof(CAXM.AxmSignalGetSoftLimit), axis);
        return new(enabled == 1, source, negative, positive);
    }

    private delegate uint ReadFlag(int axis, ref uint value);
    private delegate uint ReadNumber(int axis, ref double value);

    public MotionAxisState ReadAxis(int axisNumber)
    {
        lock (_motionLock)
        {
            EnsureInitialized();
            uint positive = 0, negative = 0, home = 0;
            Check(CAXM.AxmSignalReadLimit(axisNumber, ref positive, ref negative), nameof(CAXM.AxmSignalReadLimit));
            Check(CAXM.AxmHomeGetResult(axisNumber, ref home), nameof(CAXM.AxmHomeGetResult));
            bool alarm = Flag(CAXM.AxmSignalReadServoAlarm);
            ushort code = 0;
            ushort? alarmCode = alarm && CAXM.AxmSignalReadServoAlarmCode(axisNumber, ref code) == 0
                ? code : null;
            return new(
                Number(CAXM.AxmStatusGetCmdPos), Number(CAXM.AxmStatusGetActPos), Number(CAXM.AxmStatusReadActVel),
                Flag(CAXM.AxmSignalIsServoOn), alarm,
                Flag(CAXM.AxmStatusReadInMotion), Flag(CAXM.AxmSignalReadInpos),
                Flag(CAXM.AxmHomeReadSignal), home == 1, positive != 0, negative != 0,
                Flag(CAXM.AxmSignalReadStop)) { HomeResult = home, AlarmCode = alarmCode };
        }

        bool Flag(ReadFlag read)
        {
            uint value = 0;
            Check(read(axisNumber, ref value), read.Method.Name);
            return value != 0;
        }

        double Number(ReadNumber read)
        {
            double value = 0;
            Check(read(axisNumber, ref value), read.Method.Name);
            return value;
        }
    }

    public void MoveAxis(int axisNumber, double millimetersPerPulse, double maxSpeed, double target,
        bool relative, double speed, double accelMmPerSecondSquared, double decelMmPerSecondSquared)
    {
        lock (_motionLock)
        {
            EnsureManualAvailable();
            EnsureReady();
            StartAxis(axisNumber, millimetersPerPulse, maxSpeed, speed, accelMmPerSecondSquared, decelMmPerSecondSquared, target, relative);
        }
    }

    public void JogAxis(int axisNumber, double millimetersPerPulse, double maxSpeed, double signedSpeed,
        double accelMmPerSecondSquared, double decelMmPerSecondSquared)
    {
        lock (_motionLock)
        {
            EnsureManualAvailable();
            EnsureReady();
            StartAxis(axisNumber, millimetersPerPulse, maxSpeed, signedSpeed, accelMmPerSecondSquared, decelMmPerSecondSquared, null, false);
        }
    }

    private void StartAxis(int axis, double millimetersPerPulse, double maxSpeed, double speed,
        double accel, double decel, double? target, bool relative)
    {
        var move = PrepareAxis(axis, millimetersPerPulse, maxSpeed, speed, accel, decel, target, relative);
        if (move.AtTarget)
            return;
        if (target.HasValue)
        {
            Check(CAXM.AxmMotSetAbsRelMode(axis, relative ? 1u : 0u), nameof(CAXM.AxmMotSetAbsRelMode));
            Check(CAXM.AxmMoveStartPos(axis, move.Position, move.Speed, move.Accel, move.Decel), nameof(CAXM.AxmMoveStartPos));
        }
        else
        {
            Check(CAXM.AxmMoveVel(axis, Math.CopySign(move.Speed, speed), move.Accel, move.Decel), nameof(CAXM.AxmMoveVel));
        }
    }

    private (double Position, double Speed, double Accel, double Decel, bool AtTarget) PrepareAxis(
        int axis, double millimetersPerPulse, double maxSpeed, double speed,
        double accel, double decel, double? target, bool relative)
    {
        var settings = GetAxisSettings(axis);
        double scale = millimetersPerPulse * settings.Pulses / settings.MoveUnit;
        if (!double.IsFinite(scale) || scale <= 0 || !double.IsFinite(speed) ||
            speed == 0 || Math.Abs(speed) > Math.Min(maxSpeed, settings.MaxVelocity * scale) ||
            !double.IsFinite(accel) || accel <= 0 || !double.IsFinite(decel) || decel <= 0 ||
            settings.AccelUnitCode > 1 || (target.HasValue && !double.IsFinite(target.Value)))
            throw new InvalidOperationException("Check the axis scaling, speed and acceleration settings.");

        var state = ReadAxis(axis);
        if (!state.ServoOn || state.Alarm || state.Emergency || state.Moving || !state.Homed)
            throw new InvalidOperationException("Axis must be stopped, homed, servo on and clear of faults.");

        var (enabled, source, negative, positive) = ReadSoftLimits(axis);
        if (!enabled || !double.IsFinite(positive) || !double.IsFinite(negative) || negative >= positive)
            throw new InvalidOperationException("Configure and enable valid axis travel limits before moving.");

        double current = source == 0 ? state.CommandPosition : state.ActualPosition;
        double endpoint = target.HasValue
            ? (relative ? state.CommandPosition : 0) + target.Value / scale
            : speed > 0 ? positive : negative;
        double direction = target.HasValue ? endpoint - state.CommandPosition : speed;
        if (!double.IsFinite(endpoint) || endpoint < negative || endpoint > positive ||
            (direction > 0 && (state.PositiveLimit || current >= positive)) ||
            (direction < 0 && (state.NegativeLimit || current <= negative)))
            throw new InvalidOperationException("The requested move exceeds an axis travel limit.");
        double nativeSpeed = Math.Abs(speed) / scale;
        if (settings.AccelUnitCode == 0)
        {
            accel /= scale;
            decel /= scale;
        }
        else
        {
            accel = Math.Abs(speed) / accel;
            decel = Math.Abs(speed) / decel;
        }

        if (!double.IsFinite(nativeSpeed) || !double.IsFinite(accel) || !double.IsFinite(decel) ||
            nativeSpeed <= 0 || accel <= 0 || decel <= 0)
            throw new InvalidOperationException("The requested motion exceeds the SDK's numeric range.");

        return ((target ?? 0) / scale, nativeSpeed, accel, decel, target.HasValue && direction == 0);
    }

    public void MoveOperationAxes(MotionMove[] moves)
    {
        lock (_motionLock)
        {
            EnsureOperationCanMove();
            if (moves.Any(move => !double.IsFinite(move.Speed) || move.Speed <= 0))
                throw new InvalidOperationException("Operation must own motion and each axis speed must be positive.");
            var prepared = moves.Select(move => (move.Axis, Values: PrepareAxis(move.Axis,
                move.MillimetersPerPulse, move.MaxSpeed, move.Speed, move.AccelMmPerSecondSquared, move.DecelMmPerSecondSquared,
                move.Position, false))).Where(move => !move.Values.AtTarget).ToArray();
            if (prepared.Length == 0)
                return;
            foreach (var move in prepared)
                Check(CAXM.AxmMotSetAbsRelMode(move.Axis, 0), nameof(CAXM.AxmMotSetAbsRelMode), move.Axis);
            if (prepared.Length == 1)
            {
                var move = prepared[0];
                Check(CAXM.AxmMoveStartPos(move.Axis, move.Values.Position, move.Values.Speed,
                    move.Values.Accel, move.Values.Decel), nameof(CAXM.AxmMoveStartPos), move.Axis);
                return;
            }
            Check(CAXM.AxmMoveStartMultiPos(prepared.Length, prepared.Select(x => x.Axis).ToArray(),
                prepared.Select(x => x.Values.Position).ToArray(), prepared.Select(x => x.Values.Speed).ToArray(),
                prepared.Select(x => x.Values.Accel).ToArray(), prepared.Select(x => x.Values.Decel).ToArray()),
                nameof(CAXM.AxmMoveStartMultiPos));
        }
    }

    public void HomeAxis(int axisNumber)
    {
        lock (_motionLock)
        {
            EnsureOperationCanMove();
            var state = ReadAxis(axisNumber);
            if (!state.ServoOn || state.Alarm || state.Emergency || state.Moving)
                throw new InvalidOperationException("Homing requires a stopped axis with servo on and no fault.");
            Check(CAXM.AxmHomeSetStart(axisNumber), nameof(CAXM.AxmHomeSetStart), axisNumber);
        }
    }

    public void ResetAxisAlarm(int axisNumber)
    {
        lock (_motionLock)
        {
            EnsureOperationCanMove();
            var state = ReadAxis(axisNumber);
            if (state.Moving || state.Emergency)
                throw new InvalidOperationException("Alarm reset requires a stopped axis and a clear emergency input.");
            if (!state.Alarm)
                return;
            uint result = 0;
            try
            {
                Check(CAXM.AxmSignalServoAlarmReset(axisNumber, 1), nameof(CAXM.AxmSignalServoAlarmReset), axisNumber);
                System.Threading.Thread.Sleep(100);
            }
            finally
            {
                result = CAXM.AxmSignalServoAlarmReset(axisNumber, 0);
            }
            Check(result, nameof(CAXM.AxmSignalServoAlarmReset), axisNumber);
        }
    }

    public void SetServoEnabled(int axisNumber, bool enabled)
    {
        lock (_motionLock)
        {
            EnsureOperationCanMove();
            var state = ReadAxis(axisNumber);
            if (state.Moving || (enabled && (state.Emergency || state.Alarm)))
                throw new InvalidOperationException("Stop the axis and clear faults before enabling its servo.");
            Check(CAXM.AxmSignalServoOn(axisNumber, enabled ? 1u : 0u), nameof(CAXM.AxmSignalServoOn), axisNumber);
        }
    }

    public MotionAxisIoSettings ReadAxisIoSettings(int axisNumber)
    {
        lock (_motionLock)
        {
            EnsureInitialized();
            uint positive = 0, negative = 0, limitStop = 0, emergencyStop = 0, emergency = 0;
            uint level = 0, alarm = 0, inPosition = 0, homeSignal = 0, zPhase = 0;
            int direction = 0;
            double clear = 0, offset = 0, first = 0, second = 0, third = 0, last = 0, accel1 = 0, accel2 = 0;
            Check(CAXM.AxmSignalGetLimit(axisNumber, ref limitStop, ref positive, ref negative), nameof(CAXM.AxmSignalGetLimit), axisNumber);
            Check(CAXM.AxmSignalGetStop(axisNumber, ref emergencyStop, ref emergency), nameof(CAXM.AxmSignalGetStop), axisNumber);
            Check(CAXM.AxmHomeGetSignalLevel(axisNumber, ref level), nameof(CAXM.AxmHomeGetSignalLevel), axisNumber);
            Check(CAXM.AxmSignalGetServoAlarm(axisNumber, ref alarm), nameof(CAXM.AxmSignalGetServoAlarm), axisNumber);
            Check(CAXM.AxmSignalGetInpos(axisNumber, ref inPosition), nameof(CAXM.AxmSignalGetInpos), axisNumber);
            Check(CAXM.AxmHomeGetMethod(axisNumber, ref direction, ref homeSignal, ref zPhase, ref clear, ref offset),
                nameof(CAXM.AxmHomeGetMethod), axisNumber);
            Check(CAXM.AxmHomeGetVel(axisNumber, ref first, ref second, ref third, ref last, ref accel1, ref accel2),
                nameof(CAXM.AxmHomeGetVel), axisNumber);
            uint interlockMode = 0;
            double interlockDistance = 0;
            Check(CAXM.AxmHomeGetInterlock(axisNumber, ref interlockMode, ref interlockDistance),
                nameof(CAXM.AxmHomeGetInterlock), axisNumber);
            return new(positive, negative, limitStop, level, alarm, inPosition, emergency, emergencyStop,
                direction, homeSignal, zPhase, first, second, third, last, accel1, accel2, offset, clear)
                { InterlockMode = interlockMode, InterlockDistance = interlockDistance };
        }
    }

    public void ApplyAxisIoSettings(int axis, MotionAxisIoSettings original, MotionAxisIoSettings settings)
    {
        settings.Validate();
        lock (_motionLock)
        {
            EnsureManualAvailable();
            EnsureReady();
            EnsureAxesStopped();
            if (ReadAxis(axis).ServoOn)
                throw new InvalidOperationException("Turn off the selected axis servo before applying I/O and homing settings.");
            if (ReadAxisIoSettings(axis) != original)
                throw new InvalidOperationException("Controller settings changed. Read Board again before applying.");
            if (settings == original)
                return;

            var undo = new Stack<Action>();
            try
            {
                Write((original.LimitStop, original.PositiveLimit, original.NegativeLimit),
                    (settings.LimitStop, settings.PositiveLimit, settings.NegativeLimit),
                    x => CAXM.AxmSignalSetLimit(axis, x.Item1, x.Item2, x.Item3), nameof(CAXM.AxmSignalSetLimit));
                Write(original.HomeLevel, settings.HomeLevel, x => CAXM.AxmHomeSetSignalLevel(axis, x), nameof(CAXM.AxmHomeSetSignalLevel));
                Write(original.AlarmLevel, settings.AlarmLevel, x => CAXM.AxmSignalSetServoAlarm(axis, x), nameof(CAXM.AxmSignalSetServoAlarm));
                Write(original.InPositionLevel, settings.InPositionLevel, x => CAXM.AxmSignalSetInpos(axis, x), nameof(CAXM.AxmSignalSetInpos));
                Write((original.EmergencyStop, original.EmergencyLevel), (settings.EmergencyStop, settings.EmergencyLevel),
                    x => CAXM.AxmSignalSetStop(axis, x.Item1, x.Item2), nameof(CAXM.AxmSignalSetStop));
                Write((original.HomeDirection, original.HomeSignal, original.ZPhase, original.ClearTime, original.Offset),
                    (settings.HomeDirection, settings.HomeSignal, settings.ZPhase, settings.ClearTime, settings.Offset),
                    x => CAXM.AxmHomeSetMethod(axis, x.Item1, x.Item2, x.Item3, x.Item4, x.Item5), nameof(CAXM.AxmHomeSetMethod));
                Write((original.FirstSpeed, original.SecondSpeed, original.ThirdSpeed, original.LastSpeed, original.FirstAccel, original.SecondAccel),
                    (settings.FirstSpeed, settings.SecondSpeed, settings.ThirdSpeed, settings.LastSpeed, settings.FirstAccel, settings.SecondAccel),
                    x => CAXM.AxmHomeSetVel(axis, x.Item1, x.Item2, x.Item3, x.Item4, x.Item5, x.Item6), nameof(CAXM.AxmHomeSetVel));
                Write((original.InterlockMode, original.InterlockDistance), (settings.InterlockMode, settings.InterlockDistance),
                    x => CAXM.AxmHomeSetInterlock(axis, x.Item1, x.Item2), nameof(CAXM.AxmHomeSetInterlock));
                Check(CAXM.AxmHomeSetResult(axis, 0xFF), nameof(CAXM.AxmHomeSetResult), axis);
            }
            catch (Exception error)
            {
                var recoveryErrors = new List<Exception>();
                while (undo.TryPop(out var restore))
                {
                    try { restore(); }
                    catch (Exception recoveryError) { recoveryErrors.Add(recoveryError); }
                }
                if (recoveryErrors.Count != 0)
                {
                    IsReady = false;
                    throw new InvalidOperationException(
                        $"{error.Message} Rollback failed. Load a verified MOT file before moving. " +
                        string.Join(" ", recoveryErrors.Select(failure => failure.Message)), error);
                }
                throw;
            }

            void Write<T>(T before, T after, Func<T, uint> set, string function)
            {
                if (EqualityComparer<T>.Default.Equals(before, after))
                    return;
                undo.Push(() => Check(set(before), function, axis));
                Check(set(after), function, axis);
            }
        }
    }

    private void EnsureManualAvailable()
    {
        if (_shuttingDown || IsOperationActive)
            throw new InvalidOperationException("Manual motion is unavailable.");
    }

    private void EnsureOperationCanMove()
    {
        if (!IsOperationActive || IsOperationStopRequested)
            throw new InvalidOperationException("Operation motion is stopped or unavailable.");
    }

    private void EnsureReady()
    {
        if (!IsReady)
            throw new InvalidOperationException("Load valid motion parameters before moving.");
    }

    private void EnsureAxesStopped()
    {
        for (int axis = 0; axis < GetAxisCount(); axis++)
            if (IsAxisMoving(axis))
                throw new InvalidOperationException($"Axis {axis} is moving.");
    }

    public void StopAxis(int axisNumber, bool abrupt)
    {
        lock (_motionLock)
        {
            EnsureInitialized();
            if (IsOperationActive)
                _operationStopRequested = true;
            Check(abrupt ? CAXM.AxmMoveEStop(axisNumber) : CAXM.AxmMoveSStop(axisNumber),
                abrupt ? nameof(CAXM.AxmMoveEStop) : nameof(CAXM.AxmMoveSStop));
        }
    }

    public void StopAxes(int[] axisNumbers, bool abrupt) => StopAxes(axisNumbers, abrupt, pause: false);

    public void PauseOperationAxes(int[] axisNumbers) => StopAxes(axisNumbers, abrupt: false, pause: true);

    private void StopAxes(int[] axisNumbers, bool abrupt, bool pause)
    {
        lock (_motionLock)
        {
            EnsureInitialized();
            if (pause)
                EnsureOperationCanMove();
            else if (IsOperationActive)
                _operationStopRequested = true;
            uint result = abrupt
                ? CAXM.AxmMoveMultiEStop(axisNumbers.Length, axisNumbers)
                : CAXM.AxmMoveMultiSStop(axisNumbers.Length, axisNumbers);
            Check(result, abrupt ? nameof(CAXM.AxmMoveMultiEStop) : nameof(CAXM.AxmMoveMultiSStop));
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("The motion library is not initialized.");
    }

    private static void Check(uint result, string function, int? axisNumber = null)
    {
        if (result != 0)
            throw CreateAxlError(result, function, axisNumber);
    }

    private static InvalidOperationException CreateAxlError(uint result, string function, int? axisNumber = null)
    {
        string name = Enum.GetName((AXT_FUNC_RESULT)result) ?? "Unknown AXL error";
        string axis = axisNumber.HasValue ? $" for axis {axisNumber.Value}" : string.Empty;
        return new InvalidOperationException($"{function} failed{axis}. {name} ({result}).");
    }

    public void Dispose()
    {
        if (_initialized)
            CloseSession();
    }

    private void CloseSession()
    {
        _initialized = false;
        IsReady = false;
        if (CAXL.AxlIsOpened() != 0)
        {
            CAXL.AxlClose();
            if (CAXL.AxlIsOpened() != 0)
                throw new InvalidOperationException("AXL session could not be closed.");
        }
    }
}
