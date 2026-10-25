using LuaLoom;
using LuaLoom.Native;
using Xunit;

namespace LuaLoom.UnitTests;

/// <summary>
/// 表访问必须走受保护的调用：Lua 元方法报错、键非法时转成 <see cref="LuaException"/>，
/// 而不是以未保护错误终止进程。
/// </summary>
public sealed class TableAccessErrorTests
{
    [Fact]
    public void IndexMetamethodError_ThrowsLuaExceptionAndKeepsEnvironmentUsable()
    {
        using var env = new LuaEnv();
        env.DoString("t = setmetatable({}, { __index = function() error('boom') end })");
        using var table = env.Global.Get<LuaTable>("t");

        var exception = Assert.Throws<LuaException>(() => table.Get<int>("x"));

        Assert.Contains("boom", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2L, env.DoString("return 1 + 1")[0]);
    }

    [Fact]
    public void NewIndexMetamethodError_ThrowsLuaException()
    {
        using var env = new LuaEnv();
        env.DoString("t = setmetatable({}, { __newindex = function() error('nope') end })");
        using var table = env.Global.Get<LuaTable>("t");

        var exception = Assert.Throws<LuaException>(() => table.Set("key", 1));

        Assert.Contains("nope", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NilKey_ThrowsLuaExceptionInsteadOfAborting()
    {
        using var env = new LuaEnv();
        using var table = env.NewTable();

        Assert.Throws<LuaException>(() => table.Set<string?, int>(null, 1));
    }

    [Fact]
    public void GetInPath_ErroringMetamethod_ThrowsLuaException()
    {
        using var env = new LuaEnv();
        env.DoString("t = setmetatable({}, { __index = function() error('path') end })");
        using var table = env.Global.Get<LuaTable>("t");

        Assert.Throws<LuaException>(() => table.GetInPath<int>("a.b"));
    }

    [Fact]
    public void ForeignUserdata_IsRejectedInsteadOfReadAsObject()
    {
        using var env = new LuaEnv();

        // io 库的 userdata 带自己的元表，不能被当作 LuaLoom 的对象
        var exception = Assert.Throws<LuaConversionException>(() => env.DoString("return io.stdout"));

        Assert.Contains("userdata", exception.Actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadingTableFromString_ThrowsConversionException()
    {
        using var env = new LuaEnv();

        // _VERSION 是字符串，不能被当成 LuaTable 句柄
        Assert.Throws<LuaConversionException>(() => env.Global.Get<LuaTable>("_VERSION"));
    }
}
