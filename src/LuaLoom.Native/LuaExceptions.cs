using System.Text.RegularExpressions;

namespace LuaLoom.Native;

/// <summary>Lua 脚本编译或运行错误。</summary>
public sealed class LuaException : Exception
{
    private static readonly Regex LineNumber = new(@":(?<line>\d+):", RegexOptions.Compiled);

    public LuaException(
        LuaStatus status,
        string message,
        string chunkName = "chunk",
        string? traceback = null)
        : base(message)
    {
        Status = status;
        ChunkName = chunkName;
        Traceback = traceback ?? string.Empty;

        var match = LineNumber.Match(message);
        if (match.Success && int.TryParse(match.Groups["line"].Value, out var line))
        {
            Line = line;
        }
    }

    public LuaStatus Status { get; }

    public string ChunkName { get; }

    public int? Line { get; }

    public string Traceback { get; }
}

/// <summary>原生库找不到或无法加载。</summary>
public sealed class LuaLibraryNotFoundException : Exception
{
    public LuaLibraryNotFoundException(
        string libraryName,
        IReadOnlyList<string> attemptedPaths,
        Exception? innerException = null)
        : base(BuildMessage(libraryName, attemptedPaths), innerException)
    {
        LibraryName = libraryName;
        AttemptedPaths = attemptedPaths;
    }

    public string LibraryName { get; }

    public IReadOnlyList<string> AttemptedPaths { get; }

    private static string BuildMessage(string libraryName, IReadOnlyList<string> attemptedPaths)
    {
        var paths = attemptedPaths.Count == 0
            ? "(none)"
            : string.Join(Environment.NewLine, attemptedPaths.Select(static p => "  " + p));
        return $"Failed to load native Lua library '{libraryName}'. Attempted paths:{Environment.NewLine}{paths}";
    }
}

