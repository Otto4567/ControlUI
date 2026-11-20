using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlUI.Support.Services;
using ControlUI.Manual.Local.Models;
using Jamesnet.Wpf.Mvvm;


namespace ControlUI.Manual.Local.ViewModels;

public partial class ManualContentViewModel : ObservableBase
{
    private readonly IMotionService _motionService;
    private readonly EventLogService _eventLog;
    private readonly NavigationService _navigation;
    private readonly IUserDialogs _dialogs;
    private readonly SemaphoreSlim _motionQueue = new(1);
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly MachineStateService _machine;
    private MachineSnapshot? _snapshot;
    private MotionPreferences? _displayConfiguration;
    private bool _commandFailed;
    private bool _allStopRequested;

    public string MainStatus =>
        _pendingAllStops != 0 ? "Stopping" :
        _commandFailed || _snapshot?.Fault == true ? "Fault" :
        IsMotionBusy ? "Busy" :
        !IsMotionInitialized ? "Offline" :
        MotionAxes.Any(axis => axis.Moving.IsActive == true) ? (_allStopRequested ? "Stopping" : "Running") :
        _allStopRequested ? "Stopped" :
        _snapshot?.Ready == true ? "Ready" : "Standby";
    private bool _monitorActive;
    private int _pendingAllStops;

    public IReadOnlyList<MotionAxisProfile> MotionAxes { get; private set; } = [];
    public int AxisColumns => Math.Clamp(MotionAxes.Count, 1, 3);

    public IReadOnlyList<TriggerChannelRow> TriggerChannels { get; } =
        [new("CH1"), new("CH2")];

    public IReadOnlyList<LightingChannelRow> LightingChannels { get; } =
        [new("CH1"), new("CH2"), new("CH3"), new("CH4")];

    private static readonly string DefaultMotionParameterPath =
        Path.Combine(AppContext.BaseDirectory, "Config", "motion.mot");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MotionParameterFileName))]
    private string _motionParameterPath = DefaultMotionParameterPath;

    public string MotionParameterFileName => Path.GetFileName(MotionParameterPath);

    [ObservableProperty]
    private string _motionStatus = "Motion is not initialized.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InitializeMotionCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadMotionParametersCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveMotionParametersCommand))]
    [NotifyPropertyChangedFor(nameof(MainStatus))]
    private bool _isMotionBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InitializeMotionCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadMotionParametersCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveMotionParametersCommand))]
    [NotifyPropertyChangedFor(nameof(MainStatus))]
    private bool _isMotionInitialized;

    public ManualContentViewModel(IMotionService motionService, EventLogService eventLog, NavigationService navigation, IUserDialogs dialogs, MachineStateService machine)
    {
        _motionService = motionService;
        _eventLog = eventLog;
        _navigation = navigation;
        _dialogs = dialogs;
        _machine = machine;
        RebuildAxes();
        WeakEventManager<IMotionService, EventArgs>.AddHandler(
            _motionService, nameof(IMotionService.ConfigurationChanged), OnConfigurationChanged);
    }

    private void OnConfigurationChanged(object? sender, EventArgs args) =>
        _ = _dispatcher.InvokeAsync(() =>
        {
            _snapshot = null;
            foreach (var axis in MotionAxes)
            {
                axis.Repeat?.Cancel();
                axis.JogHeld = false;
            }
            IsMotionInitialized = false;
            MotionStatus = "Motion settings applied. Initialize motion.";
            RebuildAxes();
        });

    private void RebuildAxes()
    {
        _displayConfiguration = _motionService.Configuration;
        MotionAxes = _displayConfiguration.Axes.Select(axis =>
            new MotionAxisProfile(axis.Name, axis.Number, axis.MicrometersPerPulse, axis.MaxSpeed,
                axis.GetJogDirection())).ToArray();
        MotionParameterPath = _motionService.Configuration.GetMotPath();
        OnPropertyChanged(nameof(MotionAxes));
        OnPropertyChanged(nameof(AxisColumns));
    }

    private bool CanInitialize() => !IsMotionBusy && !IsMotionInitialized;

    public void SetMonitoring(bool active)
    {
        if (_monitorActive == active) return;
        _monitorActive = active;
        if (active)
        {
            _machine.Updated += OnSnapshot;
            _ = _machine.RefreshAsync();
        }
        else _machine.Updated -= OnSnapshot;
    }

    private Task OnSnapshot(MachineSnapshot snapshot) =>
        _dispatcher.InvokeAsync(() =>
        {
            if (_monitorActive) ApplySnapshot(snapshot);
        }).Task;

    private void ApplySnapshot(MachineSnapshot snapshot)
    {
        if (!_machine.IsCurrent(snapshot) || !ReferenceEquals(_displayConfiguration, snapshot.Configuration))
            return;
        _snapshot = snapshot;
        if (!IsMotionBusy) IsMotionInitialized = snapshot.Initialized;
        for (int i = 0; i < MotionAxes.Count; i++)
        {
            var axis = MotionAxes[i];
            var sample = snapshot.Axes[i];
            var state = sample.State;
            axis.CommandPositionMm = sample.CommandMm;
            axis.ActualPositionMm = sample.ActualMm;
            axis.ActualVelocityMmPerSecond = sample.SpeedMmPerSecond;
            axis.MillimetersPerNativeUnit = sample.MillimetersPerUnit ?? 0;
            axis.SdkSettings = sample.Settings is { } settings
                ? $"{settings.MoveUnit:G}/{settings.Pulses} · {settings.MaxVelocity:G} · {settings.AccelUnitCode}" : "—";
            axis.AlarmCode = state is { Alarm: true }
                ? state.AlarmCode is { } code ? $"Alarm 0x{code:X4}" : "Alarm · code unavailable"
                : null;
            axis.ServoOn.IsActive = state?.ServoOn;
            axis.Alarm.IsActive = state?.Alarm;
            axis.Moving.IsActive = state?.Moving;
            axis.InPosition.IsActive = state?.InPosition;
            axis.HomeSignal.IsActive = state?.HomeSignal;
            axis.Homed.IsActive = state?.Homed;
            axis.PositiveLimit.IsActive = state?.PositiveLimit;
            axis.NegativeLimit.IsActive = state?.NegativeLimit;
            axis.Emergency.IsActive = state?.Emergency;
            if (sample.ReadError != null) MotionStatus = sample.ReadError;
        }
        OnPropertyChanged(nameof(MainStatus));
        LoadMotionParametersCommand.NotifyCanExecuteChanged();
        SaveMotionParametersCommand.NotifyCanExecuteChanged();
    }

    private bool CanTransferParameters() => !IsSimulation && !IsMotionBusy && IsMotionInitialized && !_motionService.IsOperationActive &&
        MotionAxes.All(axis => !axis.JogHeld && axis.Repeat is null && axis.Moving.IsActive != true);

    public bool IsSimulation => _motionService.IsSimulation;

    [RelayCommand]
    private void OpenMotionSettings() => _navigation.Navigate("settings");

    [RelayCommand]
    private Task HomeAllAsync() => RunManualGroupAsync("Homing", () =>
        MotionHoming.RunAsync(_motionService, _motionService.Configuration,
            axis => MotionStatus = $"Homing {MotionAxes.Single(item => item.AxisNumber == axis).Axis}..."));

    [RelayCommand]
    private Task HomeAxisAsync(MotionAxisProfile? axis) =>
        axis is null || !MotionAxes.Contains(axis) ? Task.CompletedTask :
            RunManualGroupAsync("Homing", () => MotionHoming.RunAxisAsync(_motionService, axis.AxisNumber,
                _motionService.Configuration.HomeTimeoutSeconds, _ => MotionStatus = $"Homing {axis.Axis}..."));

    [RelayCommand]
    private Task ServoOnAllAsync() => SetAllServosAsync(true);

    [RelayCommand]
    private Task ServoOffAllAsync() => SetAllServosAsync(false);

    private Task SetAllServosAsync(bool enabled) =>
        RunManualGroupAsync(enabled ? "Servo On All" : "Servo Off All", () =>
            RunAxisAsync(() =>
            {
                foreach (var axis in MotionAxes)
                    _motionService.SetServoEnabled(axis.AxisNumber, enabled);
            }));

    [RelayCommand]
    private Task ServoOnAxisAsync(MotionAxisProfile? axis) =>
        RunAxisMaintenanceAsync(axis, "Servo On", number => _motionService.SetServoEnabled(number, true));

    [RelayCommand]
    private Task ServoOffAxisAsync(MotionAxisProfile? axis) =>
        RunAxisMaintenanceAsync(axis, "Servo Off", number => _motionService.SetServoEnabled(number, false));

    [RelayCommand]
    private Task ResetAxisAlarmAsync(MotionAxisProfile? axis) =>
        RunAxisMaintenanceAsync(axis, "Alarm reset", _motionService.ResetAxisAlarm);

    private Task RunAxisMaintenanceAsync(MotionAxisProfile? axis, string action, Action<int> execute) =>
        axis is null || !MotionAxes.Contains(axis) || axis.Moving.IsActive != false
            ? Task.CompletedTask
            : RunManualGroupAsync($"{axis.Axis} · {action}", () => RunAxisAsync(() => execute(axis.AxisNumber)));

    private async Task RunManualGroupAsync(string name, Func<Task> action)
    {
        if (!IsMotionInitialized || !_motionService.IsReady || IsMotionBusy ||
            _pendingAllStops != 0 || MotionAxes.Any(axis => axis.JogHeld || axis.Repeat is not null))
            return;

        _commandFailed = false;
        _allStopRequested = false;
        IsMotionBusy = true;
        MotionStatus = $"{name}...";
        bool ownsMotion = false;
        try
        {
            await RunAxisAsync(_motionService.BeginOperation);
            ownsMotion = true;
            await action();
            MotionStatus = $"{name} complete.";
            _ = WriteMotionLogAsync("Info", MotionStatus);
        }
        catch (OperationCanceledException) when (_motionService.IsOperationStopRequested)
        {
            _allStopRequested = true;
            MotionStatus = $"{name} stopped.";
            _ = WriteMotionLogAsync("Warning", MotionStatus);
        }
        catch (Exception ex)
        {
            _commandFailed = true;
            MotionStatus = ex.Message;
            if (ownsMotion && !_motionService.IsOperationStopRequested && name == "Homing")
            {
                try
                {
                    var axes = MotionAxes.Select(axis => axis.AxisNumber).ToArray();
                    await Task.Run(() => _motionService.StopAxes(axes, false));
                }
                catch (Exception stopError) { MotionStatus += $" Stop failed: {stopError.Message}"; }
            }
            _ = WriteMotionLogAsync("Error", MotionStatus);
        }
        finally
        {
            if (ownsMotion)
                await RunAxisAsync(_motionService.EndOperation);
            IsMotionBusy = false;
        }
    }

    private async Task<T> OnMotionThreadAsync<T>(Func<T> operation)
    {
        operation = _motionService.Dispatch.Capture(operation);
        await _motionQueue.WaitAsync();
        try
        {
            return await Task.Run(operation);
        }
        finally
        {
            _motionQueue.Release();
        }
    }

    private Task RunAxisAsync(Action operation) => OnMotionThreadAsync(() =>
    {
        operation();
        return true;
    });

    private async Task RecordAxisErrorAsync(Exception error)
    {
        _commandFailed = true;
        OnPropertyChanged(nameof(MainStatus));
        MotionStatus = error.Message;
        await _eventLog.WriteAsync("Error", "Motion", error.Message);
    }

    private bool CanStartAxis(MotionAxisProfile axis) =>
        IsMotionInitialized && _motionService.IsReady && MotionAxes.Contains(axis) &&
        !IsMotionBusy && !_motionService.IsOperationActive &&
        _pendingAllStops == 0 && !axis.JogHeld && axis.Repeat is null;

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task StopAllAsync() => StopAllAxesAsync(false);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task EmergencyStopAllAsync() => StopAllAxesAsync(true);

    private async Task StopAllAxesAsync(bool abrupt)
    {
        if (!IsMotionInitialized)
            return;

        _pendingAllStops++;
        OnPropertyChanged(nameof(MainStatus));
        try
        {
            foreach (var axis in MotionAxes)
            {
                axis.Repeat?.Cancel();
                axis.JogHeld = false;
            }

            int[] axes = MotionAxes.Select(axis => axis.AxisNumber).ToArray();
            await Task.Run(() => _motionService.StopAxes(axes, abrupt));
            _commandFailed = false;
            _allStopRequested = true;
            MotionStatus = abrupt ? "All axes: abrupt stop requested." : "All axes: deceleration stop requested.";
        }
        catch (Exception ex)
        {
            await RecordAxisErrorAsync(ex);
        }
        finally
        {
            _pendingAllStops--;
            OnPropertyChanged(nameof(MainStatus));
        }
    }

    private Func<double, bool, Task> CaptureMove(MotionAxisProfile axis)
    {
        double speed = axis.VelocityMmPerSecond;
        double accel = axis.AccelMmPerSecondSquared;
        double decel = axis.DecelMmPerSecondSquared;
        return (target, relative) => RunAxisAsync(() => _motionService.MoveAxis(axis.AxisNumber,
            axis.MicrometersPerPulse / 1000, axis.MaxSpeedMillimetersPerSecond ?? 0,
            target, relative, speed, accel, decel));
    }

    [RelayCommand]
    private async Task MoveAxisAsync(MotionAxisProfile? axis)
    {
        if (axis is null || !CanStartAxis(axis))
            return;
        _allStopRequested = _commandFailed = false;
        OnPropertyChanged(nameof(MainStatus));
        try
        {
            await CaptureMove(axis)(axis.TargetPositionMm, axis.MoveModeIndex == 1);
        }
        catch (OperationCanceledException) { /* Stop canceled the queued move. */ }
        catch (Exception ex) { await RecordAxisErrorAsync(ex); }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task JogPositiveAsync(MotionAxisProfile? axis) => JogAsync(axis, 1);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task JogNegativeAsync(MotionAxisProfile? axis) => JogAsync(axis, -1);

    private async Task JogAsync(MotionAxisProfile? axis, int direction)
    {
        if (axis is null || !CanStartAxis(axis))
            return;
        _allStopRequested = _commandFailed = false;
        OnPropertyChanged(nameof(MainStatus));

        double speed = axis.VelocityMmPerSecond;
        double accel = axis.AccelMmPerSecondSquared;
        double decel = axis.DecelMmPerSecondSquared;
        double maxSpeed = axis.MaxSpeedMillimetersPerSecond ?? 0;
        if (!double.IsFinite(speed) || speed <= 0 || speed > maxSpeed)
        {
            await RecordAxisErrorAsync(new ArgumentException($"{axis.Axis}: jog speed must be above zero and at most {maxSpeed:G} mm/s."));
            return;
        }
        if (!double.IsFinite(accel) || accel <= 0 || !double.IsFinite(decel) || decel <= 0)
        {
            await RecordAxisErrorAsync(new ArgumentException($"{axis.Axis}: enter positive acceleration and deceleration."));
            return;
        }

        long request = ++axis.JogRequest;
        axis.JogHeld = true;
        try
        {
            string? blocked = await OnMotionThreadAsync(() =>
            {
                var state = _motionService.ReadAxis(axis.AxisNumber);
                if (state.Emergency || state.Alarm)
                    return $"{axis.Axis}: clear the alarm or emergency input before jogging.";
                if (!state.ServoOn)
                    return $"{axis.Axis}: turn the servo on before jogging.";
                if (!state.Homed)
                    return $"{axis.Axis}: home the axis before jogging.";
                if (state.Moving)
                    return $"{axis.Axis}: wait for the axis to stop before jogging.";
                _motionService.JogAxis(axis.AxisNumber, axis.MicrometersPerPulse / 1000,
                    maxSpeed, direction * speed, accel, decel);
                return null;
            });
            if (blocked is not null)
            {
                ReleaseThisRequest();
                await RecordAxisErrorAsync(new InvalidOperationException(blocked));
            }
        }
        catch (OperationCanceledException) { ReleaseThisRequest(); }
        catch (Exception ex)
        {
            ReleaseThisRequest();
            await RecordAxisErrorAsync(ex);
        }
        void ReleaseThisRequest()
        {
            if (axis.JogRequest == request)
                axis.JogHeld = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ReleaseJogAsync(MotionAxisProfile? axis) => StopAsync(axis, false);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SmoothStopAsync(MotionAxisProfile? axis) => StopAsync(axis, false);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task AbruptStopAsync(MotionAxisProfile? axis) => StopAsync(axis, true);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task StopRepeatAsync(MotionAxisProfile? axis) =>
        axis?.Repeat is null ? Task.CompletedTask : StopAsync(axis, false);

    private async Task StopAsync(MotionAxisProfile? axis, bool abrupt)
    {
        if (axis is null || !IsMotionInitialized)
            return;
        axis.Repeat?.Cancel();
        axis.JogHeld = false;
        try
        {
            await Task.Run(() => _motionService.StopAxis(axis.AxisNumber, abrupt));
        }
        catch (Exception ex) { await RecordAxisErrorAsync(ex); }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task StartRepeatAsync(MotionAxisProfile? axis)
    {
        if (axis is null || !CanStartAxis(axis))
            return;
        _allStopRequested = _commandFailed = false;
        OnPropertyChanged(nameof(MainStatus));
        int count = axis.RepeatCount;
        int delay = axis.RepeatDelayMilliseconds;
        double target = axis.TargetPositionMm;
        bool relative = axis.MoveModeIndex == 1;
        var move = CaptureMove(axis);
        using var cancellation = new CancellationTokenSource();
        axis.Repeat = cancellation;
        try
        {
            if (count <= 0 || delay < 0 || axis.MillimetersPerNativeUnit <= 0)
                throw new InvalidOperationException("Check repeat count, delay and axis scaling.");
            var state = await OnMotionThreadAsync(() => _motionService.ReadAxis(axis.AxisNumber));
            double start = state.CommandPosition * axis.MillimetersPerNativeUnit;
            if (relative)
                target += start;
            if (target == start)
                throw new InvalidOperationException("The repeat target must differ from the start position.");

            for (int i = 0; i < count; i++)
            {
                await MoveAndWaitAsync(target);
                await MoveAndWaitAsync(start);
            }
        }
        catch (OperationCanceledException) { /* Repeat token or pending dispatch canceled by Stop. */ }
        catch (Exception ex)
        {
            if (!_motionService.IsOperationActive)
                await StopAsync(axis, false);
            await RecordAxisErrorAsync(ex);
        }
        finally { axis.Repeat = null; }

        async Task MoveAndWaitAsync(double position)
        {
            cancellation.Token.ThrowIfCancellationRequested();
            var before = await OnMotionThreadAsync(() => _motionService.ReadAxis(axis.AxisNumber));
            double direction = position - before.CommandPosition * axis.MillimetersPerNativeUnit;
            cancellation.Token.ThrowIfCancellationRequested();
            await move(position, false);
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            do
            {
                await Task.Delay(100, cancellation.Token);
                var state = await OnMotionThreadAsync(() => _motionService.ReadAxis(axis.AxisNumber));
                if (state.Alarm || state.Emergency || state.IsLimitBlocking(direction) || !state.ServoOn)
                    throw new InvalidOperationException("Axis fault during repeat.");
                if (!state.Moving && state.InPosition &&
                    Math.Abs(state.ActualPosition * axis.MillimetersPerNativeUnit - position) <= axis.MicrometersPerPulse / 1000)
                    break;
                if (elapsed.Elapsed > TimeSpan.FromMinutes(2))
                    throw new TimeoutException("Axis did not reach the repeat target.");
            } while (true);
            await Task.Delay(delay, cancellation.Token);
        }
    }

    private async Task RefreshSnapshotAsync() => ApplySnapshot(await _machine.RefreshAsync());

    [RelayCommand(CanExecute = nameof(CanInitialize))]
    private async Task InitializeMotionAsync()
    {
        if (_motionService.IsReady)
        {
            await RefreshSnapshotAsync();
            return;
        }

        string path = DefaultMotionParameterPath;
        if (await RunMotionAsync(
            () =>
            {
                path = IsSimulation ? DefaultMotionParameterPath : _motionService.Configuration.GetMotPath();
                _motionService.Initialize();
                try
                {
                    _motionService.LoadParameters(path);
                }
                catch (Exception failure)
                {
                    try
                    {
                        _motionService.Dispose();
                    }
                    catch (Exception recoveryError)
                    {
                        throw new InvalidOperationException(
                            $"{failure.Message} Recovery failed: {recoveryError.Message}", failure);
                    }
                    throw;
                }
            },
            "Initializing motion...",
            "Motion initialized and default parameters applied."))
        {
            MotionParameterPath = path;
            IsMotionInitialized = true;
            await RefreshSnapshotAsync();
        }
        else
        {
            _snapshot = null;
            IsMotionInitialized = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanTransferParameters))]
    private async Task LoadMotionParametersAsync()
    {
        string? path = _dialogs.OpenFile("Motion parameter files (*.mot)|*.mot", "Load Motion Parameters",
            Path.GetDirectoryName(MotionParameterPath), Path.GetFileName(MotionParameterPath));
        if (path is null)
            return;
        if (await RunMotionAsync(
            () => _motionService.LoadParameters(path),
            "Applying motion parameters...",
            "Motion parameters applied."))
            MotionParameterPath = path;
        await RefreshSnapshotAsync();
    }

    [RelayCommand(CanExecute = nameof(CanTransferParameters))]
    private async Task SaveMotionParametersAsync()
    {
        string? path = _dialogs.SaveFile("Motion parameter files (*.mot)|*.mot", ".mot",
            "motion-backup.mot", "Save Motion Parameters");
        if (path is null)
            return;

        await RunMotionAsync(
            () => _motionService.SaveParameters(path),
            "Saving motion parameters...",
            "Motion parameters saved.");
    }

    private async Task<bool> RunMotionAsync(
        Action operation,
        string busyMessage,
        string successMessage)
    {
        if (IsMotionBusy)
            return false;

        _commandFailed = false;
        _allStopRequested = false;
        IsMotionBusy = true;
        MotionStatus = busyMessage;

        try
        {
            await RunAxisAsync(operation);
            MotionStatus = successMessage;
            _ = WriteMotionLogAsync("Info", successMessage);
            return true;
        }
        catch (Exception ex)
        {
            _commandFailed = true;
            MotionStatus = ex.Message;
            _ = WriteMotionLogAsync("Error", $"{busyMessage} {ex.Message}");
            return false;
        }
        finally
        {
            IsMotionBusy = false;
        }
    }

    private async Task WriteMotionLogAsync(string level, string message)
    {
        try
        {
            await _eventLog.WriteAsync(level, "Motion", message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("Could not record motion event: {0}", ex.Message);
        }
    }
}
