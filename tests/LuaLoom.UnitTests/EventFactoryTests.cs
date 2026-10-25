using LuaLoom;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class EventFactoryTests
{
    private delegate int RefCallback(ref int value);

    [Fact]
    public void NonVoidHandler_WithoutReturn_UsesDefault()
    {
        using var env = new LuaEnv();
        using var function = (LuaFunction)env.DoString("return function(value) end")[0]!;

        var callback = (Func<int, int>)LuaEventDelegateFactory.Create(typeof(Func<int, int>), function);

        Assert.Equal(0, callback(1));
    }

    [Fact]
    public void NonVoidHandler_NilAndValue()
    {
        using var env = new LuaEnv();

        using var nilFunction = (LuaFunction)env.DoString("return function(value) return nil end")[0]!;
        var nilCallback = (Func<int, int>)LuaEventDelegateFactory.Create(typeof(Func<int, int>), nilFunction);
        Assert.Equal(0, nilCallback(1));

        using var valueFunction = (LuaFunction)env.DoString("return function(value) return value + 1 end")[0]!;
        var valueCallback = (Func<int, int>)LuaEventDelegateFactory.Create(typeof(Func<int, int>), valueFunction);
        Assert.Equal(2, valueCallback(1));
    }

    [Fact]
    public void ByRefHandler_ThrowsWithClearMessage()
    {
        using var env = new LuaEnv();
        using var function = (LuaFunction)env.DoString("return function(value) return value end")[0]!;

        var exception = Assert.Throws<ArgumentException>(
            () => LuaEventDelegateFactory.Create(typeof(RefCallback), function));

        Assert.Contains("ref/out", exception.Message, StringComparison.Ordinal);
    }
}
