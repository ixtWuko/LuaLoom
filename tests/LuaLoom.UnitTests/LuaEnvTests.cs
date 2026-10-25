using System.Text;
using LuaLoom;
using LuaLoom.Native;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class LuaEnvTests
{
    [Fact]
    public void Constructor_ExposesGlobalAndDefaultSearchPath()
    {
        using var env = new LuaEnv();

        Assert.NotNull(env.Global);
        Assert.Equal("?.lua", env.DoString("return package.path")[0]);
    }

    [Fact]
    public void Constructor_AppliesCustomSearchPaths()
    {
        using var env = new LuaEnv(new LuaEnvOptions { SearchPaths = ["a/?.lua", "b/?.lua"] });

        Assert.Equal("a/?.lua;b/?.lua", env.DoString("return package.path")[0]);
    }

    [Fact]
    public void DoString_ReturnsValues()
    {
        using var env = new LuaEnv();

        var results = env.DoString("return 1, 'a', true");

        Assert.Equal(3, results.Length);
        Assert.Equal(1L, results[0]);
        Assert.Equal("a", results[1]);
        Assert.Equal(true, results[2]);
    }

    [Fact]
    public void DoString_WithNoReturn_ReturnsEmptyArray()
    {
        using var env = new LuaEnv();

        var results = env.DoString("local x = 1");

        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public void CustomLoader_IsUsedByRequire()
    {
        using var env = new LuaEnv();
        env.AddLoader((ref string name) =>
            name == "answer" ? Encoding.UTF8.GetBytes("return 42") : null);

        var results = env.DoString("return require('answer')");

        Assert.Equal(42L, results[0]);
    }

    [Fact]
    public void LoaderChain_FallsThroughToNextLoader()
    {
        using var env = new LuaEnv();
        var calls = new List<string>();

        env.AddLoader((ref string name) =>
        {
            calls.Add("first:" + name);
            return null;
        });
        env.AddLoader((ref string name) =>
        {
            calls.Add("second:" + name);
            return Encoding.UTF8.GetBytes("return 'ok'");
        });

        var results = env.DoString("return require('m')");

        Assert.Equal("ok", results[0]);
        Assert.Equal(["first:m", "second:m"], calls);
    }

    [Fact]
    public void MissingModule_ThrowsWithModuleName()
    {
        using var env = new LuaEnv();

        var exception = Assert.Throws<LuaException>(() => env.DoString("require('nope')"));

        Assert.Equal(LuaStatus.ErrRun, exception.Status);
        Assert.Contains("nope", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeError_IncludesTraceback()
    {
        using var env = new LuaEnv();

        var exception = Assert.Throws<LuaException>(() => env.DoString("error('boom')", "script"));

        Assert.Equal(LuaStatus.ErrRun, exception.Status);
        Assert.Contains("boom", exception.Message, StringComparison.Ordinal);
        Assert.Contains("stack traceback:", exception.Traceback, StringComparison.Ordinal);
    }

    [Fact]
    public void SyntaxError_ReportsChunkAndLine()
    {
        using var env = new LuaEnv();

        var exception = Assert.Throws<LuaException>(() => env.DoString("local = 1", "myscript"));

        Assert.Equal(LuaStatus.ErrSyntax, exception.Status);
        Assert.Equal("myscript", exception.ChunkName);
        Assert.Equal(1, exception.Line);
    }

    [Fact]
    public void LoadString_ReturnsCallableFunction()
    {
        using var env = new LuaEnv();

        using var function = env.LoadString("return 3 + 4");
        var results = function.Call();

        Assert.Equal(7L, results[0]);
    }

    [Fact]
    public void LuaFunctionCall_RuntimeError_IncludesTraceback()
    {
        using var env = new LuaEnv();
        using var function = env.LoadString("error('call boom')", "call_script");

        var exception = Assert.Throws<LuaException>(() => function.Call());

        Assert.Contains("call boom", exception.Message, StringComparison.Ordinal);
        Assert.Contains("stack traceback:", exception.Traceback, StringComparison.Ordinal);
    }

    [Fact]
    public void CrossThreadCall_Throws()
    {
        using var env = new LuaEnv();
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                env.DoString("return 1");
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        });

        thread.Start();
        thread.Join();

        Assert.IsType<InvalidOperationException>(captured);
    }

    [Fact]
    public void Dispose_IsIdempotentAndBlocksFurtherCalls()
    {
        var env = new LuaEnv();
        env.Dispose();
        env.Dispose();

        Assert.Throws<ObjectDisposedException>(() => env.DoString("return 1"));
    }

    [Fact]
    public void UsingHandleAfterDispose_ThrowsAndDisposeIsSafe()
    {
        var env = new LuaEnv();
        var table = env.NewTable();

        env.Dispose();

        Assert.Throws<ObjectDisposedException>(() => table.Get<int>("x"));
        table.Dispose();
    }

    [Fact]
    public void HandleAccessFromOtherThread_Throws()
    {
        using var env = new LuaEnv();
        using var table = env.NewTable();
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                table.Set("x", 1);
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        });

        thread.Start();
        thread.Join();

        Assert.IsType<InvalidOperationException>(captured);
    }

    [Fact]
    public void CheckThreadAffinityDisabled_AllowsWorkerThreadCall()
    {
        using var env = new LuaEnv(new LuaEnvOptions { CheckThreadAffinity = false });
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                env.DoString("return 1");
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        });
        thread.Start();
        thread.Join();

        Assert.Null(captured);
    }

    [Fact]
    public void MemoryBytes_IsPositive()
    {
        using var env = new LuaEnv();

        Assert.True(env.MemoryBytes > 0);
    }

    [Fact]
    public void CollectGarbage_AllModes_AreAccepted()
    {
        using var env = new LuaEnv();
        env.DoString("local t = {}; for i = 1, 100 do t[i] = i end");

        foreach (var mode in Enum.GetValues<GcMode>())
        {
            env.CollectGarbage(mode);
        }
    }

    [Fact]
    public void GcParameters_AfterDispose_Throw()
    {
        var env = new LuaEnv();
        env.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = env.GcPause);
        Assert.Throws<ObjectDisposedException>(() => env.GcPause = 100);
        Assert.Throws<ObjectDisposedException>(() => _ = env.GcStepMultiplier);
        Assert.Throws<ObjectDisposedException>(() => env.GcStepMultiplier = 100);
    }

    [Fact]
    public void GcParameters_CanBeReadAndWritten()
    {
        using var env = new LuaEnv();

        var originalPause = env.GcPause;
        var originalStep = env.GcStepMultiplier;
        try
        {
            env.GcPause = 148;
            env.GcStepMultiplier = 132;

            Assert.Equal(148, env.GcPause);
            Assert.Equal(132, env.GcStepMultiplier);
        }
        finally
        {
            env.GcPause = originalPause;
            env.GcStepMultiplier = originalStep;
        }
    }

    [Fact]
    public void GcParameters_ReadingTwice_KeepsValues()
    {
        using var env = new LuaEnv();
        // 5.4 内部按 4 对齐，取 4 的倍数避免取整干扰
        env.GcPause = 248;
        env.GcStepMultiplier = 148;

        Assert.Equal(248, env.GcPause);
        Assert.Equal(248, env.GcPause);
        Assert.Equal(148, env.GcStepMultiplier);
        Assert.Equal(148, env.GcStepMultiplier);
    }
}
