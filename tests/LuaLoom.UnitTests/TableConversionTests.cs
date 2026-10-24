using LuaLoom;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class TableConversionTests
{
    [Fact]
    public void LuaTable_IsConvertedToMethodParameter()
    {
        using var env = new LuaEnv();

        var results = env.DoString("return CS.LuaLoom.UnitTests.Sample.PointMath.Sum({ X = 1, Y = 2 })");

        Assert.Equal(3L, results[0]);
    }
}
