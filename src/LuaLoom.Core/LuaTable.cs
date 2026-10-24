using LuaLoom.Internal;
using LuaLoom.Native;

namespace LuaLoom;

/// <summary>Lua 表句柄。</summary>
public sealed partial class LuaTable : LuaReference
{
    internal LuaTable(LuaMarshal marshal, int id)
        : base(marshal, id)
    {
    }

    public int Length
    {
        get
        {
            EnsureAlive();
            var state = Marshal.State;
            using var guard = LuaStackGuard.Enter(state);
            Marshal.PushReference(Id);
            return checked((int)LuaApi.lua_rawlen(state, -1));
        }
    }

    public T Get<T>(string key)
    {
        EnsureAlive();
        var state = Marshal.State;
        using var guard = LuaStackGuard.Enter(state);
        Marshal.PushReference(Id);
        LuaApi.lua_getfield(state, -1, key);

        if (LuaApi.lua_type(state, -1) == LuaApi.LuaTNil)
        {
            return default!;
        }

        try
        {
            return Marshal.Read<T>(-1);
        }
        catch (LuaConversionException exception)
        {
            throw new LuaConversionException(exception.Expected, exception.Actual, exception.Index, key);
        }
    }

    public TValue Get<TKey, TValue>(TKey key)
    {
        EnsureAlive();
        var state = Marshal.State;
        using var guard = LuaStackGuard.Enter(state);
        Marshal.PushReference(Id);
        Marshal.Push(key);
        LuaApi.lua_gettable(state, -2);

        if (LuaApi.lua_type(state, -1) == LuaApi.LuaTNil)
        {
            return default!;
        }

        return Marshal.Read<TValue>(-1);
    }

    public void Get<TKey, TValue>(TKey key, out TValue value) =>
        value = Get<TKey, TValue>(key);

    public void Set<TKey, TValue>(TKey key, TValue value)
    {
        EnsureAlive();
        var state = Marshal.State;
        using var guard = LuaStackGuard.Enter(state);
        Marshal.PushReference(Id);
        Marshal.Push(key);
        Marshal.Push(value);
        LuaApi.lua_settable(state, -3);
    }

    public T GetInPath<T>(string path)
    {
        EnsureAlive();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var state = Marshal.State;
        using var guard = LuaStackGuard.Enter(state);
        Marshal.PushReference(Id);

        foreach (var segment in path.Split('.'))
        {
            LuaApi.lua_getfield(state, -1, segment);
            if (LuaApi.lua_type(state, -1) == LuaApi.LuaTNil)
            {
                return default!;
            }
        }

        return Marshal.Read<T>(-1);
    }

    internal T GetIndex<T>(int arrayIndex)
    {
        EnsureAlive();
        var state = Marshal.State;
        using var guard = LuaStackGuard.Enter(state);
        Marshal.PushReference(Id);
        LuaApi.lua_rawgeti(state, -1, arrayIndex);
        if (LuaApi.lua_type(state, -1) == LuaApi.LuaTNil)
        {
            return default!;
        }

        return Marshal.Read<T>(-1);
    }

    public void SetInPath<T>(string path, T value)
    {
        EnsureAlive();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var state = Marshal.State;
        using var guard = LuaStackGuard.Enter(state);
        Marshal.PushReference(Id);

        var segments = path.Split('.');
        for (var i = 0; i < segments.Length - 1; i++)
        {
            LuaApi.lua_getfield(state, -1, segments[i]);
            if (LuaApi.lua_type(state, -1) != LuaApi.LuaTTable)
            {
                throw new InvalidOperationException($"Path segment '{segments[i]}' is not a table.");
            }
        }

        Marshal.Push(value);
        LuaApi.lua_setfield(state, -2, segments[^1]);
    }
}
