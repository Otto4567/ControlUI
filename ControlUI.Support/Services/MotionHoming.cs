namespace ControlUI.Support.Services;

public static class MotionHoming
{
    public static async Task RunAsync(IMotionService motion, MotionPreferences settings,
        Action<int> axisStarted, CancellationToken cancellation = default)
    {
        foreach (int axis in settings.GetHomeAxes())
            await RunAxisAsync(motion, axis, settings.HomeTimeoutSeconds, axisStarted, cancellation);
    }

    public static async Task RunAxisAsync(IMotionService motion, int axis, int timeoutSeconds,
        Action<int> axisStarted, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (motion.IsOperationStopRequested)
            throw new OperationCanceledException("Homing was stopped.");
        axisStarted(axis);
        await Task.Run(() => motion.HomeAxis(axis), cancellation);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            await Task.Delay(100, cancellation);
            if (motion.IsOperationStopRequested)
                throw new OperationCanceledException("Homing was stopped.");
            var state = await Task.Run(() => motion.ReadAxis(axis), cancellation);
            if (motion.IsOperationStopRequested)
                throw new OperationCanceledException("Homing was stopped.");
            if (!state.ServoOn || state.Alarm || state.Emergency ||
                (!state.Homed && state.HomeResult != 2))
                throw new InvalidOperationException($"Axis {axis} homing failed. Result: {state.HomeResult}.");
            if (!state.Moving && state.Homed)
                break;
            if (elapsed.Elapsed.TotalSeconds >= timeoutSeconds)
                throw new TimeoutException($"Axis {axis} exceeded the home timeout.");
        }
    }
}
