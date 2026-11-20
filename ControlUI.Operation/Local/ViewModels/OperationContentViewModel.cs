using System.Collections.ObjectModel;
using Jamesnet.Wpf.Mvvm;
using ControlUI.Operation.Local.Models;
using ControlUI.Support.Services;
using ControlUI.Support.Local.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows;

namespace ControlUI.Operation.Local.ViewModels
{
    public partial class OperationContentViewModel : ObservableBase
    {
        private readonly EventLogService _eventLog;
        private readonly IMotionService _motion;
        private readonly DispatcherTimer _monitor = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly SemaphoreSlim _queue = new(1);
        private CancellationTokenSource? _operation;
        private bool _refreshing;
        private bool _stopping;
        private bool _pauseSettled;
        private string? _lastOutcome;
        private readonly SequenceEngine _engine;
        private Task? _stopRequest;

        public OperationContentViewModel(EventLogService eventLog, ProjectWorkspace workspace, IMotionService motion, SequenceEngine engine)
        {
            _engine = engine;
            _eventLog = eventLog;
            _motion = motion;
            Events = eventLog.Entries;
            Projects = workspace.Projects;
            RebuildAxes();
            WeakEventManager<IMotionService, EventArgs>.AddHandler(
                _motion, nameof(IMotionService.ConfigurationChanged), OnConfigurationChanged);
            _monitor.Tick += async (_, _) => await RefreshAxesAsync();
        }

        public void SetMonitoring(bool active)
        {
            if (active)
            {
                _monitor.Start();
                _ = RefreshAxesAsync();
            }
            else
                _monitor.Stop();
        }

        public ObservableCollection<SequenceRecipe> Projects { get; }
        public bool IsSimulation => _motion.IsSimulation;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsProjectStartReady))]
        private SequenceRecipe? _selectedProject;

        [ObservableProperty]
        private int _speedPercent = 100;

        private double SpeedFactor => System.Math.Clamp(SpeedPercent, 1, 100) / 100.0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StepProgress))]
        private int _stepCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StepProgress))]
        private int _completedSteps;

        [ObservableProperty] private string? _currentStep;
        [ObservableProperty] private string? _stepElapsedTime;
        public string StepProgress => $"{CompletedSteps} / {StepCount}";

        public bool IsProjectStartReady => SelectedProject is not null && Axes.Count > 0 &&
            Axes.All(axis => axis.IsConnected == true && axis.IsHomed == true &&
                axis.ServoOn == true && axis.Alarm == false && axis.Emergency == false && axis.Moving == false);

        private void OnAxisChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(IsProjectStartReady));
        }

        public ReadOnlyObservableCollection<EventLogEntry> Events { get; }

        [RelayCommand]
        private void ClearEvents() => _eventLog.ClearDisplay();

        // null은 아직 장비에서 읽지 않은 값입니다. 임의의 정상 상태를 표시하지 않습니다.
        public ObservableCollection<AxisStatusInfo> Axes { get; } = [];

        private void OnConfigurationChanged(object? sender, EventArgs args) =>
            _ = _monitor.Dispatcher.InvokeAsync(() =>
            {
                MotionOnline = false;
                OperationStatus = "Motion settings applied. Initialize motion in Manual.";
                RebuildAxes();
                RefreshCommands();
            });

        private void RebuildAxes()
        {
            foreach (var axis in Axes)
                axis.PropertyChanged -= OnAxisChanged;
            Axes.Clear();
            foreach (var axis in _motion.Configuration.Axes)
            {
                var status = new AxisStatusInfo(axis.Name, axis.Number, axis.MillimetersPerPulse, axis.MaxSpeed);
                status.PropertyChanged += OnAxisChanged;
                Axes.Add(status);
            }
            OnPropertyChanged(nameof(IsProjectStartReady));
        }

        public string MainStatus
        {
            get
            {
                if (_stopping || (_lastOutcome == "Stopped" && Axes.Any(axis => axis.Moving == true)))
                    return "Stopping";
                if (!MotionOnline)
                    return "Offline";
                if (_lastOutcome == "Fault" || Axes.Any(axis => axis.Alarm == true || axis.Emergency == true))
                    return "Fault";
                if (IsSequenceRunning)
                    return IsPaused ? (_pauseSettled ? "Paused" : "Pausing") : "Running";
                if (IsOperating)
                    return "Busy";
                return _lastOutcome ?? (IsProjectStartReady ? "Ready" : "Standby");
            }
        }

        [ObservableProperty] private bool _motionOnline;
        [ObservableProperty] private bool _isOperating;
        [ObservableProperty] private bool _isSequenceRunning;
        [ObservableProperty] private bool _isPaused;
        [ObservableProperty] private int _completedCycles;
        [ObservableProperty] private string _elapsedTime = "—";
        [ObservableProperty] private string _lastCycleTime = "—";
        [ObservableProperty] private string _operationStatus = "Initialize motion in Manual.";

        private async Task<T> AccessMotionAsync<T>(Func<T> action, CancellationToken cancellation = default)
        {
            action = _motion.Dispatch.Capture(action);
            await _queue.WaitAsync(cancellation);
            try { return await Task.Run(action, cancellation); }
            finally { _queue.Release(); }
        }

        private Task AccessMotionAsync(Action action, CancellationToken cancellation = default) =>
            AccessMotionAsync(() => { action(); return true; }, cancellation);

        private async Task RefreshAxesAsync()
        {
            if (_refreshing)
                return;
            _refreshing = true;
            try
            {
                foreach (var axis in Axes.ToArray())
                {
                    MotionAxisState? state = null;
                    double scale = 0;
                    if (_motion.IsReady)
                    {
                        try
                        {
                            (state, scale) = await AccessMotionAsync(() => ReadAxis(axis));
                        }
                        catch (Exception ex)
                        {
                            if (!IsOperating)
                                OperationStatus = ex.Message;
                        }
                    }
                    axis.IsConnected = state is not null;
                    axis.CommandPosition = state?.CommandPosition * scale;
                    axis.Position = state?.ActualPosition * scale;
                    axis.Speed = state?.Velocity * scale;
                    axis.ServoOn = state?.ServoOn;
                    axis.Alarm = state?.Alarm;
                    axis.Moving = state?.Moving;
                    axis.InPosition = state?.InPosition;
                    axis.HomeSignal = state?.HomeSignal;
                    axis.IsHomed = state?.Homed;
                    axis.PositiveLimit = state?.PositiveLimit;
                    axis.NegativeLimit = state?.NegativeLimit;
                    axis.Emergency = state?.Emergency;
                }
                bool online = Axes.All(axis => axis.IsConnected == true);
                if (online && !MotionOnline && !IsOperating)
                    OperationStatus = "Motion connected.";
                MotionOnline = online;
                RefreshCommands();
            }
            finally { _refreshing = false; }
        }

        private (MotionAxisState State, double Scale) ReadAxis(AxisStatusInfo axis)
        {
            var settings = _motion.GetAxisSettings(axis.Number);
            double scale = axis.MillimetersPerPulse * settings.Pulses / settings.MoveUnit;
            if (!double.IsFinite(scale) || scale <= 0)
                throw new InvalidOperationException($"Invalid scaling for axis {axis.Axis}.");
            return (_motion.ReadAxis(axis.Number), scale);
        }

        private bool CanHome() => MotionOnline && !IsOperating && !_stopping &&
            Axes.All(axis => axis.ServoOn == true && axis.Moving == false && axis.Alarm == false && axis.Emergency == false);
        private bool CanReset() => MotionOnline && !IsOperating && !_stopping &&
            Axes.All(axis => axis.Moving == false && axis.Emergency == false);
        [ObservableProperty]
        private string? _runFeedback;

        [RelayCommand]
        private void DismissRunFeedback() => RunFeedback = null;

        private bool CanRun() => !_stopping && (!IsOperating || (IsPaused && _pauseSettled));

        private string? GetRunBlockReason(SequenceRecipe? project)
        {
            if (project is null)
                return "Select a project before running.";
            if (!project.Pages.Any(page => page.Steps.Count > 0))
                return $"Project '{project.Name}' has no steps.";
            if (Axes.Count == 0)
                return "Configure at least one motion axis.";

            foreach (var axis in Axes)
            {
                string? reason = axis.IsConnected != true ? "not connected" :
                    axis.Emergency != false ? "emergency input active or unknown" :
                    axis.Alarm != false ? "alarm active or unknown" :
                    axis.ServoOn != true ? "servo OFF" :
                    axis.Moving != false ? "still moving" :
                    axis.IsHomed != true ? "home required" : null;
                if (reason is not null)
                    return $"Axis {axis.Axis}: {reason}.";
            }

            return null;
        }

        private void ShowRunFeedback(string reason)
        {
            RunFeedback = reason;
            _ = _eventLog.WriteAsync("Warning", "Operation", reason);
        }

        private bool CanPause() => IsSequenceRunning && !IsPaused && !_stopping;
        private bool CanStop() => !_stopping &&
            (IsOperating || (_motion.IsReady && (_motion.IsOperationActive || Axes.Any(axis => axis.Moving == true))));

        partial void OnSelectedProjectChanged(SequenceRecipe? value)
        {
            ClearStepProgress();
            RefreshCommands();
        }

        private void ClearStepProgress()
        {
            CompletedSteps = 0;
            StepCount = 0;
            CurrentStep = null;
            StepElapsedTime = null;
        }

        private void RefreshCommands()
        {
            OnPropertyChanged(nameof(MainStatus));
            HomeCommand.NotifyCanExecuteChanged();
            ResetCommand.NotifyCanExecuteChanged();
            RunCommand.NotifyCanExecuteChanged();
            PauseCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanHome))]
        private Task HomeAsync() => OperateAsync("Homing", token =>
            MotionHoming.RunAsync(_motion, _motion.Configuration,
                axis =>
                {
                    OperationStatus = $"Homing {Axes.Single(item => item.Number == axis).Axis}...";
                    _ = _eventLog.WriteAsync("Info", "Operation", OperationStatus);
                }, token));

        [RelayCommand(CanExecute = nameof(CanReset))]
        private Task ResetAsync() => OperateAsync("Alarm reset", async token =>
        {
            foreach (var axis in Axes)
                await AccessMotionAsync(() => _motion.ResetAxisAlarm(axis.Number), token);
        });

        [RelayCommand(CanExecute = nameof(CanRun), AllowConcurrentExecutions = true)]
        private async Task RunAsync()
        {
            if (IsPaused)
            {
                if (!_pauseSettled || _stopping || !_engine.Resume())
                    return;
                IsPaused = _pauseSettled = false;
                OperationStatus = "Sequence resumed.";
                _ = _eventLog.WriteAsync("Info", "Operation", OperationStatus);
                RefreshCommands();
                return;
            }

            var project = SelectedProject;
            if (!CanRun())
                return;
            if (GetRunBlockReason(project) is { } blocked)
            {
                ShowRunFeedback(blocked);
                return;
            }
            if (project is null)
                return;

            var mode = project.RunMode;
            int repeats = Math.Max(1, project.RepeatCount);
            double speedFactor = SpeedFactor;

            var steps = new List<SequenceInstruction>();
            foreach (var page in project.Pages)
            {
                for (int index = 0; index < page.Steps.Count; index++)
                {
                    var step = page.Steps[index];
                    try
                    {
                        var moves = BuildMoves(step);
                        if (step.Action == StepAction.Wait)
                        {
                            if (step.WaitMilliseconds < 0)
                                throw new InvalidOperationException("Wait time must be zero or greater (ms).");
                        }
                        else
                        {
                            if (moves.Length == 0)
                                throw new InvalidOperationException("Select at least one axis for the move.");
                            foreach (var move in moves)
                            {
                                string axis = Axes.Single(item => item.Number == move.Axis).Axis;
                                if (!double.IsFinite(move.Speed) || move.Speed <= 0 || move.Speed > move.MaxSpeed)
                                    throw new InvalidOperationException($"Axis {axis}: speed must be above zero and at most {move.MaxSpeed:G} mm/s.");
                                if (!double.IsFinite(move.Position) ||
                                    !double.IsFinite(move.AccelMmPerSecondSquared) || move.AccelMmPerSecondSquared <= 0 ||
                                    !double.IsFinite(move.DecelMmPerSecondSquared) || move.DecelMmPerSecondSquared <= 0)
                                    throw new InvalidOperationException($"Axis {axis}: enter a valid target and positive acceleration and deceleration.");
                            }
                        }
                        steps.Add(new(page.Name, step.Action, step.WaitMilliseconds, moves));
                    }
                    catch (InvalidOperationException ex)
                    {
                        ShowRunFeedback($"{page.Name} · Step {index + 1}: {ex.Message}");
                        return;
                    }
                }
            }

            RunFeedback = null;
            ClearStepProgress();
            StepCount = steps.Count;
            CompletedCycles = 0;
            _lastOutcome = null;
            IsOperating = IsSequenceRunning = true;
            RefreshCommands();
            var plan = new SequencePlan(project.Name, mode, repeats, speedFactor, _motion.Configuration.Axes.ToArray(), steps.ToArray());
            _engine.Progress += Report;
            try
            {
                var result = await _engine.RunAsync(plan);
                _lastOutcome = result.Outcome.ToString();
                OperationStatus = result.Message;
                _ = _eventLog.WriteAsync(result.Outcome switch
                {
                    SequenceOutcome.Fault => "Error",
                    SequenceOutcome.Stopped => "Warning",
                    _ => "Info"
                }, "Operation", result.Message);
            }
            catch (Exception ex)
            {
                _lastOutcome = "Fault";
                OperationStatus = ex.Message;
                _ = _eventLog.WriteAsync("Error", "Operation", ex.Message);
            }
            finally
            {
                _engine.Progress -= Report;
                IsOperating = IsSequenceRunning = IsPaused = _pauseSettled = false;
                RefreshCommands();
            }

            void Report(SequenceProgress progress) =>
                _ = _monitor.Dispatcher.InvokeAsync(() =>
                {
                    CompletedSteps = progress.CompletedSteps;
                    CompletedCycles = progress.Cycles;
                    CurrentStep = progress.Step;
                    ElapsedTime = SequenceEngine.FormatDuration(progress.Elapsed, false);
                    if (progress.StepElapsed is { } elapsed)
                        StepElapsedTime = SequenceEngine.FormatDuration(elapsed, true);
                    if (progress.LastCycle is { } cycle)
                        LastCycleTime = SequenceEngine.FormatDuration(cycle, true);
                    _pauseSettled = IsPaused && progress.PauseSettled;
                    if (_pauseSettled)
                        OperationStatus = "Paused. Press Run to resume.";
                    else if (!_stopping && !IsPaused && CurrentStep != null)
                        OperationStatus = CurrentStep;
                    if (progress.LogLevel != null && progress.LogMessage != null)
                        _ = _eventLog.WriteAsync(progress.LogLevel, "Sequence", progress.LogMessage);
                    RefreshCommands();
                });
        }

        private MotionMove[] BuildMoves(SequenceStep step)
        {
            AxisStatusInfo FindAxis(string name) =>
                Axes.SingleOrDefault(axis => StringComparer.OrdinalIgnoreCase.Equals(axis.Axis, name))
                ?? throw new InvalidOperationException($"Axis '{name}' is not configured.");

            MotionMove Build(AxisStatusInfo axis, double position, double speed, double accel, double decel) =>
                new(axis.Number, axis.MillimetersPerPulse, axis.MaxSpeed, position, speed, accel, decel);

            if (step.Action == StepAction.Move)
                return [Build(FindAxis(step.Axis),
                    step.PositionMm, step.VelocityMmPerSecond, step.AccelMmPerSecondSquared, step.DecelMmPerSecondSquared)];
            if (step.Action != StepAction.MultiAxisMove)
                return [];
            return step.AxisTargets.Where(entry => entry.Value.IsEnabled)
                .Select(entry => Build(FindAxis(entry.Key), entry.Value.PositionMm, entry.Value.VelocityMmPerSecond,
                    entry.Value.AccelMmPerSecondSquared, entry.Value.DecelMmPerSecondSquared)).ToArray();
        }

        [RelayCommand(CanExecute = nameof(CanPause))]
        private void Pause()
        {
            _pauseSettled = false;
            _engine.Pause();
            IsPaused = true;
            OperationStatus = "Pausing current step...";
            _ = _eventLog.WriteAsync("Info", "Operation", OperationStatus);
            RefreshCommands();
        }

        [RelayCommand(CanExecute = nameof(CanStop))]
        private async Task StopAsync()
        {
            _stopping = true;
            OperationStatus = "Stopping...";
            RefreshCommands();
            var cancellation = _operation;
            cancellation?.Cancel();
            var stop = IsSequenceRunning ? _engine.StopAsync() : StopAxesAsync();
            _stopRequest = stop;
            try
            {
                await stop;
                _lastOutcome = "Stopped";
                OperationStatus = "Stop requested.";
                _ = _eventLog.WriteAsync("Warning", "Operation", OperationStatus);
            }
            catch (Exception ex)
            {
                _lastOutcome = "Fault";
                OperationStatus = ex.Message;
                _ = _eventLog.WriteAsync("Error", "Operation", OperationStatus);
            }
            finally { _stopRequest = null; _stopping = false; RefreshCommands(); }
        }

        private Task StopAxesAsync()
        {
            int[] numbers = Axes.Select(axis => axis.Number).ToArray();
            return Task.Run(() => _motion.StopAxes(numbers, false));
        }

        private async Task OperateAsync(string name, Func<CancellationToken, Task> action)
        {
            if (IsOperating || _stopping)
                return;
            using var cancellation = new CancellationTokenSource();
            _operation = cancellation;
            _lastOutcome = null;
            IsOperating = true;
            RefreshCommands();
            bool ownsMotion = false;
            try
            {
                await AccessMotionAsync(_motion.BeginOperation, cancellation.Token);
                ownsMotion = true;
                await action(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                OperationStatus = $"{name} complete.";
                _ = _eventLog.WriteAsync("Info", "Operation", OperationStatus);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested || _motion.IsOperationStopRequested)
            {
                if (!cancellation.IsCancellationRequested)
                {
                    _lastOutcome = "Stopped";
                    OperationStatus = $"{name} stopped.";
                    _ = _eventLog.WriteAsync("Warning", "Operation", OperationStatus);
                }
            }
            catch (Exception ex)
            {
                _lastOutcome = "Fault";
                OperationStatus = ex.Message;
                if (ownsMotion)
                {
                    try { await StopAxesAsync(); }
                    catch (Exception stopError) { OperationStatus += $" Stop failed: {stopError.Message}"; }
                }
                _ = _eventLog.WriteAsync("Error", "Operation", OperationStatus);
            }
            finally
            {
                if (_stopRequest is { } stop)
                {
                    try { await stop; }
                    catch { /* StopAsync reports the failure. */ }
                }
                if (ownsMotion)
                    await AccessMotionAsync(_motion.EndOperation);
                _operation = null;
                IsOperating = IsSequenceRunning = IsPaused = _pauseSettled = false;
                RefreshCommands();
            }
        }
    }
}
