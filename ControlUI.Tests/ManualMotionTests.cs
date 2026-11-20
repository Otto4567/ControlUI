using System.Reflection;
using ControlUI.Manual.Local.ViewModels;
using ControlUI.Motion;
using ControlUI.Support.Services;
using Xunit;

namespace ControlUI.Tests;

public sealed class ManualMotionTests
{
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
