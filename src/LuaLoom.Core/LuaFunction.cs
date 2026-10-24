using LuaLoom.Internal;
using LuaLoom.Native;

namespace LuaLoom;

/// <summary>Lua 函数句柄。适合低频调用；高频路径应映射到 delegate。</summary>
public sealed class LuaFunction : LuaReference
{
    internal LuaFunction(LuaMarshal marshal, int id)
        : base(marshal, id)
    {
    }

    public object[] Call(params object[] args)
    {
        return Invoke(args, returnTypes: null);
    }

    public object[] Call(object[] args, Type[] returnTypes)
    {
        ArgumentNullException.ThrowIfNull(returnTypes);
        return Invoke(args, returnTypes);
    }

    /// <summary>把该函数包装成协程；每次 Resume 推进一段。</summary>
    public LuaCoroutine CreateCoroutine()
    {
        EnsureAlive();
        return new LuaCoroutine(Marshal, Id);
    }

    private object[] Invoke(object[] args, Type[]? returnTypes)
    {
        EnsureAlive();
        ArgumentNullException.ThrowIfNull(args);

        var state = Marshal.State;
        using var guard = LuaStackGuard.Enter(state);
        var baseTop = LuaApi.lua_gettop(state);
        var errorFunction = Marshal.Environment?.PushErrorHandler() ?? 0;
        var hasErrorHandler = errorFunction != 0;

        Marshal.PushReference(Id);
        foreach (var argument in args)
        {
            Marshal.PushObject(argument);
        }

        var status = LuaApi.lua_pcallk(
            state,
            args.Length,
            LuaApi.LuaMultRet,
            errorFunction,
            context: IntPtr.Zero,
            continuation: IntPtr.Zero);

        if (status != LuaApi.LuaOk)
        {
            throw LuaErrors.Create(state, (LuaStatus)status, "chunk");
        }

        var count = LuaApi.lua_gettop(state) - baseTop - (hasErrorHandler ? 1 : 0);
        var results = new object[count];
        for (var i = 0; i < count; i++)
        {
            var index = baseTop + (hasErrorHandler ? 2 : 1) + i;
            results[i] = returnTypes is null
                ? Marshal.ReadObject(index)!
                : Marshal.Read(returnTypes[i], index)!;
        }

        return results;
    }
}
