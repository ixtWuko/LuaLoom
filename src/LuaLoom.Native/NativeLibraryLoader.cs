using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace LuaLoom.Native;

/// <summary>
/// 默认的原生库加载器：裸库名 → 程序目录 → runtimes/win-x64/native → 程序集所在目录。
/// 同一库名在进程内只加载一次。
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
        var fileName = libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? libraryName
            : libraryName + ".dll";

        var candidates = new List<string>
        {
            libraryName,
            Path.Combine(baseDirectory, fileName),
            Path.Combine(baseDirectory, "runtimes", "win-x64", "native", fileName),
        };

        if (!string.Equals(assemblyDirectory, baseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(assemblyDirectory, fileName));
        }

        return candidates;
    }
}
