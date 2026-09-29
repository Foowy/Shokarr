namespace Shokarr.Services;

public sealed class RetryOnceGate
{
    private readonly object _lock = new();
    private volatile bool _done;

    public void Run(Action action)
    {
        if (_done)
            return;
        lock (_lock)
        {
            if (_done)
                return;
            action();
            _done = true;
        }
    }
}
