using System.Runtime.InteropServices;

namespace LuaLoom.Native;

/// <summary>
/// Lua C API 的一对一映射。全部栈操作只能在这里与 marshal 层出现。
/// </summary>
internal static class LuaApi
{
    internal const string Library = "lualoom";

    /// <summary>构建时选定的版本画像；版本差异只在 profile 内处理。</summary>
    internal static readonly LuaRuntimeProfile Profile =
        LuaRuntimeProfile.Select(LuaRuntimeProfile.ReadFromAssembly());

    // lua.h 常量
    internal const int LuaOk = 0;
    internal const int LuaYield = 1;
    internal const int LuaErrRun = 2;
    internal const int LuaErrSyntax = 3;
    internal const int LuaErrMem = 4;
    internal const int LuaErrErr = 5;
    internal const int LuaMultRet = -1;

    // lua.h 类型
    internal const int LuaTNil = 0;
    internal const int LuaTBoolean = 1;
    internal const int LuaTNumber = 3;
    internal const int LuaTString = 4;
    internal const int LuaTTable = 5;
    internal const int LuaTFunction = 6;
    internal const int LuaTUserData = 7;

    // lua.h GC 选项
    internal const int LuaGcStop = 0;
    internal const int LuaGcRestart = 1;
    internal const int LuaGcCollect = 2;
    internal const int LuaGcCount = 3;
    internal const int LuaGcCountB = 4;
    internal const int LuaGcStep = 5;

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr luaL_newstate();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void luaL_openlibs(IntPtr L);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void lua_close(IntPtr L);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_gettop(IntPtr L);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void lua_settop(IntPtr L, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_checkstack(IntPtr L, int extra);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_absindex(IntPtr L, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_type(IntPtr L, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr lua_typename(IntPtr L, int type);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void lua_pushnil(IntPtr L);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void lua_pushboolean(IntPtr L, int value);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void lua_pushinteger(IntPtr L, long value);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void lua_pushnumber(IntPtr L, double value);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr lua_pushstring(IntPtr L, string? value);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr lua_pushlstring(IntPtr L, byte[] value, nuint length);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void lua_pushcclosure(IntPtr L, IntPtr function, int upvalueCount);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr lua_tolstring(IntPtr L, int index, out nuint length);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern long lua_tointegerx(IntPtr L, int index, out int isNumber);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern double lua_tonumberx(IntPtr L, int index, out int isNumber);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_toboolean(IntPtr L, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr lua_touserdata(IntPtr L, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void lua_createtable(IntPtr L, int arraySize, int recordSize);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_getfield(IntPtr L, int index, string key);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void lua_setfield(IntPtr L, int index, string key);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_rawgeti(IntPtr L, int index, long key);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void lua_rawseti(IntPtr L, int index, long key);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_getglobal(IntPtr L, string name);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void lua_setglobal(IntPtr L, string name);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_next(IntPtr L, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_setmetatable(IntPtr L, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_getmetatable(IntPtr L, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int luaL_loadbufferx(IntPtr L, byte[] buffer, nuint size, string chunkName, string? mode);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void luaL_traceback(IntPtr L, IntPtr levelState, string? message, int level);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_pcallk(
        IntPtr L,
        int argCount,
        int resultCount,
        int errorFunction,
        IntPtr context,
        IntPtr continuation);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_error(IntPtr L);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int luaL_ref(IntPtr L, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void luaL_unref(IntPtr L, int index, int reference);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_gc(IntPtr L, int what, int data);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int luaL_newmetatable(IntPtr L, string name);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void luaL_setmetatable(IntPtr L, string name);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_gettable(IntPtr L, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void lua_settable(IntPtr L, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void lua_pushvalue(IntPtr L, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void lua_rotate(IntPtr L, int index, int count);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint lua_rawlen(IntPtr L, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int lua_rawequal(IntPtr L, int index1, int index2);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr lua_newthread(IntPtr L);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void lua_xmove(IntPtr from, IntPtr to, int count);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr lua_topointer(IntPtr L, int index);
}
