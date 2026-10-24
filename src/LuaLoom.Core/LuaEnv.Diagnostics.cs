using System.Diagnostics;
using System.Runtime.InteropServices;
using LuaLoom.Internal;
using LuaLoom.Native;

namespace LuaLoom;

/// <summary>耗时报告排序方式。</summary>
public enum ProfileSort
{
    Total,
    Average,
    Called,
}

public sealed partial class LuaEnv
{
    private static readonly LuaCSFunction ClockCallback = OnClock;
    private static readonly IntPtr ClockPointer =
        System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(ClockCallback);

    private LuaTable? _profiler;
    private LuaTable? _memory;
    private bool _profiling;
    private bool _clockRegistered;

    public void StartProfile()
    {
        EnsureReady();
        if (_profiling)
        {
            throw new InvalidOperationException("Profiling is already active.");
        }

        EnsureClock();
        var function = GetProfilerFunction("start");
        using (function)
        {
            function.Call();
        }

        _profiling = true;
    }

    public string StopProfile(ProfileSort sort = ProfileSort.Total)
    {
        EnsureReady();
        if (!_profiling)
        {
            throw new InvalidOperationException("Profiling is not active.");
        }

        var function = GetProfilerFunction("stop");
        try
        {
            var result = function.Call(SortName(sort));
            return (string)result[0];
        }
        finally
        {
            function.Dispose();
            _profiling = false;
        }
    }

    public string MemorySnapshot(int top = 20)
    {
        EnsureReady();
        var module = _memory ??= LoadDiagnosticsModule("LuaLoom.Diagnostics.memory.lua", "=lua_loom_memory");
        var function = module.Get<LuaFunction>("snapshot")
            ?? throw new LuaException(LuaStatus.ErrRun, "memory.snapshot is missing.");

        using (function)
        {
            var result = function.Call(top);
            return (string)result[0];
        }
    }

    private LuaFunction GetProfilerFunction(string name)
    {
        var module = _profiler ??= LoadDiagnosticsModule("LuaLoom.Diagnostics.profiler.lua", "=lua_loom_profiler");
        return module.Get<LuaFunction>(name)
            ?? throw new LuaException(LuaStatus.ErrRun, $"profiler.{name} is missing.");
    }

    private LuaTable LoadDiagnosticsModule(string resourceName, string chunkName)
    {
        using var stream = typeof(LuaEnv).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        var source = reader.ReadToEnd();

        var chunk = LoadString(source, chunkName);
        using (chunk)
        {
            var result = chunk.Call();
            return result.Length > 0 && result[0] is LuaTable table
                ? table
                : throw new InvalidOperationException($"Diagnostics module '{resourceName}' did not return a table.");
        }
    }

    private void EnsureClock()
    {
        if (_clockRegistered)
        {
            return;
        }

        LuaApi.lua_pushcclosure(Handle, ClockPointer, 0);
        LuaApi.lua_setglobal(Handle, "lua_loom_clock");
        _clockRegistered = true;
    }

    private static int OnClock(IntPtr state)
    {
        var seconds = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        LuaApi.lua_pushnumber(state, seconds);
        return 1;
    }

    private static string SortName(ProfileSort sort) => sort switch
    {
        ProfileSort.Total => "TOTAL",
        ProfileSort.Average => "AVERAGE",
        ProfileSort.Called => "CALLED",
        _ => "TOTAL",
    };
}
