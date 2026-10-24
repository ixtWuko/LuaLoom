namespace LuaLoom;

/// <summary>类型转换失败：栈上的 Lua 类型与期望类型不兼容。</summary>
public sealed class LuaConversionException : Exception
{
    public LuaConversionException(string expected, string actual, int index, string? key = null)
        : base(BuildMessage(expected, actual, index, key))
    {
        Expected = expected;
        Actual = actual;
        Index = index;
        Key = key;
    }

    public string Expected { get; }

    public string Actual { get; }

    public int Index { get; }

    public string? Key { get; }

    private static string BuildMessage(string expected, string actual, int index, string? key)
    {
        var location = key is null ? $"stack index {index}" : $"key '{key}'";
        return $"Cannot convert Lua {actual} to {expected} at {location}.";
    }
}
