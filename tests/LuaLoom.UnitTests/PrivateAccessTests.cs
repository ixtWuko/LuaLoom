using LuaLoom;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class PrivateAccessTests
{
    [Fact]
    public void PrivateAccessible_EnablesReflectionAccess()
    {
        using var env = new LuaEnv();

        var results = env.DoString("""
            local p = CS.LuaLoom.UnitTests.Sample.PrivateMembers()
            local before = p.Secret
            xlua.private_accessible(CS.LuaLoom.UnitTests.Sample.PrivateMembers)
            return before, p.Secret, p:Secret(), p._secret
            """);

        Assert.Null(results[0]);
        Assert.NotNull(results[1]);
        Assert.Equal(7L, results[2]);
        Assert.Equal(7L, results[3]);
    }
}
