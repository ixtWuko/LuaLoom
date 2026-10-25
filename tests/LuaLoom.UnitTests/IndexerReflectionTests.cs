using LuaLoom;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class IndexerReflectionTests
{
    [Fact]
    public void MultipleIndexers_SelectByKeyType()
    {
        using var env = new LuaEnv();

        var results = env.DoString("""
            local dual = CS.LuaLoom.UnitTests.Sample.DualIndexer()
            dual[1] = 'int'
            dual[2.5] = 'object'
            return dual[1], dual[2.5]
            """);

        Assert.Equal("int", results[0]);
        Assert.Equal("object", results[1]);
    }
}
