using LuaLoom;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void Profile_CollectsSamples()
    {
        using var env = new LuaEnv();

        env.StartProfile();
        env.DoString("local total = 0; for i = 1, 1000 do total = total + i end; return total", "bench");
        var report = env.StopProfile(ProfileSort.Called);

        Assert.Contains("CALLS", report, StringComparison.Ordinal);
        Assert.Contains("TOTAL(s)", report, StringComparison.Ordinal);
        Assert.Contains("bench", report, StringComparison.Ordinal);
        Assert.DoesNotContain("(no samples collected)", report, StringComparison.Ordinal);
    }

    [Fact]
    public void StartProfile_Twice_Throws()
    {
        using var env = new LuaEnv();

        env.StartProfile();

        try
        {
            Assert.Throws<InvalidOperationException>(env.StartProfile);
        }
        finally
        {
            env.StopProfile();
        }
    }

    [Fact]
    public void StopProfile_WithoutStart_Throws()
    {
        using var env = new LuaEnv();

        Assert.Throws<InvalidOperationException>(() => env.StopProfile());
    }

    [Fact]
    public void MemorySnapshot_ReturnsTable()
    {
        using var env = new LuaEnv();

        var report = env.MemorySnapshot(5);

        Assert.Contains("ENTRIES", report, StringComparison.Ordinal);
        Assert.Contains("GLOBAL", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostics_AfterDispose_Throw()
    {
        var env = new LuaEnv();
        env.Dispose();

        Assert.Throws<ObjectDisposedException>(env.StartProfile);
        Assert.Throws<ObjectDisposedException>(() => env.MemorySnapshot());
    }
}
