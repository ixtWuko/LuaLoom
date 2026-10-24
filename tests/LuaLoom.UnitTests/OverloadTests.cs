using LuaLoom;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class OverloadTests
{
    private const string Overloads = "CS.LuaLoom.UnitTests.Sample.Overloads";

    [Fact]
    public void StringArgument_PrefersStringOverload()
    {
        using var env = new LuaEnv();

        var results = env.DoString($"return {Overloads}.Pick('x')");

        Assert.Equal("string", results[0]);
    }

    [Fact]
    public void IntegerArgument_PrefersLongOverload()
    {
        using var env = new LuaEnv();

        var results = env.DoString($"return {Overloads}.Pick(1)");

        Assert.Equal("long", results[0]);
    }

    [Fact]
    public void OptionalParameter_UsesDefaultValue()
    {
        using var env = new LuaEnv();

        var results = env.DoString($"return {Overloads}.Optional()");

        Assert.Equal(7L, results[0]);
    }
}
