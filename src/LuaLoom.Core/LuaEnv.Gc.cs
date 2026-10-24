using LuaLoom.Native;

namespace LuaLoom;

public sealed partial class LuaEnv
{
    private const int GcSetPause = 6;
    private const int GcSetStepMultiplier = 7;

    /// <summary>GC pause 参数；读写都映射到 <c>LUA_GCSETPAUSE</c>。</summary>
    public int GcPause
    {
        get
        {
            EnsureReady();
            return LuaApi.lua_gc(_state, GcSetPause, 0);
        }
        set
        {
            EnsureReady();
            LuaApi.lua_gc(_state, GcSetPause, value);
        }
    }

    /// <summary>GC step multiplier；读写都映射到 <c>LUA_GCSETSTEPMUL</c>。</summary>
    public int GcStepMultiplier
    {
        get
        {
            EnsureReady();
            return LuaApi.lua_gc(_state, GcSetStepMultiplier, 0);
        }
        set
        {
            EnsureReady();
            LuaApi.lua_gc(_state, GcSetStepMultiplier, value);
        }
    }
}
