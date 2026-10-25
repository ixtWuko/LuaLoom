using LuaLoom.Native;

namespace LuaLoom;

public sealed partial class LuaEnv
{
    private const int GcSetPause = 6;
    private const int GcSetStepMultiplier = 7;

    /// <summary>GC pause 参数；<c>lua_gc</c> 没有读取入口，getter 先取旧值再立即写回。</summary>
    public int GcPause
    {
        get
        {
            EnsureReady();
            var previous = LuaApi.lua_gc(_state, GcSetPause, 0);
            LuaApi.lua_gc(_state, GcSetPause, previous);
            return previous;
        }
        set
        {
            EnsureReady();
            LuaApi.lua_gc(_state, GcSetPause, value);
        }
    }

    /// <summary>GC step multiplier；读取方式与 <see cref="GcPause"/> 相同。</summary>
    public int GcStepMultiplier
    {
        get
        {
            EnsureReady();
            var previous = LuaApi.lua_gc(_state, GcSetStepMultiplier, 0);
            LuaApi.lua_gc(_state, GcSetStepMultiplier, previous);
            return previous;
        }
        set
        {
            EnsureReady();
            LuaApi.lua_gc(_state, GcSetStepMultiplier, value);
        }
    }
}
