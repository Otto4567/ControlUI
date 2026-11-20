using ControlUI.Motion;
using ControlUI.Support.Services;
using Xunit;

namespace ControlUI.Tests;

public sealed class AjinMotionConcurrencyTests
{
    [Theory]
    [InlineData("count")]
    [InlineData("settings")]
    [InlineData("save")]
    public async Task OrdinaryCallsWaitForConfigurationWhileStopRemainsAvailable(string operation)
    {
        var config = new MotionPreferences();
        using var motion = new AjinMotionService(config);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeout = TimeSpan.FromSeconds(5);
        var changing = Task.Run(() => motion.ApplyConfiguration(config, () =>
        {
            entered.SetResult();
            Assert.True(release.Wait(timeout));
        }));
        try
        {
            await entered.Task.WaitAsync(timeout);
            var ordinary = Task.Run(() =>
            {
                attempting.SetResult();
                return Assert.Throws<InvalidOperationException>(() =>
                {
                    switch (operation)
                    {
                        case "count": motion.GetAxisCount(); break;
                        case "settings": motion.GetAxisSettings(0); break;
                        case "save": motion.SaveParameters("unused.mot"); break;
                    }
                });
            });
            await attempting.Task.WaitAsync(timeout);
            await Task.Run(() =>
            {
                motion.StopForShutdown();
                motion.StopForUnhandledException();
            }).WaitAsync(timeout);
            Assert.NotSame(ordinary, await Task.WhenAny(ordinary, Task.Delay(100)));
            release.Set();
            Assert.Contains("not initialized", (await ordinary.WaitAsync(timeout)).Message);
        }
        finally
        {
            release.Set();
            await changing.WaitAsync(timeout);
        }
    }
}
