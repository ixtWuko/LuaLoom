using LuaLoom;
using LuaLoom.Native;
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

    [Theory]
    [InlineData("1", "int")]
    [InlineData("-2147483648", "int")]
    [InlineData("2147483647", "int")]
    [InlineData("2147483648", "long")]
    [InlineData("-2147483649", "long")]
    [InlineData("4000000000", "long")]
    public void IntegerArgument_UsesValueRange(string literal, string expected)
    {
        using var env = new LuaEnv();

        var results = env.DoString($"return {Overloads}.Pick({literal})");

        Assert.Equal(expected, results[0]);
    }

    [Fact]
    public void NilArgument_DoesNotMatchValueTypeParameter()
    {
        using var env = new LuaEnv();

        Assert.Throws<LuaException>(() => env.DoString($"return {Overloads}.Optional(nil)"));
    }

    [Fact]
    public void OptionalParameter_UsesDefaultValue()
    {
        using var env = new LuaEnv();

        var results = env.DoString($"return {Overloads}.Optional()");

        Assert.Equal(7L, results[0]);
    }
}
