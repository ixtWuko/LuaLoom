using LuaLoom;
using LuaLoom.Native;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class GenericMethodTests
{
    [Fact]
    public void StaticGenericMethod_IsInvoked()
    {
        using var env = new LuaEnv();

        var result = env.DoString("""
            local describe = lua_loom_generic(
                CS.LuaLoom.UnitTests.Sample.Generics, 'Describe', CS.System.Int32)
            return describe(7)
            """);

        Assert.Equal("Int32:7", result[0]);
    }

    [Fact]
    public void InstanceGenericMethod_IsInvoked()
    {
        using var env = new LuaEnv();

        var result = env.DoString("""
            local show = lua_loom_generic(
                CS.LuaLoom.UnitTests.Sample.GenericBox, 'Show', CS.System.String)
            local box = CS.LuaLoom.UnitTests.Sample.GenericBox()
            return show(box, 'x')
            """);

        Assert.Equal("String=x", result[0]);
    }

    [Fact]
    public void MissingTypeArguments_Throws()
    {
        using var env = new LuaEnv();

        var exception = Assert.Throws<LuaException>(
            () => env.DoString("lua_loom_generic(CS.LuaLoom.UnitTests.Sample.Generics, 'Describe')"));

        Assert.Contains("at least one type argument", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownGenericMethod_Throws()
    {
        using var env = new LuaEnv();

        var exception = Assert.Throws<LuaException>(() => env.DoString("""
            local f = lua_loom_generic(CS.LuaLoom.UnitTests.Sample.Player, 'Nope', CS.System.Int32)
            return f()
            """));

        Assert.Contains("No generic method", exception.Message, StringComparison.Ordinal);
    }
}
