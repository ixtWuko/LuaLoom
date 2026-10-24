using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace LuaLoom.Native;

/// <summary>
/// 默认的原生库加载器：按当前 RID 查找 lualoom，支持程序目录、runtimes/&lt;rid&gt;/native
/// 与 LUALOOM_NATIVE_DIR 覆盖。同一库名在进程内只加载一次。
/// </summary>
public sealed class NativeLibraryLoader : ILibraryLoader
{
    private static readonly ConcurrentDictionary<string, IntPtr> Cache = new(StringComparer.OrdinalIgnoreCase);

    static NativeLibraryLoader()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryLoader).Assembly, static (name, _, _) =>
        {
            try
            {
                return Instance.Load(name);
            }
            catch (LuaLibraryNotFoundException)
            {
                return IntPtr.Zero; // 交给运行时默认解析
            }
        });
    }

    public static NativeLibraryLoader Instance { get; } = new();

    public IntPtr Load(string libraryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryName);

        if (Cache.TryGetValue(libraryName, out var cached))
        {
            return cached;
        }

        var attempted = BuildCandidatePaths(libraryName);
        foreach (var candidate in attempted)
        {
            if (NativeLibrary.TryLoad(candidate, out var handle))
            {
                Cache.TryAdd(libraryName, handle);
                return Cache[libraryName];
            }
        }

        throw new LuaLibraryNotFoundException(libraryName, attempted);
    }

    private static IReadOnlyList<string> BuildCandidatePaths(string libraryName)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var assemblyDirectory = Path.GetDirectoryName(typeof(NativeLibraryLoader).Assembly.Location) ?? baseDirectory;
        var fileName = ResolveFileName(libraryName);
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
            {
                candidates.Add(path);
            }
        }

        var overrideDirectory = Environment.GetEnvironmentVariable("LUALOOM_NATIVE_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            Add(Path.Combine(overrideDirectory, fileName));
        }

        Add(libraryName);

        foreach (var rid in CandidateRuntimeIdentifiers())
        {
            Add(Path.Combine(baseDirectory, "runtimes", rid, "native", fileName));
        }

        Add(Path.Combine(baseDirectory, fileName));
        Add(Path.Combine(assemblyDirectory, fileName));

        if (!string.Equals(assemblyDirectory, baseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var rid in CandidateRuntimeIdentifiers())
            {
                Add(Path.Combine(assemblyDirectory, "runtimes", rid, "native", fileName));
            }
        }

        return candidates;
    }

    private static string ResolveFileName(string libraryName)
    {
        if (libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || libraryName.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
            || libraryName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
        {
            return libraryName;
        }

        if (OperatingSystem.IsWindows())
        {
            return libraryName + ".dll";
        }

        return OperatingSystem.IsMacOS()
            ? "lib" + libraryName + ".dylib"
            : "lib" + libraryName + ".so";
    }

    private static IReadOnlyList<string> CandidateRuntimeIdentifiers()
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        var fallback = BuildPortableRid();
        return string.Equals(rid, fallback, StringComparison.OrdinalIgnoreCase)
            ? [rid]
            : [rid, fallback];
    }

    private static string BuildPortableRid()
    {
        var os = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsMacOS()
                ? "osx"
                : "linux";

        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => "unknown",
        };

        return $"{os}-{architecture}";
    }
}
