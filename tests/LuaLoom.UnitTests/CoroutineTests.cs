using LuaLoom;
using LuaLoom.Native;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class CoroutineTests
{
    [Fact]
    public void Resume_AdvancesCoroutine()
    {
        using var env = new LuaEnv();
        using var function = (LuaFunction)env.DoString("""
            return function(a)
                local b = coroutine.yield(a + 1)
                return b * 2
            end
            """)[0]!;
        using var coroutine = function.CreateCoroutine();

        var first = coroutine.Resume(1);
        Assert.Equal(2L, first[0]);
        Assert.Equal(LuaCoroutineStatus.Suspended, coroutine.Status);

        var second = coroutine.Resume(5);
        Assert.Equal(10L, second[0]);
        Assert.Equal(LuaCoroutineStatus.Dead, coroutine.Status);
    }

    [Fact]
    public void Resume_AfterFinish_Throws()
    {
        using var env = new LuaEnv();
        using var function = (LuaFunction)env.DoString("return function() return 1 end")[0]!;
        using var coroutine = function.CreateCoroutine();

        coroutine.Resume();

        Assert.Throws<InvalidOperationException>(() => coroutine.Resume());
    }

    [Fact]
    public void CoroutineError_ThrowsAndFinishes()
    {
        using var env = new LuaEnv();
        using var function = (LuaFunction)env.DoString("return function() error('coroutine boom') end")[0]!;
        using var coroutine = function.CreateCoroutine();

        var exception = Assert.Throws<LuaException>(() => coroutine.Resume());

        Assert.Contains("coroutine boom", exception.Message, StringComparison.Ordinal);
        Assert.Equal(LuaCoroutineStatus.Dead, coroutine.Status);
    }

    [Fact]
    public void DisposedCoroutine_Throws()
    {
        using var env = new LuaEnv();
        using var function = (LuaFunction)env.DoString("return function() end")[0]!;
        var coroutine = function.CreateCoroutine();
        coroutine.Dispose();
        coroutine.Dispose();

        Assert.Throws<ObjectDisposedException>(() => coroutine.Resume());
    }
}

public sealed class MultiEnvironmentTests
{
    [Fact]
    public void Environments_HaveIndependentGlobals()
    {
        using var first = new LuaEnv();
        using var second = new LuaEnv();

        first.DoString("value = 1");
        second.DoString("value = 2");

        Assert.Equal(1L, first.DoString("return value")[0]);
        Assert.Equal(2L, second.DoString("return value")[0]);
    }

    [Fact]
    public void CrossEnvironmentHandle_IsRejected()
    {
        using var first = new LuaEnv();
        using var second = new LuaEnv();
        using var table = first.NewTable();
        table.Set("x", 1);

        var exception = Assert.Throws<InvalidOperationException>(() => second.Global.Set("stolen", table));

        Assert.Contains("different LuaEnv", exception.Message, StringComparison.Ordinal);
    }
}
