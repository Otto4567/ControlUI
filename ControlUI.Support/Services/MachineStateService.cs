using System.Runtime.CompilerServices;

namespace ControlUI.Support.Services;

public sealed record AxisSnapshot(ProgramAxis Axis, MotionAxisState? State, MotionAxisSettings? Settings,
    double? MillimetersPerUnit, string? ReadError)
{
    public double? CommandMm => State?.CommandPosition * MillimetersPerUnit;
    public double? ActualMm => State?.ActualPosition * MillimetersPerUnit;
    public double? SpeedMmPerSecond => State?.Velocity * MillimetersPerUnit;
    public bool Fault => ReadError != null || State is { Alarm: true } or { Emergency: true };
    public string? NotReadyReason => State switch
    {
        null => ReadError ?? "not connected",
        { Emergency: true } => "emergency input active",
        { Alarm: true } => "alarm active",
        { ServoOn: false } => "servo OFF",
        { Moving: true } => "still moving",
        { Homed: false } => "home required",
        _ => null
    };
}

public sealed record MachineSnapshot(MotionPreferences Configuration, long Revision, bool Initialized,
    IReadOnlyList<AxisSnapshot> Axes)
{
    public bool Online => Initialized && Axes.Count > 0 && Axes.All(axis => axis.State != null);
    public bool Ready => Online && Axes.All(axis => axis.NotReadyReason == null);
    public bool Fault => Axes.Any(axis => axis.Fault);
}

public sealed class MachineStateService : IDisposable
{
    private readonly IMotionService _motion;
    private readonly Timer _timer;
    private readonly EventLogService _log;
    private readonly object _gate = new();
    private Func<MachineSnapshot, Task>? _observers;
    // Only Sample updates failure counts. Unsubscribing releases the observer and its history.
    private readonly ConditionalWeakTable<Func<MachineSnapshot, Task>, Dictionary<string, FailureWindow>> _failures = new();
    private sealed class FailureWindow
    {
        public long ReportedAt = TimeProvider.System.GetTimestamp();
        public int Repeats;
    }

    private Task<MachineSnapshot>? _reading;
    // Only the single sampling task accesses this cache.
    private readonly Dictionary<int, (MotionAxisSettings Settings, double Scale)> _units = [];
    private MotionPreferences? _configuration;
    private long _revision;
    private bool _disposed;

    public MachineStateService(IMotionService motion, EventLogService log)
    {
        _motion = motion;
        _log = log;
        _timer = new Timer(state =>
        {
            lock (_gate)
                if (_observers != null && !_disposed && _reading is not { IsCompleted: false })
                    _ = PollAsync();
        });
    }

    public event Func<MachineSnapshot, Task> Updated
    {
        add
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_observers == null) _timer.Change(0, 250);
                _observers += value;
            }
        }
        remove
        {
            lock (_gate)
            {
                _observers -= value;
                if (_observers == null && !_disposed) _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
    }

    public bool IsCurrent(MachineSnapshot snapshot) =>
        ReferenceEquals(snapshot.Configuration, _motion.Configuration) &&
        snapshot.Revision == _motion.ParameterRevision && snapshot.Initialized == _motion.IsReady;

    public Task<MachineSnapshot> RefreshAsync()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_reading is not { IsCompleted: false })
                _reading = Task.Run(Sample);
            return _reading;
        }
    }

    private async Task PollAsync()
    {
        try { await RefreshAsync().ConfigureAwait(false); }
        catch (Exception error) { await LogAsync("Warning", $"Sampling failed: {error}").ConfigureAwait(false); }
    }

    private async Task LogAsync(string level, string message)
    {
        try { await _log.WriteAsync(level, "MachineState", message).ConfigureAwait(false); }
        catch (Exception logError) { System.Diagnostics.Trace.TraceWarning("{0}; logging failed: {1}", message, logError); }
    }

    private async Task<MachineSnapshot> Sample()
    {
        MachineSnapshot snapshot;
        do
        {
            var config = _motion.Configuration;
            long revision = _motion.ParameterRevision;
            bool ready = _motion.IsReady;
            if (!ready || !ReferenceEquals(config, _configuration) || revision != _revision)
                _units.Clear();
            _configuration = config;
            _revision = revision;
            var axes = new List<AxisSnapshot>(config.Axes.Count);
            foreach (var axis in config.Axes)
                axes.Add(ready ? ReadAxis(axis) : new(axis, null, null, null, null));
            snapshot = new(config, revision, ready, axes.AsReadOnly());
        } while (!IsCurrent(snapshot));

        Func<MachineSnapshot, Task>? observers;
        lock (_gate) observers = _observers;
        if (observers != null)
            foreach (var observer in Delegate.EnumerateInvocationList(observers))
            {
                try { await observer(snapshot).ConfigureAwait(false); }
                catch (Exception error)
                {
                    await ReportObserverFailureAsync(observer, error).ConfigureAwait(false);
                    continue;
                }
                if (_failures.TryGetValue(observer, out var failures))
                {
                    _failures.Remove(observer);
                    await LogAsync("Info", $"Observer {observer.Method.Name} recovered. Pending repeats: {failures.Values.Sum(f => (long)f.Repeats)}.").ConfigureAwait(false);
                }
            }
        return snapshot;
    }

    private Task ReportObserverFailureAsync(Func<MachineSnapshot, Task> observer, Exception error)
    {
        var failures = _failures.GetOrCreateValue(observer);
        if (!failures.TryGetValue(error.Message, out var window))
        {
            failures.Add(error.Message, new FailureWindow());
            return LogAsync("Warning", $"Observer {observer.Method.Name} failed: {error}");
        }

        window.Repeats++;
        if (TimeProvider.System.GetElapsedTime(window.ReportedAt) < TimeSpan.FromMinutes(1))
            return Task.CompletedTask;

        var message = $"Observer {observer.Method.Name}: {error.Message} (repeated {window.Repeats} times).";
        window.Repeats = 0;
        window.ReportedAt = TimeProvider.System.GetTimestamp();
        return LogAsync("Warning", message);
    }

    private AxisSnapshot ReadAxis(ProgramAxis axis)
    {
        try
        {
            if (!_units.TryGetValue(axis.Number, out var units))
            {
                var settings = _motion.GetAxisSettings(axis.Number);
                double scale = axis.MillimetersPerPulse * settings.Pulses / settings.MoveUnit;
                if (!double.IsFinite(scale) || scale <= 0)
                    throw new InvalidOperationException($"Invalid scaling for axis {axis.Name}.");
                units = (settings, scale);
                _units.Add(axis.Number, units);
            }
            return new(axis, _motion.ReadAxis(axis.Number), units.Settings, units.Scale, null);
        }
        catch (Exception ex)
        {
            return new(axis, null, null, null, ex.Message);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _observers = null;
            _timer.Dispose();
        }
    }
}
