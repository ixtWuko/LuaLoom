using LuaLoom;
using LuaLoom.UnitTests.Sample;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class ArrayCastTests
{
    [Fact]
    public void RegisteredCast_TakesPrecedenceOverReflection()
    {
        using var env = new LuaEnv();
        env.RegisterCast(typeof(Player), static _ => new Player(123));

        using var table = (LuaTable)env.DoString("return { Hp = 1 }")[0]!;
        var player = table.Cast<Player>();

        Assert.Equal(123, player.Hp);
    }

    [Fact]
    public void Cast_MissingKeysKeepDefaults()
    {
        using var env = new LuaEnv();
        using var table = (LuaTable)env.DoString("return { Hp = 5 }")[0]!;

        var config = table.Cast<Config>();

        Assert.Equal(5, config.Hp);
        Assert.Null(config.Name);
        Assert.Equal(0f, config.Pos.X);
        Assert.Equal(0f, config.Pos.Y);
    }

    [Fact]
    public void Cast_MultiDimensionalArrays()
    {
        using var env = new LuaEnv();
        using var table = (LuaTable)env.DoString(
            "return { { 1, 2 }, { 3, 4 } }")[0]!;

        var matrix = table.Cast<int[,]>();
        Assert.Equal(2, matrix.GetLength(0));
        Assert.Equal(2, matrix.GetLength(1));
        Assert.Equal(4, matrix[1, 1]);

        using var cubeTable = (LuaTable)env.DoString(
            "return { { { 1, 2 }, { 3, 4 } }, { { 5, 6 }, { 7, 8 } } }")[0]!;
        var cube = cubeTable.Cast<int[,,]>();
        Assert.Equal(8, cube[1, 1, 1]);
    }

    [Fact]
    public void Array_ClassElementsRoundTrip()
    {
        using var env = new LuaEnv();

        var results = env.DoString("""
            local players = {
                CS.LuaLoom.UnitTests.Sample.Player(2),
                CS.LuaLoom.UnitTests.Sample.Player(3)
            }
            local echoed = CS.LuaLoom.UnitTests.Sample.ArraySurface.EchoPlayers(players)
            return echoed[1].Hp, echoed[2].Hp
            """);

        Assert.Equal(2L, results[0]);
        Assert.Equal(3L, results[1]);
    }

    [Fact]
    public void Array_StringEnumAndObjectElements()
    {
        using var env = new LuaEnv();

        var results = env.DoString("""
            local lengths = CS.LuaLoom.UnitTests.Sample.ArraySurface.SumLengths({ 'ab', 'c' })
            local colors = CS.LuaLoom.UnitTests.Sample.ArraySurface.EchoColors({ 1, 2 })
            local objects = CS.LuaLoom.UnitTests.Sample.ArraySurface.EchoObjects({ 7, 'x', true })
            return lengths, colors[1], colors[2], objects[1], objects[2], objects[3]
            """);

        Assert.Equal(3L, results[0]);
        Assert.Equal(1L, results[1]);
        Assert.Equal(2L, results[2]);
        Assert.Equal(7L, results[3]);
        Assert.Equal("x", results[4]);
        Assert.Equal(true, results[5]);
    }

    [Fact]
    public void Array_RectangularAndCubeRoundTrip()
    {
        using var env = new LuaEnv();

        var results = env.DoString("""
            local matrix = CS.LuaLoom.UnitTests.Sample.ArraySurface.EchoMatrix({ { 1, 2 }, { 3, 4 } })
            local cube = CS.LuaLoom.UnitTests.Sample.ArraySurface.EchoCube(
                { { { 1, 2 }, { 3, 4 } }, { { 5, 6 }, { 7, 8 } } })
            return matrix[2][2], cube[2][2][2]
            """);

        Assert.Equal(4L, results[0]);
        Assert.Equal(8L, results[1]);
    }

    [Fact]
    public void Array_NullAndEmptyReturn()
    {
        using var env = new LuaEnv();

        var results = env.DoString("""
            local empty = CS.LuaLoom.UnitTests.Sample.ArraySurface.Empty()
            local nullArray = CS.LuaLoom.UnitTests.Sample.ArraySurface.NullArray()
            return #empty, nullArray == nil
            """);

        Assert.Equal(0L, results[0]);
        Assert.Equal(true, results[1]);
    }

    [Fact]
    public void Array_NullableParameterAcceptsNil()
    {
        using var env = new LuaEnv();

        var results = env.DoString("""
            return CS.LuaLoom.UnitTests.Sample.ArraySurface.SumNullable(nil)
            """);

        Assert.Equal(-1L, results[0]);
    }
}
