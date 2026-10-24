using LuaLoom;
using LuaLoom.Native;
using LuaLoom.UnitTests.Sample;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class OutRefParamsTests
{
    private const string Parsing = "CS.LuaLoom.UnitTests.Sample.Parsing";
    private const string Player = "CS.LuaLoom.UnitTests.Sample.Player";

    [Fact]
    public void OutParameter_ReturnsAfterReturnValue()
    {
        using var env = new LuaEnv();

        var results = env.DoString($"return {Parsing}.TryParse('42')");

        Assert.Equal(true, results[0]);
        Assert.Equal(42L, results[1]);
    }

    [Fact]
    public void OutParameter_ReportsFailure()
    {
        using var env = new LuaEnv();

        var results = env.DoString($"return {Parsing}.TryParse('not a number')");

        Assert.Equal(false, results[0]);
        Assert.Equal(0L, results[1]);
    }

    [Fact]
    public void RefParameter_ReturnsUpdatedValue()
    {
        using var env = new LuaEnv();

        var results = env.DoString($"return {Parsing}.Bump(1)");

        Assert.Equal(2L, results[0]);
    }

    [Fact]
    public void ReturnValueComesBeforeRefValue()
    {
        using var env = new LuaEnv();

        var results = env.DoString($"return {Parsing}.AddAndBump(3, 4)");

        Assert.Equal(6L, results[0]);
        Assert.Equal(7L, results[1]);
    }

    [Fact]
    public void ParamsParameter_ExpandsArguments()
    {
        using var env = new LuaEnv();

        var results = env.DoString($"return {Parsing}.Join('-', 1, 2, 3)");

        Assert.Equal("1-2-3", results[0]);
    }

    [Fact]
    public void ParamsParameter_AcceptsNoArguments()
    {
        using var env = new LuaEnv();

        var results = env.DoString($"return {Parsing}.Sum()");

        Assert.Equal(0L, results[0]);
    }

    [Fact]
    public void ParamsConstructor_ExpandsArguments()
    {
        using var env = new LuaEnv();

        var results = env.DoString(
            $"return CS.LuaLoom.UnitTests.Sample.NumberList(1, 2, 3).Total");

        Assert.Equal(6L, results[0]);
    }

    [Fact]
    public void BoundType_OutRefParamsMembersFallBackToReflection()
    {
        using var env = new LuaEnv();
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Player),
            FullName = "LuaLoom.UnitTests.Sample.Player",
            InstanceIndex = EmptyIndex,
        });

        var results = env.DoString($"""
            local p = {Player}(1)
            local text = p:Join('-', 1, 2, 3)
            local value = {Parsing}.Bump(4)
            return text, value
            """);

        Assert.Equal("1-2-3", results[0]);
        Assert.Equal(5L, results[1]);
    }

    private static readonly LuaCSFunction EmptyIndexHandler = EmptyIndex;

    private static int EmptyIndex(IntPtr state) => 0;
}
