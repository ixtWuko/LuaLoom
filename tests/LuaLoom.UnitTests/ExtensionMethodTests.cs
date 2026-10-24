using LuaLoom;
using LuaLoom.Native;
using LuaLoom.UnitTests.Sample;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class ExtensionMethodTests
{
    private const string PlayerType = "CS.LuaLoom.UnitTests.Sample.Player";

    [Fact]
    public void ExtensionMethod_IsCalledWithInstanceAsFirstArgument()
    {
        using var env = new LuaEnv();
        env.RegisterExtensionType(typeof(PlayerExtensions));

        var results = env.DoString($"""
            local p = {PlayerType}(3)
            return p:Triple(2)
            """);

        Assert.Equal(6L, results[0]);
    }

    [Fact]
    public void ExtensionMethod_FallsBackFromBinding()
    {
        using var env = new LuaEnv();
        env.RegisterExtensionType(typeof(PlayerExtensions));
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Player),
            FullName = "LuaLoom.UnitTests.Sample.Player",
            InstanceIndex = EmptyIndex,
        });

        var results = env.DoString($"""
            local p = {PlayerType}(4)
            return p:Triple(3)
            """);

        Assert.Equal(12L, results[0]);
    }

    private static readonly LuaCSFunction EmptyIndexHandler = EmptyIndex;

    private static int EmptyIndex(IntPtr state) => 0;
}
