using LuaLoom;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class LuaTableApiTests
{
    [Fact]
    public void SetInPath_WritesNestedTable()
    {
        using var env = new LuaEnv();
        using var root = env.NewTable();
        using var level1 = env.NewTable();
        using var level2 = env.NewTable();
        root.Set("a", level1);
        level1.Set("b", level2);

        root.SetInPath("a.b.c", 7);

        Assert.Equal(7, root.GetInPath<int>("a.b.c"));
    }

    [Fact]
    public void GetInPath_MissingSegmentReturnsDefault()
    {
        using var env = new LuaEnv();
        using var root = env.NewTable();

        Assert.Equal(0, root.GetInPath<int>("missing.path"));
    }

    [Fact]
    public void SetInPath_NonTableSegmentThrows()
    {
        using var env = new LuaEnv();
        using var root = env.NewTable();
        root.Set("a", 1);

        Assert.Throws<InvalidOperationException>(() => root.SetInPath("a.b", 2));
    }

    [Fact]
    public void Length_TracksArrayPart()
    {
        using var env = new LuaEnv();
        using var table = env.NewTable();
        table.Set(1, "a");
        table.Set(2, "b");

        Assert.Equal(2, table.Length);
    }

    [Fact]
    public void MissingKey_ReturnsDefault()
    {
        using var env = new LuaEnv();
        using var table = env.NewTable();

        Assert.Null(table.Get<string>("missing"));
    }

    [Fact]
    public void IntegerKeys_RoundTrip()
    {
        using var env = new LuaEnv();
        using var table = env.NewTable();

        table.Set(1, "first");
        table.Set(2, "second");

        Assert.Equal("first", table.Get<int, string>(1));
        Assert.Equal("second", table.Get<int, string>(2));
    }
}
