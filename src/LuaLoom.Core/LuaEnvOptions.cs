using LuaLoom.Native;

namespace LuaLoom;

/// <summary>虚拟机选项。</summary>
public sealed record LuaEnvOptions
{
    /// <summary>原生库加载器；默认为 <see cref="NativeLibraryLoader"/>。</summary>
    public ILibraryLoader? LibraryLoader { get; init; }

    /// <summary>日志回调；不设置时不输出。</summary>
    public Action<LogLevel, string>? Logger { get; init; }

    /// <summary>初始 package.path 片段，按 `;` 拼接。</summary>
    public string[] SearchPaths { get; init; } = ["?.lua"];

    /// <summary>未生成绑定的类型是否走反射兜底（M004 使用）。</summary>
    public bool EnableReflectionFallback { get; init; } = true;

    /// <summary>是否检查线程归属；关闭后不保证线程安全。</summary>
    public bool CheckThreadAffinity { get; init; } = true;
}

/// <summary>GC 操作，映射 lua_gc 的选项。</summary>
public enum GcMode
{
    Collect,
    Stop,
    Restart,
    Step,
    Count,
}

/// <summary>日志级别。</summary>
public enum LogLevel
{
    Trace,
    Info,
    Warning,
    Error,
}

/// <summary>脚本加载器：返回脚本字节；返回 null 表示交给下一个 loader。</summary>
/// <remarks><paramref name="moduleName"/> 可被实现改写为真实路径，用于调试信息。</remarks>
public delegate byte[]? ScriptLoader(ref string moduleName);
