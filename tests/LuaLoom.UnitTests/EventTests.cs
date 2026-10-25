using LuaLoom;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class EventTests
{
    private const string Emitter = "CS.LuaLoom.UnitTests.Sample.Emitter";

    [Fact]
    public void Subscribe_ReceivesEventArguments()
    {
        using var env = new LuaEnv();

        var result = env.DoString($$"""
            local emitter = {{Emitter}}()
            local box = { value = 0 }
            emitter:Changed('+', function(v) box.value = v end)
            emitter:Raise(7)
            return box.value
            """);

        Assert.Equal(7L, result[0]);
    }

    [Fact]
    public void Unsubscribe_StopsReceiving()
    {
        using var env = new LuaEnv();

        var result = env.DoString($$"""
            local emitter = {{Emitter}}()
            local box = { total = 0 }
            local fn = function(v) box.total = box.total + v end
            emitter:Changed('+', fn)
            emitter:Raise(1)
            emitter:Changed('-', fn)
            emitter:Raise(5)
            return box.total
            """);

        Assert.Equal(1L, result[0]);
    }

    [Fact]
    public void DuplicateSubscribe_ReplacesPreviousHandler()
    {
        using var env = new LuaEnv();

        var result = env.DoString($$"""
            local emitter = {{Emitter}}()
            local count = 0
            local fn = function(value) count = count + 1 end
            emitter:Changed('+', fn)
            emitter:Changed('+', fn)
            emitter:Raise(1)
            return count
            """);

        Assert.Equal(1L, result[0]);
    }

    [Fact]
    public void InvalidOperator_Throws()
    {
        using var env = new LuaEnv();

        var exception = Assert.Throws<Native.LuaException>(() => env.DoString($$"""
            local emitter = {{Emitter}}()
            emitter:Changed('?', function() end)
            """));

        Assert.Contains("'+' or '-'", exception.Message, StringComparison.Ordinal);
    }
}
