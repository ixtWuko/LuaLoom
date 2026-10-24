using System.Runtime.InteropServices;

namespace LuaLoom.Native;

/// <summary>C API 错误码到托管异常的转换，以及受保护的调用封装。</summary>
internal static class LuaErrors
{
    public static LuaException Create(IntPtr state, LuaStatus status, string chunkName)
    {
        var message = ReadTopMessage(state);
        LuaApi.lua_settop(state, LuaApi.lua_gettop(state) - 1);
        var traceback = message.Contains("stack traceback:", StringComparison.Ordinal) ? message : null;
        return new LuaException(status, message, chunkName, traceback);
    }

    public static void PCall(IntPtr state, int argumentCount, int resultCount, string chunkName)
    {
        var status = LuaApi.lua_pcallk(
            state,
            argumentCount,
            resultCount,
            errorFunction: 0,
            context: IntPtr.Zero,
            continuation: IntPtr.Zero);

        if (status != LuaApi.LuaOk)
        {
            throw Create(state, (LuaStatus)status, chunkName);
        }
    }

    private static string ReadTopMessage(IntPtr state)
    {
        var pointer = LuaApi.lua_tolstring(state, -1, out var length);
        if (pointer == IntPtr.Zero || length == 0)
        {
            var typeName = Marshal.PtrToStringUTF8(LuaApi.lua_typename(state, LuaApi.lua_type(state, -1))) ?? "unknown";
            return $"Lua error object is not a string (type: {typeName}).";
        }

        return Marshal.PtrToStringUTF8(pointer, checked((int)length)) ?? string.Empty;
    }
}
