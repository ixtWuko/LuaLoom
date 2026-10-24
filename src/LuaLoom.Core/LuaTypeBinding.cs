using LuaLoom.Native;

namespace LuaLoom;

/// <summary>一个 C# 类型在 Lua 侧的绑定描述，由生成代码提供。</summary>
public sealed class LuaTypeBinding
{
    public required Type Type { get; init; }

    public required string FullName { get; init; }

    /// <summary>
    /// 对应 <c>__call</c>：参数从栈索引 2 开始（1 是类表）。返回 0 且不压栈表示未处理，运行时回落到反射构造。
    /// </summary>
    public LuaCSFunction? Call { get; init; }

    /// <summary>
    /// 对应类表的 <c>__index</c>：参数为 (类表, 键)。返回 0 且不压栈表示未处理，运行时回落到反射。
    /// </summary>
    public LuaCSFunction? StaticIndex { get; init; }

    /// <summary>
    /// 对应类表的 <c>__newindex</c>：参数为 (类表, 键, 值)。返回 0 表示已处理；
    /// 压入 <c>nil</c> 并返回 1 表示未处理，运行时回落到反射。
    /// </summary>
    public LuaCSFunction? StaticNewIndex { get; init; }

    /// <summary>
    /// 对应实例 userdata 的 <c>__index</c>：参数为 (userdata, 键)。返回 0 且不压栈表示未处理，运行时回落到反射。
    /// </summary>
    public LuaCSFunction? InstanceIndex { get; init; }

    /// <summary>
    /// 对应实例 userdata 的 <c>__newindex</c>：参数为 (userdata, 键, 值)。返回 0 表示已处理；
    /// 压入 <c>nil</c> 并返回 1 表示未处理，运行时回落到反射。
    /// </summary>
    public LuaCSFunction? InstanceNewIndex { get; init; }
}
