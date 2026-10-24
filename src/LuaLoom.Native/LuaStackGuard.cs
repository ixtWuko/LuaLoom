using System.Runtime.InteropServices;

namespace LuaLoom.Native;

/// <summary>
/// 记录进入时的栈顶，离开时恢复。所有跨边界入口都必须套这一层，异常路径也不例外。
/// </summary>
internal readonly ref struct LuaStackGuard
{
    private readonly IntPtr _state;
    private readonly int _top;

    private LuaStackGuard(IntPtr state, int top)
    {
        _state = state;
        _top = top;
    }

    public int Top => _top;

    public static LuaStackGuard Enter(IntPtr state)
    {
        if (state == IntPtr.Zero)
        {
            throw new ArgumentException("Lua state pointer must not be zero.", nameof(state));
        }

        return new LuaStackGuard(state, LuaApi.lua_gettop(state));
    }

    public void Dispose()
    {
        if (_state != IntPtr.Zero)
        {
            LuaApi.lua_settop(_state, _top);
        }
    }
}

/// <summary>lua_CFunction 的托管形式；生成代码与反射兜底都以它为边界。</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int LuaCSFunction(IntPtr state);
