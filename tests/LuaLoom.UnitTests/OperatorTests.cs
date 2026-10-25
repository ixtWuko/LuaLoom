using LuaLoom;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class OperatorTests
{
    private const string Money = "CS.LuaLoom.UnitTests.Sample.Money";

    [Fact]
    public void Addition_InvokesOperator()
    {
        using var env = new LuaEnv();

        var result = env.DoString($"local a = {Money}(1); local b = {Money}(2); return (a + b).Amount");

        Assert.Equal(3L, result[0]);
    }

    [Fact]
    public void SubtractionAndUnaryMinus_InvokeOperators()
    {
        using var env = new LuaEnv();

        var result = env.DoString($$"""
            local a = {{Money}}(5)
            local b = {{Money}}(2)
            return (a - b).Amount, (-a).Amount
            """);

        Assert.Equal(3L, result[0]);
        Assert.Equal(-5L, result[1]);
    }

    [Fact]
    public void Equality_UsesOperator()
    {
        using var env = new LuaEnv();

        var result = env.DoString($$"""
            local a = {{Money}}(1)
            local b = {{Money}}(1)
            local c = {{Money}}(2)
            return a == b, a == c
            """);

        Assert.Equal(true, result[0]);
        Assert.Equal(false, result[1]);
    }

    [Fact]
    public void Comparison_UsesOperators()
    {
        using var env = new LuaEnv();

        var result = env.DoString($$"""
            local a = {{Money}}(1)
            local b = {{Money}}(2)
            return a < b, b < a
            """);

        Assert.Equal(true, result[0]);
        Assert.Equal(false, result[1]);
    }

    [Fact]
    public void MultiplicationDivisionModuloAndLessOrEqual_InvokeOperators()
    {
        using var env = new LuaEnv();

        var result = env.DoString("""
            local a = CS.LuaLoom.UnitTests.Sample.MathValue(7)
            local b = CS.LuaLoom.UnitTests.Sample.MathValue(2)
            return (a * b).Value, (a / b).Value, (a % b).Value, a <= b, a <= a
            """);

        Assert.Equal(14L, result[0]);
        Assert.Equal(3L, result[1]);
        Assert.Equal(1L, result[2]);
        Assert.Equal(false, result[3]);
        Assert.Equal(true, result[4]);
    }

    [Fact]
    public void Indexer_ReadsAndWrites()
    {
        using var env = new LuaEnv();

        var result = env.DoString("""
            local bag = CS.LuaLoom.UnitTests.Sample.Bag()
            bag[1] = 5
            return bag[1], bag[2]
            """);

        Assert.Equal(5L, result[0]);
        Assert.Equal(0L, result[1]);
    }
}
