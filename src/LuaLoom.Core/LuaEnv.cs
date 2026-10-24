using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using LuaLoom.Internal;
using LuaLoom.Native;
using NativeMarshal = System.Runtime.InteropServices.Marshal;

namespace LuaLoom;

/// <summary>Lua 虚拟机（5.3 / 5.4 / 5.5）。所有公共入口都要求同一线程调用。</summary>
public sealed partial class LuaEnv : IDisposable
{
    private const string LoaderBridgeName = "lua_loom_loader";

    private const string Bootstrap = """
        local searchers = package.searchers
        table.insert(searchers, 2, function(name)
            local chunk, path = lua_loom_loader(name)
            if chunk == nil then
                return "\n\tno LuaLoom loader for '" .. name .. "'"
            end
            local fn, err = load(chunk, "@" .. path)
            if not fn then
                return "\n\t" .. tostring(err)
            end
            return fn, path
        end)
        """;

    private static readonly ConcurrentDictionary<IntPtr, LuaEnv> Environments = new();

    private static readonly LuaCSFunction ErrorHandlerCallback = OnError;
    private static readonly IntPtr ErrorHandlerPointer =
        NativeMarshal.GetFunctionPointerForDelegate(ErrorHandlerCallback);

    private static readonly LuaCSFunction LoaderBridgeCallback = OnLoader;
    private static readonly IntPtr LoaderBridgePointer =
        NativeMarshal.GetFunctionPointerForDelegate(LoaderBridgeCallback);

    private readonly IntPtr _state;
    private readonly LuaMarshal _marshal;
    private readonly LuaEnvOptions _options;
    private readonly List<ScriptLoader> _loaders = new();
    private readonly int _errorHandlerRef;
    private readonly int _ownerThreadId;
    private int _disposed;

    public LuaEnv(LuaEnvOptions? options = null)
    {
        _options = options ?? new LuaEnvOptions();
        _ownerThreadId = Environment.CurrentManagedThreadId;

        (_options.LibraryLoader ?? NativeLibraryLoader.Instance).Load(LuaApi.Library);

        _state = LuaApi.luaL_newstate();
        if (_state == IntPtr.Zero)
        {
            throw new LuaException(LuaStatus.ErrMem, "luaL_newstate returned a null state.");
        }

        LuaApi.luaL_openlibs(_state);

        LuaMarshal? marshal = null;
        try
        {
            marshal = new LuaMarshal(_state);
            _marshal = marshal;
            _marshal.Environment = this;
            Environments[_state] = this;

            using (LuaStackGuard.Enter(_state))
            {
                RegisterErrorHandler(out _errorHandlerRef);
                RegisterLoaderBridge();
                Global = CreateGlobalTable();
                ApplySearchPaths();

                ExecuteChunk(Bootstrap, "=lua_loom_init");
            }

            Interop.Install(this);
        }
        catch
        {
            Environments.TryRemove(_state, out _);
            Interop.Uninstall(_state);
            marshal?.Dispose();
            LuaApi.lua_close(_state);
            throw;
        }
    }

    public LuaTable Global { get; }

    public long MemoryBytes
    {
        get
        {
            EnsureReady();
            var kilobytes = LuaApi.lua_gc(_state, LuaApi.LuaGcCount, 0);
            var remainder = LuaApi.lua_gc(_state, LuaApi.LuaGcCountB, 0);
            return (kilobytes * 1024L) + remainder;
        }
    }

    internal IntPtr Handle => _state;

    internal LuaMarshal Marshal => _marshal;

    internal LuaEnvOptions Options => _options;

    public void AddLoader(ScriptLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        EnsureReady();
        _loaders.Add(loader);
    }

    public object[] DoString(string chunk, string chunkName = "chunk")
    {
        EnsureReady();
        ArgumentNullException.ThrowIfNull(chunk);

        using var guard = LuaStackGuard.Enter(_state);
        var baseTop = LuaApi.lua_gettop(_state);
        var errorIndex = PushErrorHandler();
        LoadChunk(chunk, chunkName);

        var status = LuaApi.lua_pcallk(
            _state,
            argCount: 0,
            resultCount: LuaApi.LuaMultRet,
            errorFunction: errorIndex,
            context: IntPtr.Zero,
            continuation: IntPtr.Zero);

        if (status != LuaApi.LuaOk)
        {
            throw LuaErrors.Create(_state, (LuaStatus)status, chunkName);
        }

        var count = LuaApi.lua_gettop(_state) - baseTop - 1;
        var results = new object[count];
        for (var i = 0; i < count; i++)
        {
            results[i] = _marshal.ReadObject(baseTop + 2 + i)!;
        }

        return results;
    }

    public LuaFunction LoadString(string chunk, string chunkName = "chunk")
    {
        EnsureReady();
        ArgumentNullException.ThrowIfNull(chunk);

        using var guard = LuaStackGuard.Enter(_state);
        PushErrorHandler();
        LoadChunk(chunk, chunkName);
        return _marshal.CreateFunction(-1);
    }

    public LuaTable NewTable(int arraySize = 0, int recordSize = 0)
    {
        EnsureReady();
        return _marshal.CreateTable(arraySize, recordSize);
    }

    public void RegisterMarshaler(ITypeMarshaler marshaler)
    {
        ArgumentNullException.ThrowIfNull(marshaler);
        EnsureReady();
        _marshal.RegisterMarshaler(marshaler);
    }

    public void Tick()
    {
        EnsureReady();
        _marshal.DrainDeferredReleases();
        LuaApi.lua_gc(_state, LuaApi.LuaGcStep, 0);
    }

    public void CollectGarbage(GcMode mode)
    {
        EnsureReady();
        var what = mode switch
        {
            GcMode.Collect => LuaApi.LuaGcCollect,
            GcMode.Stop => LuaApi.LuaGcStop,
            GcMode.Restart => LuaApi.LuaGcRestart,
            GcMode.Step => LuaApi.LuaGcStep,
            GcMode.Count => LuaApi.LuaGcCount,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        LuaApi.lua_gc(_state, what, 0);
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        EnsureThread();
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Environments.TryRemove(_state, out _);
        Interop.Uninstall(_state);
        _marshal.Dispose();
        LuaApi.lua_close(_state);
    }

    private static int OnError(IntPtr state)
    {
        var pointer = LuaApi.lua_tolstring(state, 1, out var length);
        string? message = null;
        if (pointer != IntPtr.Zero)
        {
            message = NativeMarshal.PtrToStringUTF8(pointer, checked((int)length));
        }

        LuaApi.luaL_traceback(state, state, message, 1);
        return 1;
    }

    private static int OnLoader(IntPtr state)
    {
        var moduleName = ReadArgumentString(state, 1);
        if (!Environments.TryGetValue(state, out var environment))
        {
            LuaApi.lua_pushnil(state);
            return 1;
        }

        foreach (var loader in environment._loaders.ToArray())
        {
            var name = moduleName;
            byte[]? bytes;
            try
            {
                bytes = loader(ref name);
            }
            catch (Exception exception)
            {
                LuaApi.lua_pushstring(state, exception.Message);
                return LuaApi.lua_error(state);
            }

            if (bytes is null)
            {
                continue;
            }

            LuaApi.lua_pushlstring(state, bytes, (nuint)bytes.Length);
            LuaApi.lua_pushstring(state, name);
            return 2;
        }

        LuaApi.lua_pushnil(state);
        environment.Options.Logger?.Invoke(LogLevel.Trace, $"No LuaLoom loader matched module '{moduleName}'.");
        return 1;
    }

    private static string ReadArgumentString(IntPtr state, int index)
    {
        var pointer = LuaApi.lua_tolstring(state, index, out var length);
        return pointer == IntPtr.Zero ? string.Empty : NativeMarshal.PtrToStringUTF8(pointer, checked((int)length)) ?? string.Empty;
    }

    private void ExecuteChunk(string chunk, string chunkName)
    {
        var errorIndex = PushErrorHandler();
        LoadChunk(chunk, chunkName);
        var status = LuaApi.lua_pcallk(_state, 0, 0, errorIndex, IntPtr.Zero, IntPtr.Zero);
        if (status != LuaApi.LuaOk)
        {
            throw LuaErrors.Create(_state, (LuaStatus)status, chunkName);
        }
    }

    private void LoadChunk(string chunk, string chunkName)
    {
        var bytes = Encoding.UTF8.GetBytes(chunk);
        var status = LuaApi.luaL_loadbufferx(_state, bytes, (nuint)bytes.Length, chunkName, "t");
        if (status != LuaApi.LuaOk)
        {
            throw LuaErrors.Create(_state, (LuaStatus)status, chunkName);
        }
    }

    private int PushErrorHandler()
    {
        LuaApi.lua_rawgeti(_state, LuaApi.Profile.RegistryIndex, _errorHandlerRef);
        return LuaApi.lua_gettop(_state);
    }

    private void RegisterErrorHandler(out int reference)
    {
        LuaApi.lua_pushcclosure(_state, ErrorHandlerPointer, 0);
        reference = _marshal.AddReference(LuaApi.lua_gettop(_state));
    }

    private void RegisterLoaderBridge()
    {
        LuaApi.lua_pushcclosure(_state, LoaderBridgePointer, 0);
        LuaApi.lua_setglobal(_state, LoaderBridgeName);
    }

    private LuaTable CreateGlobalTable()
    {
        LuaApi.lua_rawgeti(_state, LuaApi.Profile.RegistryIndex, 2); // LUA_RIDX_GLOBALS
        var id = _marshal.AddReference(LuaApi.lua_gettop(_state));
        return new LuaTable(_marshal, id);
    }

    private void ApplySearchPaths()
    {
        if (_options.SearchPaths.Length == 0)
        {
            return;
        }

        LuaApi.lua_getglobal(_state, "package");
        if (LuaApi.lua_type(_state, -1) == LuaApi.LuaTTable)
        {
            LuaApi.lua_pushstring(_state, string.Join(';', _options.SearchPaths));
            LuaApi.lua_setfield(_state, -2, "path");
        }

        LuaApi.lua_settop(_state, LuaApi.lua_gettop(_state) - 1);
    }

    private void EnsureReady()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(LuaEnv));
        }

        EnsureThread();
    }

    internal void EnsureThread()
    {
        if (_options.CheckThreadAffinity && Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                $"LuaEnv is single-threaded. Created on thread {_ownerThreadId}, called from {Environment.CurrentManagedThreadId}.");
        }
    }
}
