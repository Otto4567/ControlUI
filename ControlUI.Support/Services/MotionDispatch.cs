namespace ControlUI.Support.Services;

// A stop invalidates start commands already waiting in either screen's queue.
public sealed class MotionDispatch
{
    private readonly AsyncLocal<long?> _queuedGeneration = new();
    private long _generation;
    public object Gate { get; } = new();

    public Func<T> Capture<T>(Func<T> action)
    {
        long generation = Interlocked.Read(ref _generation);
        return () =>
        {
            long? previous = _queuedGeneration.Value;
            _queuedGeneration.Value = generation;
            try { return action(); }
            finally { _queuedGeneration.Value = previous; }
        };
    }

    public void Invalidate() => Interlocked.Increment(ref _generation);

    public void CheckStart()
    {
        if (_queuedGeneration.Value is long queued && queued != Interlocked.Read(ref _generation))
            throw new OperationCanceledException("A stop canceled the pending motion command.");
    }
}
