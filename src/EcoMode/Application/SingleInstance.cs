namespace ecomode;

internal sealed class SingleInstance : IDisposable
{
    internal const string Name = @"Global\ToolsLib.EcoMode";
    private readonly Mutex _mutex;
    private SingleInstance(Mutex mutex) => _mutex = mutex;

    public static SingleInstance? TryAcquire(string name = Name)
    {
        try
        {
            var mutex = new Mutex(true, name, out bool created);
            if (created) return new SingleInstance(mutex);
            mutex.Dispose();
        }
        catch (UnauthorizedAccessException) { /* another user's instance owns the global object */ }
        return null;
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
