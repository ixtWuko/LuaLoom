using System.Runtime.InteropServices;
using LuaLoom.Internal;
using LuaLoom.Native;
using NativeMarshal = System.Runtime.InteropServices.Marshal;

namespace LuaLoom;

public enum LuaCoroutineStatus
{
    Suspended,
    Dead,
}

/// <summary>Lua 协程句柄：由 <see cref="LuaFunction.CreateCoroutine"/> 创建，C# 侧推进。</summary>
public sealed class LuaCoroutine : IDisposable
{
    private readonly LuaMarshal _marshal;
    private readonly IntPtr _thread;
    private readonly int _reference;
    private int _disposed;

    internal LuaCoroutine(LuaMarshal marshal, int functionReference)
    {
        _marshal = marshal;
        var main = marshal.State;

        using (LuaStackGuard.Enter(main))
        {
            _thread = LuaApi.lua_newthread(main);
            if (_thread == IntPtr.Zero)
            {
                throw new LuaException(LuaStatus.ErrMem, "lua_newthread returned a null thread.");
            }

            marshal.PushReference(functionReference);
            LuaApi.lua_xmove(main, _thread, 1);
            _reference = marshal.AddReference(LuaApi.lua_gettop(main));
        }
    }

    public LuaCoroutineStatus Status { get; private set; } = LuaCoroutineStatus.Suspended;

    public object[] Resume(params object[] args)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(LuaCoroutine));
        }

        _marshal.EnsureUsable();

        if (Status == LuaCoroutineStatus.Dead)
        {
            throw new InvalidOperationException("Coroutine has already finished.");
        }

        ArgumentNullException.ThrowIfNull(args);

        var main = _marshal.State;
        using var guard = LuaStackGuard.Enter(main);

        LuaApi.lua_rawgeti(main, LuaApi.Profile.RegistryIndex, _reference);
        var threadIndex = LuaApi.lua_gettop(main);

        foreach (var argument in args)
        {
            _marshal.PushObject(argument);
        }

        LuaApi.lua_xmove(main, _thread, args.Length);

        var status = LuaApi.Profile.Resume(_thread, main, args.Length, out var resultCount);
        if (status != LuaApi.LuaOk && status != LuaApi.LuaYield)
        {
            Status = LuaCoroutineStatus.Dead;
            throw new LuaException((LuaStatus)status, ReadError(), "coroutine");
        }

        if (resultCount > 0)
        {
            LuaApi.lua_xmove(_thread, main, resultCount);
        }

        var results = new object[resultCount];
        for (var i = 0; i < resultCount; i++)
        {
            results[i] = _marshal.ReadObject(threadIndex + 1 + i)!;
        }

        Status = status == LuaApi.LuaYield ? LuaCoroutineStatus.Suspended : LuaCoroutineStatus.Dead;
        return results;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _marshal.ReleaseReference(_reference);
    }

    private string ReadError()
    {
        var pointer = LuaApi.lua_tolstring(_thread, -1, out var length);
        return pointer == IntPtr.Zero
            ? "coroutine failed with a non-string error"
            : NativeMarshal.PtrToStringUTF8(pointer, checked((int)length)) ?? "coroutine failed";
    }
}
