using System.Collections.Concurrent;
using ControlUI.Motion;
using ControlUI.Support.Services;
using Xunit;

namespace ControlUI.Tests;

public sealed class MachineStateServiceTests
{
    [Fact]
    public async Task UnitsAreReusedUntilReloadAndOldSamplesAreInvalidated()
    {
        using var motion = new SimulatedMotionService(TimeProvider.System);
        using var machine = new MachineStateService(motion);
        var offline = await machine.RefreshAsync();
        Assert.False(offline.Online);
        Assert.All(offline.Axes, axis => Assert.Null(axis.State));

        motion.Initialize();
        var first = await machine.RefreshAsync();
        var second = await machine.RefreshAsync();
        Assert.True(second.Online);
        Assert.False(second.Ready);
        Assert.Same(first.Axes[0].Settings, second.Axes[0].Settings);
        Assert.Equal(1d, second.Axes[0].MillimetersPerUnit);

        motion.LoadParameters("simulated.mot");
        Assert.False(machine.IsCurrent(second));
        var reloaded = await machine.RefreshAsync();
        Assert.NotSame(first.Axes[0].Settings, reloaded.Axes[0].Settings);
        motion.SimulateAlarm(0);
        var fault = await machine.RefreshAsync();
        Assert.True(fault.Fault);
        Assert.Equal("alarm active", fault.Axes[0].NotReadyReason);

        motion.ApplyConfiguration(new MotionPreferences { Axes = [new("U", 7, 1, 50)], HomeOrder = "U" }, () => { });
        Assert.False(machine.IsCurrent(reloaded));
        var changed = await machine.RefreshAsync();
        Assert.False(changed.Ready);
        Assert.Equal(7, Assert.Single(changed.Axes).Axis.Number);
        Assert.Null(changed.Axes[0].CommandMm);
    }

    [Fact]
    public async Task BothActiveScreensReceiveTheSameSample()
    {
        using var motion = new SimulatedMotionService(TimeProvider.System);
        using var machine = new MachineStateService(motion);
        var seen = new ConcurrentQueue<MachineSnapshot>();
        var next = new TaskCompletionSource<MachineSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        machine.Updated += seen.Enqueue;
        void Second(MachineSnapshot value) => next.TrySetResult(value);
        machine.Updated += Second;
        try
        {
            var sample = await next.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Contains(seen, other => ReferenceEquals(other, sample));
        }
        finally
        {
            machine.Updated -= Second;
            machine.Updated -= seen.Enqueue;
        }
    }
}
