using ControlUI.Support.Services;

namespace ControlUI.Motion;

public sealed class SimulatedMotionService(TimeProvider clock, bool startWithAlarm = false,
    MotionPreferences? configuration = null) : IMotionService
{
    private const double TravelLimit = 1000;
    private const double FollowingDelaySeconds = 0.04;
    public MotionDispatch Dispatch { get; } = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, (long Time, bool Abrupt)> _stops = new();
    private readonly object _gate = new();
    private Dictionary<int, Axis> _axes = CreateAxes(configuration ?? new MotionPreferences());
    private static Dictionary<int, Axis> CreateAxes(MotionPreferences configuration) => configuration.Axes
        .ToDictionary(axis => axis.Number, axis => new Axis(axis.Number, axis.MillimetersPerPulse, axis.MaxSpeed));
    public MotionPreferences Configuration { get; private set; } = configuration ?? new MotionPreferences();
    public event EventHandler<EventArgs>? ConfigurationChanged;

    public void ApplyConfiguration(MotionPreferences configuration, Action save)
    {
        _ = configuration.GetHomeAxes();
        _ = configuration.GetMotPath();
        lock (_gate)
        {
            foreach (var axis in _axes.Values)
                Update(axis);
            if (_shuttingDown || _operationActive || _axes.Values.Any(axis => axis.Moving))
                throw new InvalidOperationException("Stop simulated motion before applying settings.");
            var axes = CreateAxes(configuration);
            save();
            _ready = _operationStopped = false;
            Configuration = configuration;
            _axes = axes;
        }
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }
    private volatile bool _ready;
    private volatile bool _operationActive;
    private volatile bool _operationStopped;

    public bool IsSimulation => true;
    private long _parameterRevision;
    public long ParameterRevision => Interlocked.Read(ref _parameterRevision);
    public bool IsReady => _ready;
    public bool IsOperationActive => _operationActive;
    public bool IsOperationStopRequested => _operationStopped;

    public void Initialize()
    {
        lock (_gate)
        {
            if (_shuttingDown)
                throw new InvalidOperationException("Motion is shutting down.");
            if (_ready)
                return;
            _axes.Values.First().Alarm = startWithAlarm;
            Interlocked.Increment(ref _parameterRevision);
            _ready = true;
        }
    }

    // Manual initialization applies the default .mot path. Simulation data stays in memory.
    public void LoadParameters(string path)
    {
        RequireReady();
        Interlocked.Increment(ref _parameterRevision);
    }

    public void SaveParameters(string path) =>
        throw new NotSupportedException("Simulation does not write equipment parameter files.");

    public int GetAxisCount()
    {
        RequireReady();
        return _axes.Count;
    }

    public MotionAxisSettings GetAxisSettings(int axisNumber)
    {
        lock (_gate)
        {
            RequireReady();
            var axis = _axes[axisNumber];
            return new(axis.MillimetersPerPulse, 1, axis.MaxSpeed, 1);
        }
    }

    public MotionBoardReadback ReadMotionBoard(int axisNumber)
    {
        lock (_gate)
            return new(GetAxisSettings(axisNumber), 0, 0, new(true, 1, -TravelLimit, TravelLimit));
    }

    public MotionAxisState ReadAxis(int axisNumber)
    {
        lock (_gate)
        {
            RequireReady();
            var axis = _axes[axisNumber];
            Update(axis);
            return new(axis.CommandPosition, axis.Position, axis.Velocity, axis.ServoOn, axis.Alarm, axis.Moving,
                !axis.Moving && Math.Abs(axis.CommandPosition - axis.Position) <= axis.MillimetersPerPulse, Math.Abs(axis.Position) <= axis.MillimetersPerPulse, axis.Homed,
                axis.Position >= TravelLimit, axis.Position <= -TravelLimit, false)
            {
                HomeResult = axis.Homing ? 2u : axis.Homed ? 1u : 0xFFu, AlarmCode = axis.Alarm ? (ushort)1 : null
            };
        }
    }

    private volatile bool _shuttingDown;
    private long _faultStopTime = long.MaxValue;

    public void StopForUnhandledException()
    {
        _shuttingDown = true;
        _operationStopped = true;
        Interlocked.CompareExchange(ref _faultStopTime, clock.GetTimestamp(), long.MaxValue);
    }

    public void StopForShutdown()
    {
        _shuttingDown = true;
        if (!_ready)
            return;

        int[] axes = _axes.Keys.ToArray();
        StopAxes(axes, abrupt: false);
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (axes.Any(number => ReadAxis(number).Moving))
        {
            if (timeout.Elapsed >= TimeSpan.FromSeconds(10))
                throw new TimeoutException("Simulated motion did not stop within ten seconds.");
            Thread.Sleep(50);
        }
    }

    public void BeginOperation()
    {
        lock (_gate)
        {
            RequireReady();
            foreach (var axis in _axes.Values)
                Update(axis);
            if (_shuttingDown || _operationActive || _axes.Values.Any(axis => axis.Moving))
                throw new InvalidOperationException("Simulated motion is already in use.");
            lock (Dispatch.Gate)
            {
                Dispatch.CheckStart();
                _operationStopped = false;
                _operationActive = true;
            }
        }
    }

    public void EndOperation()
    {
        lock (_gate)
            _operationActive = false;
    }

    public void HomeAxis(int axisNumber)
    {
        lock (_gate)
        {
            var axis = GetIdleAxis(axisNumber, operation: true);
            if (!axis.ServoOn)
                throw new InvalidOperationException("Enable the simulated servo before homing.");
            axis.Homed = false;
            Start(axis, axis.IoSettings.Offset, axis.IoSettings.FirstSpeed,
                axis.IoSettings.FirstAccel, axis.IoSettings.SecondAccel, minimumDuration: 0.3);
            axis.Homing = true;
        }
    }

    public void SimulateAlarm(int axisNumber)
    {
        lock (_gate)
        {
            RequireReady();
            var axis = _axes[axisNumber];
            Update(axis);
            axis.Alarm = true;
            if (axis.Moving)
                Stop(axis, true, clock.GetTimestamp());
        }
    }

    public void ResetAxisAlarm(int axisNumber)
    {
        lock (_gate)
            GetIdleAxis(axisNumber, operation: true, allowAlarm: true).Alarm = false;
    }

    public void SetServoEnabled(int axisNumber, bool enabled)
    {
        lock (_gate)
        {
            var axis = GetIdleAxis(axisNumber, operation: true, allowAlarm: !enabled);
            axis.ServoOn = enabled;
        }
    }

    public MotionAxisIoSettings ReadAxisIoSettings(int axisNumber)
    {
        lock (_gate)
        {
            RequireReady();
            return _axes[axisNumber].IoSettings;
        }
    }

    public void ApplyAxisIoSettings(int axisNumber, MotionAxisIoSettings original, MotionAxisIoSettings settings)
    {
        settings.Validate();
        lock (_gate)
        {
            foreach (var other in _axes.Values)
                Update(other);
            if (_axes.Values.Any(other => other.Moving))
                throw new InvalidOperationException("Stop all simulated axes before applying I/O and homing settings.");
            var axis = GetIdleAxis(axisNumber, operation: false, allowAlarm: true);
            if (axis.ServoOn || axis.IoSettings != original)
                throw new InvalidOperationException("Turn servo off and read the current axis settings before applying.");
            if (settings != original)
                axis.Homed = false;
            axis.IoSettings = settings;
        }
    }

    public void MoveAxis(int axisNumber, double millimetersPerPulse, double maxSpeed, double target,
        bool relative, double speed, double accelMmPerSecondSquared, double decelMmPerSecondSquared)
    {
        lock (_gate)
        {
            var axis = GetIdleAxis(axisNumber, operation: false);
            double destination = relative ? axis.CommandPosition + target : target;
            ValidateMove(axis, destination, speed, accelMmPerSecondSquared, decelMmPerSecondSquared);
            Start(axis, destination, speed, accelMmPerSecondSquared, decelMmPerSecondSquared);
        }
    }

    public void JogAxis(int axisNumber, double millimetersPerPulse, double maxSpeed, double signedSpeed,
        double accelMmPerSecondSquared, double decelMmPerSecondSquared)
    {
        lock (_gate)
        {
            var axis = GetIdleAxis(axisNumber, operation: false);
            double target = signedSpeed > 0 ? TravelLimit : -TravelLimit;
            double speed = Math.Abs(signedSpeed);
            ValidateMove(axis, target, speed, accelMmPerSecondSquared, decelMmPerSecondSquared);
            Start(axis, target, speed, accelMmPerSecondSquared, decelMmPerSecondSquared);
        }
    }

    public void MoveOperationAxes(MotionMove[] moves)
    {
        lock (_gate)
        {
            foreach (var move in moves)
                ValidateMove(GetIdleAxis(move.Axis, operation: true), move.Position, move.Speed,
                    move.AccelMmPerSecondSquared, move.DecelMmPerSecondSquared);
            long start = clock.GetTimestamp();
            foreach (var move in moves)
                Start(_axes[move.Axis], move.Position, move.Speed,
                    move.AccelMmPerSecondSquared, move.DecelMmPerSecondSquared, timestamp: start);
        }
    }

    public void StopAxis(int axisNumber, bool abrupt) => StopAxes([axisNumber], abrupt);

    public void StopAxes(int[] axisNumbers, bool abrupt) => StopAxes(axisNumbers, abrupt, pause: false);

    public void PauseOperationAxes(int[] axisNumbers) => StopAxes(axisNumbers, abrupt: false, pause: true);

    private void StopAxes(int[] axisNumbers, bool abrupt, bool pause)
    {
        lock (Dispatch.Gate)
        {
            RequireReady();
            if (pause && (!_operationActive || _operationStopped))
                throw new InvalidOperationException("No active operation can be paused.");
            Dispatch.Invalidate();
            if (!pause && _operationActive)
                _operationStopped = true;
            long time = clock.GetTimestamp();
            foreach (int number in axisNumbers)
            {
                _stops.AddOrUpdate(number, (time, abrupt), (_, previous) =>
                    abrupt && !previous.Abrupt ? (time, true) : previous);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _ready = _operationActive = _operationStopped = false;
            foreach (var axis in _axes.Values)
            {
                axis.Moving = axis.Homing = axis.Homed = false;
                axis.CommandPosition = axis.Position = axis.Velocity = 0;
                axis.Trajectory = null;
            }
        }
    }

    private Axis GetIdleAxis(int number, bool operation, bool allowAlarm = false)
    {
        RequireReady();
        var axis = _axes[number];
        Update(axis);
        if (_shuttingDown || operation != _operationActive || (operation && _operationStopped) ||
            axis.Moving || (!allowAlarm && axis.Alarm))
            throw new InvalidOperationException("Simulated axis is busy or has a fault.");
        return axis;
    }

    private static void ValidateMove(Axis axis, double target, double speed, double accel, double decel)
    {
        if (!axis.ServoOn || !axis.Homed || !double.IsFinite(target) || Math.Abs(target) > TravelLimit ||
            !double.IsFinite(speed) || speed <= 0 || speed > axis.MaxSpeed ||
            !double.IsFinite(accel) || accel <= 0 || !double.IsFinite(decel) || decel <= 0)
            throw new InvalidOperationException("Home the simulated axis and check its position, speed and acceleration.");
    }

    private void Start(Axis axis, double target, double speed, double accel, double decel,
        double minimumDuration = 0, long? timestamp = null)
    {
        Dispatch.CheckStart();
        double start = axis.CommandPosition;
        double distance = Math.Abs(target - start);
        double direction = Math.Sign(target - start);
        double peak = Math.Min(speed, Math.Sqrt(2 * distance / (1 / accel + 1 / decel)));
        double accelTime = peak / accel;
        double decelTime = peak / decel;
        double accelDistance = peak * accelTime / 2;
        double cruiseDistance = Math.Max(0, distance - accelDistance - peak * decelTime / 2);
        double cruiseTime = peak > 0 ? cruiseDistance / peak : 0;
        double decelStart = accelTime + cruiseTime;
        double travelTime = decelStart + decelTime;

        axis.Trajectory = time =>
        {
            if (time <= 0)
                return (start, 0);
            if (time >= travelTime)
                return (target, 0);
            if (time < accelTime)
                return (start + direction * accel * time * time / 2, direction * accel * time);
            if (time < decelStart)
                return (start + direction * (accelDistance + peak * (time - accelTime)), direction * peak);

            double remaining = travelTime - time;
            return (target - direction * decel * remaining * remaining / 2, direction * decel * remaining);
        };
        axis.Deceleration = decel;
        axis.Duration = Math.Max(travelTime, minimumDuration);
        axis.Timestamp = timestamp ?? clock.GetTimestamp();
        axis.Moving = axis.Duration > 0;
    }

    private void Stop(Axis axis, bool abrupt, long timestamp)
    {
        axis.Homing = false;
        var previous = axis.Trajectory!;
        double elapsed = clock.GetElapsedTime(axis.Timestamp, timestamp).TotalSeconds;
        var (position, velocity) = previous(elapsed);
        double duration = abrupt ? 0 : Math.Abs(velocity) / axis.Deceleration;
        double signedDecel = Math.Sign(velocity) * axis.Deceleration;
        double target = position + velocity * duration / 2;
        axis.Trajectory = time =>
        {
            if (time < 0)
                return previous(elapsed + time);
            if (time >= duration)
                return (target, 0);
            return (position + velocity * time - signedDecel * time * time / 2,
                velocity - signedDecel * time);
        };
        axis.Timestamp = timestamp;
        axis.Duration = duration;
        Update(axis);
    }

    private void Update(Axis axis)
    {
        if (_stops.TryRemove(axis.Number, out var stop) && axis.Moving)
        {
            Stop(axis, stop.Abrupt, Math.Max(stop.Time, axis.Timestamp));
            return;
        }
        if (!axis.Moving || axis.Trajectory is not { } sample)
            return;

        long stopTime = Interlocked.Read(ref _faultStopTime);
        if (stopTime != long.MaxValue)
        {
            double stoppedAt = Math.Max(0, clock.GetElapsedTime(axis.Timestamp, stopTime).TotalSeconds);
            (axis.CommandPosition, _) = sample(stoppedAt);
            axis.Position = axis.CommandPosition;
            axis.Velocity = 0;
            axis.Moving = axis.Homing = false;
            axis.Trajectory = null;
            return;
        }

        double elapsed = clock.GetElapsedTime(axis.Timestamp).TotalSeconds;
        (axis.CommandPosition, _) = sample(elapsed);
        (axis.Position, axis.Velocity) = sample(elapsed - FollowingDelaySeconds);
        if (elapsed < axis.Duration + FollowingDelaySeconds)
            return;

        axis.Moving = false;
        axis.Homed |= axis.Homing;
        axis.Homing = false;
        axis.Trajectory = null;
    }

    private void RequireReady()
    {
        if (!_ready)
            throw new InvalidOperationException("Initialize simulated motion first.");
    }

    private sealed class Axis(int number, double millimetersPerPulse, double maxSpeed)
    {
        public int Number { get; } = number;
        public double MillimetersPerPulse { get; } = millimetersPerPulse;
        public double MaxSpeed { get; } = maxSpeed;
        public bool Alarm, Moving, Homed, Homing;
        public bool ServoOn = true;
        public MotionAxisIoSettings IoSettings { get; set; } =
            new(1, 1, 1, 1, 1, 1, 1, 0, 0, 4, 0, 50, 25, 10, 2, 0.1, 0.1, 0, 0);
        public double CommandPosition, Position, Velocity, Duration, Deceleration;
        public Func<double, (double Position, double Velocity)>? Trajectory;
        public long Timestamp;
    }
}
