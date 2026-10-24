using LuaLoom;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class CompatApiTests
{
    [Fact]
    public void TypeOf_ReturnsTypeObject()
    {
        using var env = new LuaEnv();

        var results = env.DoString("return typeof(CS.LuaLoom.UnitTests.Sample.Player).Name");

        Assert.Equal("Player", results[0]);
    }

    [Fact]
    public void Cast_ReturnsSameObjectView()
    {
        using var env = new LuaEnv();

        var results = env.DoString("""
            local player = CS.LuaLoom.UnitTests.Sample.Player(7)
            local viewed = cast(player, typeof(CS.LuaLoom.UnitTests.Sample.Player))
            return viewed.Hp
            """);

        Assert.Equal(7L, results[0]);
    }

    [Fact]
    public void UInt64_Helpers_PreserveUnsignedSemantics()
    {
        using var env = new LuaEnv();

        var results = env.DoString("""
            local max = uint64.parse('18446744073709551615')
            local ten = uint64.parse('10')
            local three = uint64.parse('3')
            return uint64.tostring(max),
                   uint64.compare(ten, three),
                   uint64.tostring(uint64.divide(ten, three)),
                   uint64.tostring(uint64.remainder(ten, three))
            """);

        Assert.Equal("18446744073709551615", results[0]);
        Assert.Equal(1L, results[1]);
        Assert.Equal("3", results[2]);
        Assert.Equal("1", results[3]);
    }

    [Fact]
    public void XluaGetGenericMethod_WorksAsAlias()
    {
        using var env = new LuaEnv();

        var results = env.DoString("""
            local box = CS.LuaLoom.UnitTests.Sample.GenericBox()
            local generic = xlua.get_generic_method(
                CS.LuaLoom.UnitTests.Sample.GenericBox,
                'Show')
            local show = generic(CS.System.Int32)
            return show(box, 5)
            """);

        Assert.Equal("Int32=5", results[0]);
    }
}
