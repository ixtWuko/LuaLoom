using System.Collections.Concurrent;
using LuaLoom.Native;

namespace LuaLoom.Internal;

/// <summary>Lua 状态指针到 marshal 的边界索引，供原生回调反查所属环境。</summary>
internal static class LuaMarshalRegistry
{
    private static readonly ConcurrentDictionary<IntPtr, LuaMarshal> Marshals = new();

    public static void Register(IntPtr state, LuaMarshal marshal) => Marshals[state] = marshal;

    public static void Unregister(IntPtr state) => Marshals.TryRemove(state, out _);

    public static bool TryGet(IntPtr state, out LuaMarshal marshal) => Marshals.TryGetValue(state, out marshal!);

    public static LuaMarshal Get(IntPtr state)
    {
        if (!Marshals.TryGetValue(state, out var marshal))
        {
            throw new InvalidOperationException("No LuaLoom environment is registered for this Lua state.");
        }

        return marshal;
    }
}
