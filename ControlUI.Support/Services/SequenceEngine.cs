using System.Diagnostics;
using ControlUI.Support.Local.Models;

namespace ControlUI.Support.Services;

public sealed record SequenceInstruction(string Page, StepAction Action, int WaitMilliseconds, MotionMove[] Moves);
public sealed record SequencePlan(string Name, ProjectRunMode Mode, int Repeats, double SpeedFactor,
    ProgramAxis[] Axes, SequenceInstruction[] Steps);
public enum SequenceOutcome { Completed, Stopped, Fault }
public sealed record SequenceResult(SequenceOutcome Outcome, string Message);
public sealed record SequenceProgress(int Cycles, int CompletedSteps, string? Step, TimeSpan Elapsed,
    TimeSpan? StepElapsed, TimeSpan? LastCycle, bool PauseSettled, string? LogLevel, string? LogMessage);

public sealed class SequenceEngine(IMotionService motion)
{
    private readonly object _gate = new();
    private Execution? _active;
    public event Action<SequenceProgress>? Progress;

    public Task<SequenceResult> RunAsync(SequencePlan plan)
    {
        lock (_gate)
        {
            if (plan.Steps.Length == 0 || plan.Axes.Length == 0 || !double.IsFinite(plan.SpeedFactor) ||
                plan.SpeedFactor is <= 0 or > 1 || plan.Steps.Any(step => step.WaitMilliseconds < 0 && step.Action == StepAction.Wait))
                throw new ArgumentException("Invalid sequence plan.");
            if (_active != null)
                throw new InvalidOperationException("A sequence is already running.");
            var execution = new Execution(motion, plan, progress => Progress?.Invoke(progress));
            _active = execution;
            var begin = motion.Dispatch.Capture(() => { motion.BeginOperation(); return true; });
            return Task.Run(async () =>
            {
                try { return await execution.RunAsync(begin).ConfigureAwait(false); }
                finally
                {
                    lock (_gate)
                    {
                        _active = null;
                        execution.Dispose();
                    }
                }
            });
        }
    }

    public void Pause() { lock (_gate) _active?.RequestPause(); }
    public bool Resume() { lock (_gate) return _active?.Resume() == true; }
    public Task StopAsync() { lock (_gate) return _active?.StopAsync() ?? Task.CompletedTask; }

    private sealed class Execution(IMotionService motion, SequencePlan plan, Action<SequenceProgress> report) : IDisposable
    {
        private readonly object _stopGate = new();
        private readonly CancellationTokenSource _cancel = new();
        private readonly Stopwatch _elapsed = new();
        private readonly int[] _axes = plan.Axes.Select(axis => axis.Number).ToArray();
        private readonly Dictionary<int, ProgramAxis> _definitions = plan.Axes.ToDictionary(axis => axis.Number);
        private Stopwatch? _stepClock;
        private string? _step;
        private int _cycles, _completed;
        private TimeSpan? _lastCycle;
        private TimeSpan _lastReport;
        private int _pause; // 0 running, 1 requested, 2 settled
        private Task? _stop;
        private bool _finished;

        public void RequestPause() => Interlocked.CompareExchange(ref _pause, 1, 0);
        public bool Resume() => Interlocked.CompareExchange(ref _pause, 0, 2) == 2;

        public Task StopAsync()
        {
            lock (_stopGate)
            {
                if (_finished) return Task.CompletedTask;
                _cancel.Cancel();
                return _stop ??= Task.Run(() => motion.StopAxes(_axes, false));
            }
        }

        public async Task<SequenceResult> RunAsync(Func<bool> begin)
        {
            bool owned = false;
            SequenceOutcome outcome = SequenceOutcome.Completed;
            string message = $"{plan.Name} complete.";
            try
            {
                CheckStop(checkBoard: false);
                owned = begin();
                foreach (int number in _axes)
                {
                    var state = motion.ReadAxis(number);
                    if (!state.Homed || state.Moving || !state.ServoOn || state.Alarm || state.Emergency)
                        throw new InvalidOperationException("All axes must be homed, stopped and ready.");
                }
                _elapsed.Start();
                do
                {
                    var cycle = Stopwatch.StartNew();
                    _completed = 0;
                    foreach (var step in plan.Steps)
                    {
                        await WaitForResumeAsync().ConfigureAwait(false);
                        CheckStop();
                        string action = step.Action == StepAction.Wait
                            ? $"Wait {step.WaitMilliseconds / plan.SpeedFactor / 1000:0.###} s"
                            : $"Move {string.Join(", ", step.Moves.Select(m => $"{_definitions[m.Axis].Name} to {m.Position:0.####} mm"))}";
                        _step = $"Cycle {_cycles + 1} · Step {_completed + 1} · {action}";
                        _stepClock = Stopwatch.StartNew();
                        _unfinished = true;
                        Publish("Info", $"{plan.Name} / {step.Page} · {_step} · Started.");
                        if (step.Action == StepAction.Wait)
                            // Speed override scales cycle time, including waits: 50% doubles the delay.
                            await WaitStepAsync(step.WaitMilliseconds / plan.SpeedFactor).ConfigureAwait(false);
                        else
                            await MoveAsync(step.Moves).ConfigureAwait(false);
                        CheckStop();
                        ++_completed;
                        FinishStep("Completed", "Info");
                    }
                    _lastCycle = cycle.Elapsed;
                    ++_cycles;
                    Publish();
                    await Task.Delay(1, _cancel.Token).ConfigureAwait(false);
                } while (plan.Mode == ProjectRunMode.Continuous || (plan.Mode == ProjectRunMode.Repeat && _cycles < plan.Repeats));
            }
            catch (OperationCanceledException)
            {
                outcome = SequenceOutcome.Stopped;
                message = $"{plan.Name} stopped.";
            }
            catch (Exception ex)
            {
                outcome = SequenceOutcome.Fault;
                message = ex.Message;
            }

            Task? stopping;
            lock (_stopGate)
            {
                if (_cancel.IsCancellationRequested && outcome == SequenceOutcome.Completed)
                {
                    outcome = SequenceOutcome.Stopped;
                    message = $"{plan.Name} stopped.";
                }
                _finished = true;
                stopping = _stop;
            }
            try
            {
                if (stopping != null) await stopping.ConfigureAwait(false);
                else if (owned && outcome != SequenceOutcome.Completed) motion.StopAxes(_axes, false);
                if (owned && outcome != SequenceOutcome.Completed)
                {
                    var timeout = Stopwatch.StartNew();
                    while (_axes.Any(axis => motion.ReadAxis(axis).Moving))
                    {
                        if (timeout.Elapsed >= TimeSpan.FromSeconds(10)) throw new TimeoutException("Stop did not settle.");
                        await Task.Delay(25).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) { outcome = SequenceOutcome.Fault; message += $" Stop failed: {ex.Message}"; }
            finally
            {
                if (owned)
                {
                    try { motion.EndOperation(); }
                    catch (Exception ex) { outcome = SequenceOutcome.Fault; message += $" Release failed: {ex.Message}"; }
                }
            }
            _elapsed.Stop();
            if (_unfinished)
                FinishStep(outcome == SequenceOutcome.Stopped ? "Stopped" : $"Failed: {message}",
                    outcome == SequenceOutcome.Stopped ? "Warning" : "Error");
            Publish();
            return new(outcome, message);
        }

        private bool _unfinished;

        private void FinishStep(string result, string level)
        {
            _stepClock!.Stop();
            _unfinished = false;
            Publish(level, $"{plan.Name} · {_step} · {result} ({FormatDuration(_stepClock.Elapsed, true)}).");
        }

        private async Task WaitStepAsync(double milliseconds)
        {
            var wait = Stopwatch.StartNew();
            while (wait.Elapsed.TotalMilliseconds < milliseconds)
            {
                CheckStop();
                CheckAxisFaults();
                if (Volatile.Read(ref _pause) != 0)
                {
                    wait.Stop();
                    await WaitForResumeAsync().ConfigureAwait(false);
                    wait.Start();
                }
                await DelayAsync(Math.Clamp(milliseconds - wait.Elapsed.TotalMilliseconds, 1, 25)).ConfigureAwait(false);
            }
        }

        private async Task WaitForResumeAsync()
        {
            CheckStop();
            if (Interlocked.CompareExchange(ref _pause, 2, 1) != 1) return;
            if (_unfinished) _stepClock?.Stop();
            Publish();
            while (Volatile.Read(ref _pause) == 2)
            {
                CheckStop();
                CheckAxisFaults();
                await DelayAsync(25).ConfigureAwait(false);
            }
            if (_unfinished) _stepClock?.Start();
        }

        private async Task MoveAsync(MotionMove[] requested)
        {
            var moves = requested.Select(move => move with { Speed = move.Speed * plan.SpeedFactor }).ToArray();
            var axes = moves.Select(move => move.Axis).ToArray();
            while (true)
            {
                await WaitForResumeAsync().ConfigureAwait(false);
                var start = motion.Dispatch.Capture(() =>
                {
                    CheckStop();
                    var targets = moves.Select(move =>
                    {
                        var settings = motion.GetAxisSettings(move.Axis);
                        double scale = move.MillimetersPerPulse * settings.Pulses / settings.MoveUnit;
                        if (!double.IsFinite(scale) || scale <= 0)
                            throw new InvalidOperationException($"Invalid scaling for axis {move.Axis}.");
                        var state = motion.ReadAxis(move.Axis);
                        return (Move: move, Scale: scale, Direction: move.Position - state.CommandPosition * scale);
                    }).ToArray();
                    motion.MoveOperationAxes(moves);
                    return targets;
                });
                var targets = start();
                var timeout = Stopwatch.StartNew();
                bool settling = false;
                while (true)
                {
                    CheckStop();
                    if (!settling && Volatile.Read(ref _pause) != 0)
                    {
                        motion.PauseOperationAxes(axes);
                        settling = true;
                    }
                    bool complete = true;
                    foreach (var (move, scale, direction) in targets)
                    {
                        var state = motion.ReadAxis(move.Axis);
                        if (!state.ServoOn || state.Alarm || state.Emergency || state.IsLimitBlocking(direction))
                            throw new InvalidOperationException($"Axis {_definitions[move.Axis].Name} fault during motion.");
                        complete &= !state.Moving && (settling || (state.InPosition &&
                            Math.Abs(state.ActualPosition * scale - move.Position) <= move.MillimetersPerPulse));
                    }
                    if (complete)
                    {
                        if (!settling) return;
                        break;
                    }
                    if (timeout.Elapsed >= TimeSpan.FromMinutes(5))
                        throw new TimeoutException("Motion did not complete within five minutes.");
                    await DelayAsync(25).ConfigureAwait(false);
                }
            }
        }

        private void CheckAxisFaults()
        {
            foreach (int number in _axes)
            {
                var state = motion.ReadAxis(number);
                if (state.Alarm || state.Emergency || !state.ServoOn)
                    throw new InvalidOperationException($"Axis {_definitions[number].Name} fault during sequence.");
            }
        }

        private void CheckStop(bool checkBoard = true)
        {
            _cancel.Token.ThrowIfCancellationRequested();
            if (checkBoard && motion.IsOperationStopRequested) throw new OperationCanceledException("Axis stopped.");
        }

        private async Task DelayAsync(double milliseconds)
        {
            if (_elapsed.Elapsed - _lastReport >= TimeSpan.FromMilliseconds(200)) Publish();
            await Task.Delay(TimeSpan.FromMilliseconds(milliseconds), _cancel.Token).ConfigureAwait(false);
        }

        private void Publish(string? level = null, string? message = null)
        {
            _lastReport = _elapsed.Elapsed;
            report(new(_cycles, _completed, _step, _lastReport, _stepClock?.Elapsed, _lastCycle,
                Volatile.Read(ref _pause) == 2, level, message));
        }

        public void Dispose() => _cancel.Dispose();
    }

    public static string FormatDuration(TimeSpan time, bool tenths)
    {
        string value = $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
        return tenths ? $"{value}.{time.Milliseconds / 100}" : value;
    }
}
