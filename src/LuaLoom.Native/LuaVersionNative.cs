using System.Runtime.InteropServices;

namespace LuaLoom.Native;

/// <summary>Lua 5.3 独有的原生入口。</summary>
internal static class Lua53Native
{
    [DllImport(LuaApi.Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr lua_newuserdata(IntPtr L, nuint size);

    [DllImport(LuaApi.Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_resume(IntPtr L, IntPtr from, int argumentCount);
}

/// <summary>Lua 5.4 与 5.5 共用的原生入口。</summary>
internal static class Lua54Native
{
    [DllImport(LuaApi.Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr lua_newuserdatauv(IntPtr L, nuint size, int userValues);

    [DllImport(LuaApi.Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_resume(IntPtr L, IntPtr from, int argumentCount, out int resultCount);
}
