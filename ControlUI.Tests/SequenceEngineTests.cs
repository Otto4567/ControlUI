using ControlUI.Motion;
using ControlUI.Support.Local.Models;
using ControlUI.Support.Services;
using Xunit;

namespace ControlUI.Tests;

public sealed class SequenceEngineTests : IAsyncLifetime
{
    private static readonly ProgramAxis Axis = new("Y", 0, 0.1, 1000);
    private readonly SimulatedMotionService _motion = new(TimeProvider.System,
        configuration: new MotionPreferences { Axes = [Axis], HomeOrder = "Y" });
    private readonly SequenceEngine _engine;

    public SequenceEngineTests() => _engine = new(_motion);

    public async Task InitializeAsync()
    {
        _motion.Initialize();
        _motion.BeginOperation();
        _motion.HomeAxis(0);
        await UntilAsync(() => _motion.ReadAxis(0).Homed);
        _motion.EndOperation();
    }

    public Task DisposeAsync()
    {
        _motion.Dispose();
        return Task.CompletedTask;
    }

    private static SequencePlan Move(double target = 2) =>
        new("Move test", ProjectRunMode.Once, 1, 1, [Axis],
            [new("Page", StepAction.Move, 0, [new(0, Axis.MillimetersPerPulse, Axis.MaxSpeed, target, 20, 100, 100)])]);

    private static SequencePlan Wait(int ms, ProjectRunMode mode = ProjectRunMode.Once) =>
        new("Wait test", mode, 3, 1, [Axis], [new("Page", StepAction.Wait, ms, [])]);

    [Fact]
    public async Task MoveCompletesAtTheRequestedPosition()
    {
        var result = await _engine.RunAsync(Move()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SequenceOutcome.Completed, result.Outcome);
        Assert.Equal(2, _motion.ReadAxis(0).ActualPosition, 4);
        Assert.False(_motion.IsOperationActive);
    }

    [Fact]
    public async Task PauseSettlesBeforeResumeAndRetainsTheAbsoluteTarget()
    {
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.Progress += p => { if (p.PauseSettled) paused.TrySetResult(); };
        var run = _engine.RunAsync(Move(12));
        await UntilAsync(() => _motion.ReadAxis(0).Velocity > 1);
        _engine.Pause();
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var state = _motion.ReadAxis(0);
        Assert.False(state.Moving);
        Assert.InRange(state.ActualPosition, 0, 11.99);
        Assert.False(run.IsCompleted);
        Assert.True(_engine.Resume());
        Assert.Equal(SequenceOutcome.Completed, (await run.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
        Assert.Equal(12, _motion.ReadAxis(0).ActualPosition, 4);
    }

    [Fact]
    public async Task StopDuringMotionEndsOwnershipAfterTheAxisSettles()
    {
        var run = _engine.RunAsync(Move(50));
        await UntilAsync(() => _motion.ReadAxis(0).Velocity > 1);
        await _engine.StopAsync();
        Assert.Equal(SequenceOutcome.Stopped, (await run.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
        Assert.False(_motion.ReadAxis(0).Moving);
        Assert.False(_motion.IsOperationActive);
    }

    [Fact]
    public async Task AlarmDuringMotionProducesAFault()
    {
        var run = _engine.RunAsync(Move(50));
        await UntilAsync(() => _motion.ReadAxis(0).Moving);
        _motion.SimulateAlarm(0);
        Assert.Equal(SequenceOutcome.Fault, (await run.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
        Assert.False(_motion.ReadAxis(0).Moving);
        Assert.False(_motion.IsOperationActive);
    }

    [Fact]
    public async Task RepeatRunsExactlyTheSpecifiedNumberOfCycles()
    {
        int completed = 0;
        _engine.Progress += p => completed = p.Cycles;
        var result = await _engine.RunAsync(Wait(0, ProjectRunMode.Repeat)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SequenceOutcome.Completed, result.Outcome);
        Assert.Equal(3, completed);
    }

    [Fact]
    public async Task ContinuousRunContinuesUntilStopped()
    {
        var cycled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.Progress += p => { if (p.Cycles >= 4) cycled.TrySetResult(); };
        var run = _engine.RunAsync(Wait(5, ProjectRunMode.Continuous));
        await cycled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(run.IsCompleted);
        await _engine.StopAsync();
        Assert.Equal(SequenceOutcome.Stopped, (await run.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
    }

    [Fact]
    public async Task ExternalStopInvalidatesQueuedStartBeforeOwnership()
    {
        Task<SequenceResult> run;
        lock (_motion.Dispatch.Gate)
        {
            run = _engine.RunAsync(Move());
            _motion.StopAxis(0, true);
        }
        Assert.Equal(SequenceOutcome.Stopped, (await run.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
        Assert.False(_motion.IsOperationActive);
        Assert.Equal(0, _motion.ReadAxis(0).CommandPosition);
    }

    [Fact]
    public async Task AlarmDuringWaitFaultsTheSequence()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.Progress += p => { if (p.Step != null) started.TrySetResult(); };
        var run = _engine.RunAsync(Wait(10_000));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _motion.SimulateAlarm(0);
        Assert.Equal(SequenceOutcome.Fault, (await run.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
        Assert.False(_motion.IsOperationActive);
    }

    [Fact]
    public async Task HalfSpeedWaitResumesWithItsRemainingScaledDuration()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paused = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.Progress += p =>
        {
            if (p.Step != null) started.TrySetResult();
            if (p.PauseSettled && p.StepElapsed is { } elapsed) paused.TrySetResult(elapsed);
        };
        var run = _engine.RunAsync(Wait(800) with { SpeedFactor = 0.5 });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(600);
        _engine.Pause();
        var elapsed = await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(1700);
        Assert.False(run.IsCompleted);
        var resumed = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(_engine.Resume());
        Assert.Equal(SequenceOutcome.Completed, (await run.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
        Assert.InRange((elapsed + resumed.Elapsed).TotalMilliseconds, 1550, 1950);
    }

    private static async Task UntilAsync(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!ready()) await Task.Delay(5, timeout.Token);
    }
}
