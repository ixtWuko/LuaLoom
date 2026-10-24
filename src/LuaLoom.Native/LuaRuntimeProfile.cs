using System.Reflection;
using System.Runtime.InteropServices;

namespace LuaLoom.Native;

/// <summary>构建时写入的 Lua 版本，供 <see cref="LuaRuntimeProfile"/> 选择画像。</summary>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class LuaVersionAttribute : Attribute
{
    public LuaVersionAttribute(string version) => Version = version;

    public string Version { get; }
}

/// <summary>
/// 版本差异的唯一收敛点。新增 Lua 版本时实现一个 profile，并在选择表里登记。
/// </summary>
internal abstract class LuaRuntimeProfile
{
    public abstract string Version { get; }

    /// <summary>LUA_REGISTRYINDEX 的实际取值。</summary>
    public abstract int RegistryIndex { get; }

    public abstract IntPtr NewUserdata(IntPtr state, nuint size);

    public abstract int Resume(IntPtr state, IntPtr from, int argumentCount, out int resultCount);

    public static LuaRuntimeProfile Select(string version) => version switch
    {
        "53" => Lua53Profile.Instance,
        "54" => Lua54Profile.Instance,
        "55" => Lua55Profile.Instance,
        _ => throw new InvalidOperationException(
            $"Unsupported Lua version '{version}'. Supported: 53, 54, 55."),
    };

    public static string ReadFromAssembly() =>
        typeof(LuaRuntimeProfile).Assembly.GetCustomAttribute<LuaVersionAttribute>()?.Version ?? "54";
}

/// <summary>Lua 5.3：lua_newuserdata + 三参数 lua_resume。</summary>
internal sealed class Lua53Profile : LuaRuntimeProfile
{
    public static readonly Lua53Profile Instance = new();

    private Lua53Profile()
    {
    }

    public override string Version => "53";

    public override int RegistryIndex => -1001000;

    public override IntPtr NewUserdata(IntPtr state, nuint size) =>
        Lua53Native.lua_newuserdata(state, size);

    public override int Resume(IntPtr state, IntPtr from, int argumentCount, out int resultCount)
    {
        var status = Lua53Native.lua_resume(state, from, argumentCount);
        resultCount = LuaApi.lua_gettop(state);
        return status;
    }
}

/// <summary>Lua 5.4：lua_newuserdatauv + 四参数 lua_resume。</summary>
internal sealed class Lua54Profile : LuaRuntimeProfile
{
    public static readonly Lua54Profile Instance = new();

    private Lua54Profile()
    {
    }

    public override string Version => "54";

    public override int RegistryIndex => -1001000;

    public override IntPtr NewUserdata(IntPtr state, nuint size) =>
        Lua54Native.lua_newuserdatauv(state, size, 0);

    public override int Resume(IntPtr state, IntPtr from, int argumentCount, out int resultCount) =>
        Lua54Native.lua_resume(state, from, argumentCount, out resultCount);
}

/// <summary>
/// Lua 5.5：userdata 与 resume 签名同 5.4，注册表伪索引改为 -(INT_MAX/2 + 1000)。
/// 仅预留，尚未用 5.5 实测。
/// </summary>
internal sealed class Lua55Profile : LuaRuntimeProfile
{
    public static readonly Lua55Profile Instance = new();

    private Lua55Profile()
    {
    }

    public override string Version => "55";

    public override int RegistryIndex => -(int.MaxValue / 2 + 1000);

    public override IntPtr NewUserdata(IntPtr state, nuint size) =>
        Lua54Native.lua_newuserdatauv(state, size, 0);

    public override int Resume(IntPtr state, IntPtr from, int argumentCount, out int resultCount) =>
        Lua54Native.lua_resume(state, from, argumentCount, out resultCount);
}
