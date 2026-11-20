using System.Reflection;
using ControlUI.Manual.Local.ViewModels;
using ControlUI.Motion;
using ControlUI.Support.Services;
using Xunit;

namespace ControlUI.Tests;

public sealed class ManualMotionTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task CanceledHomeStopsItsAxisBeforeReadbackAndBeforeReleasingOwnership(bool failRead, bool homeAll)
    {
        using var simulator = new SimulatedMotionService(TimeProvider.System);
        simulator.Initialize();
        var motion = DispatchProxy.Create<IMotionService, HomingProbe>();
        var probe = (HomingProbe)(object)motion;
        probe.Simulator = simulator;
        probe.FailRead = failRead;
        var log = new EventLogService();
        using var machine = new MachineStateService(motion, log);
        var manual = new ManualContentViewModel(motion, log, new NavigationService(), null!, machine)
        {
            IsMotionInitialized = true
        };

        var homing = homeAll ? manual.HomeAllCommand.ExecuteAsync(null)
            : manual.HomeAxisCommand.ExecuteAsync(manual.MotionAxes[0]);
        await homing.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("start,stop-axis,read", string.Join(",", probe.Events.Take(3)));
        Assert.Equal(new[] { "stop-group", "end" }, probe.Events.TakeLast(2));
        Assert.Equal(homeAll ? simulator.Configuration.GetHomeAxes() : [0], probe.StoppedGroup);
        Assert.False(simulator.IsOperationActive);
        // A deceleration stop can still be settling after the failed readback.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (simulator.ReadAxis(0).Moving) await Task.Delay(10, timeout.Token);
        Assert.False(simulator.ReadAxis(0).Homed);
        Assert.Equal(failRead ? "Fault" : "Stopped", manual.MainStatus);
    }

    public class HomingProbe : DispatchProxy
    {
        public SimulatedMotionService Simulator { get; set; } = null!;
        public bool FailRead { get; set; }
        public List<string> Events { get; } = [];
        public int[]? StoppedGroup { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case nameof(IMotionService.HomeAxis):
                    Simulator.HomeAxis((int)args![0]!);
                    Events.Add("start");
                    Simulator.StopAxis(1, false); // Another axis cancels the operation.
                    return null;
                case nameof(IMotionService.StopAxis):
                    Assert.Equal(0, args![0]);
                    Events.Add("stop-axis");
                    break;
                case nameof(IMotionService.ReadAxis):
                    Events.Add("read");
                    if (FailRead) throw new InvalidOperationException("Injected home readback failure.");
                    break;
                case nameof(IMotionService.StopAxes):
                    StoppedGroup = (int[])args![0]!;
                    Events.Add("stop-group");
                    break;
                case nameof(IMotionService.EndOperation):
                    Events.Add("end");
                    break;
            }
            return targetMethod.Invoke(Simulator, args);
        }
    }

    [Fact]
    public async Task ReleasedQueuedJogCannotClearOrRunPastTheNextPress()
    {
        using var motion = new SimulatedMotionService(TimeProvider.System);
        motion.Initialize();
        var log = new EventLogService();
        using var machine = new MachineStateService(motion, log);
        var manual = new ManualContentViewModel(motion, log, new NavigationService(), null!, machine)
        {
            IsMotionInitialized = true
        };
        var axis = manual.MotionAxes[0];
        await manual.HomeAxisCommand.ExecuteAsync(axis);
        axis.VelocityMmPerSecond = 1;
        axis.AccelMmPerSecondSquared = axis.DecelMmPerSecondSquared = 100;
        var queue = (SemaphoreSlim)typeof(ManualContentViewModel)
            .GetField("_motionQueue", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manual)!;

        // Keep both starts queued while the first release bypasses that queue.
        for (int i = 0; i < 3; i++)
        {
            await queue.WaitAsync();
            Task first, second;
            try
            {
                first = manual.JogPositiveCommand.ExecuteAsync(axis);
                await manual.ReleaseJogCommand.ExecuteAsync(axis).WaitAsync(TimeSpan.FromSeconds(3));
                second = manual.JogNegativeCommand.ExecuteAsync(axis);
            }
            finally { queue.Release(); }

            await Task.WhenAll(first, second);
            Assert.True(axis.JogHeld);
            Assert.True(motion.ReadAxis(axis.AxisNumber).Moving);
            await manual.ReleaseJogCommand.ExecuteAsync(axis);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (motion.ReadAxis(axis.AxisNumber).Moving) await Task.Delay(10, timeout.Token);
            Assert.False(axis.JogHeld);
        }

        // A release must also stop motion when the UI no longer records a held press.
        motion.JogAxis(axis.AxisNumber, axis.MicrometersPerPulse / 1000, 100, 1, 100, 100);
        await manual.ReleaseJogCommand.ExecuteAsync(axis);
        using var finalTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (motion.ReadAxis(axis.AxisNumber).Moving) await Task.Delay(10, finalTimeout.Token);
    }
}
