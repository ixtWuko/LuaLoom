namespace LuaLoom;

/// <summary>
/// Lua 对象句柄。持有注册表引用 id，作用域结束即释放；未显式释放的由终结器入队，
/// 由宿主线程的 Tick 统一回收。
/// </summary>
public abstract class LuaReference : IDisposable
{
    private int _disposed;

    internal LuaReference(Internal.LuaMarshal marshal, int id)
    {
        Marshal = marshal;
        Id = id;
    }

    public int Id { get; }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    internal Internal.LuaMarshal Marshal { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Marshal.ReleaseReference(Id);
            GC.SuppressFinalize(this);
        }
    }

    internal void EnsureAlive()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }

        Marshal.EnsureUsable();
    }

    ~LuaReference()
    {
        if (!IsDisposed)
        {
            Marshal.EnqueueRelease(Id);
        }
    }
}
