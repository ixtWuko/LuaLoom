# M003 vm-runtime

## 功能详细描述与预期

虚拟机生命周期与公共入口：创建/销毁 `LuaEnv`、注册脚本加载器、执行代码块、GC 控制、句柄延迟清理、线程约束检查。它是使用方直接接触的唯一入口。

**场景**

- 宿主启动：`new LuaEnv(options)` 得到一个可执行 Lua（版本由构建决定）的环境，`package.path` 已按选项初始化。
- 加载脚本：`AddLoader` 注册自定义读取逻辑，`DoString("require 'main'")` 拉起业务入口。
- 运行期：周期性调用 `Tick()`，处理重入场景下不能立即释放的句柄。
- 协程：Lua 函数用 `coroutine.yield` 分段执行，C# 用 `CreateCoroutine()` / `Resume()` 控制推进节奏。
- 退出：`Dispose()` 关闭虚拟机，之后所有调用抛 `ObjectDisposedException`。

**边界**

- 不做类型转换（M002）、不做表与 delegate 代理（M004）、不定义生成规则（M005）。
- 不提供多虚拟机共享状态；每个 `LuaEnv` 完全独立。

**预期（可观察）**

| 编号 | 预期 | 观察方式 |
| :-- | :-- | :-- |
| E1 | 构造后 `Global` 可用，`package.path` 等于选项拼接结果 | 读 `_G.package.path` 比较 |
| E2 | `DoString("return 1,'a'")` 返回长度 2 的数组，元素为 `1` 与 `"a"` | 断言数组内容 |
| E3 | `DoString("return")` 返回空数组而非 `null` | 断言非 null 且长度为 0 |
| E4 | 自定义 loader 返回 `byte[]` 时 `require` 成功；返回 `null` 时继续下一个 loader | 注册两个 loader，观察命中顺序 |
| E5 | 所有 loader 都失败时抛 `LuaException`，消息含模块名与搜索过的 loader 数量 | 断言消息 |
| E6 | 脚本报错时异常消息包含 `chunkName`、行号与 traceback | 执行 `error('boom')` 断言 |
| E7 | 子线程调用任一公共成员抛 `InvalidOperationException` | 跨线程测试 |
| E8 | 重复 `Dispose` 不抛异常 | 连调两次 |
| E9 | 重入的 `Dispose` 延迟到 `Tick()` 执行，句柄最终被回收 | 回调里释放句柄，Tick 后查引用计数 |
| E10 | `CreateCoroutine()` 返回可推进的协程句柄；`Resume` 传入参数并返回 `yield`/`return` 的值 | 两次 `Resume` 的返回值与状态变化 |
| E11 | 协程结束或内部报错后状态为 `Dead`；再次 `Resume` 抛 `InvalidOperationException`，内部错误抛 `LuaException` | 分别断言 |

**非目标**

- 多虚拟机共享编译器缓存或共享 registry。
- 脚本加密与签名（不在基线）。
- C# 侧 `yield`、跨环境协程、与 `Task` 自动互转。

## 使用与交互设计

**调用方**：宿主业务代码、M004、M006。

**公共 API**

```csharp
public sealed partial class LuaEnv : IDisposable
{
    public LuaEnv(LuaEnvOptions? options = null);
    public LuaTable Global { get; }
    internal IntPtr Handle { get; }
    public long MemoryBytes { get; }
    public void AddLoader(ScriptLoader loader);
    public object[] DoString(string chunk, string chunkName = "chunk", LuaTable? env = null);
    public LuaFunction LoadString(string chunk, string chunkName = "chunk", LuaTable? env = null);
    public T LoadString<T>(string chunk, string chunkName = "chunk", LuaTable? env = null) where T : class;
    public LuaTable NewTable(int arraySize = 0, int recordSize = 0);
    public void RegisterMarshaler(ITypeMarshaler marshaler);
    public void Tick();
    public void CollectGarbage(GcMode mode);
    public void Dispose();
}

public sealed record LuaEnvOptions
{
    public ILibraryLoader? LibraryLoader { get; init; }
    public Action<LogLevel, string>? Logger { get; init; }
    public string[] SearchPaths { get; init; } = ["?.lua"];
    public bool EnableReflectionFallback { get; init; } = true;
    public bool StrictReflection { get; init; }
    public bool CheckThreadAffinity { get; init; } = true;
}

public delegate byte[]? ScriptLoader(ref string moduleName);

public sealed class LuaCoroutine : LuaReference
{
    public LuaCoroutineStatus Status { get; }
    public object[] Resume(params object[] args);
}

public enum LuaCoroutineStatus { Suspended, Dead }
```

`LuaTable`/`LuaFunction` 的类型定义在 M002，`LuaEnv` 只返回句柄，不重复定义。`LuaFunction.CreateCoroutine()` 创建协程，句柄生命周期规则与 M002 的 `LuaReference` 一致。

```csharp
using var function = (LuaFunction)env.DoString("""
    return function(a)
        local b = coroutine.yield(a + 1)
        return b * 2
    end
    """)[0]!;

using var coroutine = function.CreateCoroutine();
var first = coroutine.Resume(1);     // [2], Suspended
var second = coroutine.Resume(5);    // [10], Dead
```

**调用顺序**

```csharp
var options = new LuaEnvOptions { SearchPaths = ["scripts/?.lua"] };
using var env = new LuaEnv(options);
env.AddLoader((ref string name) =>
{
    var path = Path.Combine("scripts", name.Replace('.', '/') + ".lua");
    if (!File.Exists(path)) return null;          // 交给下一个 loader
    name = Path.GetFullPath(path);                // 供调试器定位
    return File.ReadAllBytes(path);
});
env.DoString("require 'main'", "bootstrap");
var update = env.Global.Get<Action<double>>("update");   // 需 M004 支持
update(0.016);
```

**异常表**

| 异常 | 触发条件 | 调用方处理 |
| :-- | :-- | :-- |
| `LuaException` | 脚本编译或运行错误、`require` 找不到模块 | 记录并决定是否中断 |
| `InvalidOperationException` | 跨线程调用 | 修正调用点，不重试 |
| `ObjectDisposedException` | 环境已释放 | 调用方缺陷 |
| `LuaLibraryNotFoundException` | 原生库缺失 | 初始化阶段终止 |

**界面**：无界面。可观察产出是返回值、异常与日志回调。

## 具体实现细节

**数据结构**

`LuaEnv`：

| 字段 | 类型 | 含义 |
| :-- | :-- | :-- |
| `_L` | `IntPtr` | Lua 状态 |
| `_marshal` | `LuaMarshal` | 类型转换与引用表 |
| `_loaders` | `List<ScriptLoader>` | 按注册顺序尝试 |
| `_ownerThread` | `int` | 创建线程 id |
| `_disposed` | `bool` | 幂等释放标记 |

延迟释放队列在 `LuaMarshal` 内部（`ConcurrentQueue<int>`），`Tick()` 调用 `DrainDeferredReleases()` 消费，避免 GC 线程直接操作 Lua。

**公共枚举**：`GcMode { Collect, Stop, Restart, Step, Count }`；`LogLevel { Trace, Info, Warning, Error }`。

**初始化步骤**

1. `ILibraryLoader.Load("lualoom")` → `luaL_newstate()`；库加载失败抛 `LuaLibraryNotFoundException`，状态创建失败抛 `LuaException`。
2. `luaL_openlibs()` 打开标准库。
3. 构造 `LuaMarshal`，创建 userdata 元表与引用表。
4. 注册 `require` 的 searcher：先走 `_loaders`，再走 `package.path` 文件查找；loader 返回 `null` 视为未命中，修改 `moduleName` 为真实路径以保留调试信息。
5. 设置 `package.path` 为 `SearchPaths` 按 `;` 拼接。
6. 注册消息处理器（`lua_pcall` 的 `msgh`），把错误对象转成带 traceback 的字符串。
7. 调用 `Interop.Install(this)` 建立 `CS` 表与类型解析器（实现在 M004，同程序集内的启动钩子）。

**执行与错误**

- `DoString`：`luaL_loadbufferx` → 失败直接抛 `LuaException`（语法错误，含 chunk 与行号）；成功则 `lua_pcallk`（`nargs=0`、`nresults` 不定），结果按 M002 转成 `object[]`。
- `LoadString`：只编译不执行，返回 `LuaFunction` 句柄。
- 错误对象为非字符串时，用 `tostring` 转换；转换失败则输出类型名。

**GC 与清理**

- `CollectGarbage(GcMode)` 映射到 `lua_gc`，`GcMode` 取 `Collect`、`Stop`、`Restart`、`Step`、`Count`。
- `MemoryBytes` 取 `lua_gc(LUA_GCCOUNT)` 与 `LUA_GCCOUNTB` 合成字节数。
- `Tick()` 调用 `_marshal.DrainDeferredReleases()` 消费终结器入队的 id，并执行一次 `lua_gc(LUA_GCSTEP)`。
- `Dispose()`：置位标记 → 清空 loader → `lua_close` → 释放原生库句柄（引用计数归零时）。
- 终结器与跨线程释放统一走 `LuaMarshal.EnqueueRelease(id)`，由 `Tick()` 消费。

**线程约束**：公共成员入口统一走 `EnsureThread()`，比较 `Environment.CurrentManagedThreadId`，不一致抛 `InvalidOperationException`；`CheckThreadAffinity=false` 时跳过检查但不保证安全。

**协程桥接**

- `LuaCoroutine` 持有 `lua_newthread` 创建的 `lua_State*` 与注册表引用；创建时把函数从主线程 `lua_xmove` 到协程栈。
- `Resume`：主线程压入协程与参数 → `lua_xmove` 到协程 → `lua_resume`（版本差异由 `LuaRuntimeProfile.Resume` 收敛）→ 结果移回主线程 → 按 M002 规则转成 `object[]`。
- 协程 `return` 后状态为 `Dead`；再次 `Resume` 抛 `InvalidOperationException`。协程内错误从协程栈读取错误对象，抛 `LuaException`，状态变为 `Dead`。
- `Dispose` 释放协程引用，重复释放无副作用；所有栈操作仍在主线程的 `LuaStackGuard` 内完成。

**复杂度**：初始化 O(1)（不含库内部）；`DoString` O(脚本)；`Tick` O(待释放数量)；`Resume` O(本次推进的脚本量)。

**接口契约与错误传播**

- `DoString` 永不返回 `null`，无返回值时返回 `Array.Empty<object>()`。
- 原生错误码统一转成 `LuaException`；其余异常（loader 抛出、参数校验失败）原样向上传播，不二次包装。
- loader 抛出的异常不被吞，直接中止 `require` 并向上传播。
- 协程句柄遵循单线程约束；对已结束的协程再次 `Resume` 抛 `InvalidOperationException`，协程内脚本错误抛 `LuaException`，已释放句柄抛 `ObjectDisposedException`。

## 测试方案

| 输入 | 操作 | 预期 |
| :-- | :-- | :-- |
| 默认选项 | 构造后读 `package.path` | 等于 `?.lua` |
| `SearchPaths = ["a/?.lua","b/?.lua"]` | 构造后读 `package.path` | 等于 `a/?.lua;b/?.lua` |
| `return 1,'a'` | `DoString` | 返回 `[1,"a"]` |
| `return` | `DoString` | 返回空数组 |
| 两个 loader，第一个返回 `null` | `require 'm'` | 第二个被调用并成功 |
| 所有 loader 返回 `null` 且无文件 | `require 'missing'` | 抛 `LuaException`，消息含 `missing` |
| `error('boom')` | `DoString` | 抛 `LuaException`，消息含 `boom` 与 traceback |
| 语法错误 `local = 1` | `DoString` | 抛 `LuaException`，消息含行号与 `chunkName` |
| 子线程调用 `DoString` | 直接调用 | 抛 `InvalidOperationException` |
| 连续两次 `Dispose` | 调用 | 第二次无操作、不抛异常 |
| C# 回调中 `Dispose` 一个表 | 随后 `Tick` | 引用计数归零，无泄漏 |
| 循环分配 10 万个表后释放 | 查询 `MemoryBytes` | 数值回落，不持续增长 |
| 带一次 `yield` 的函数 | 两次 `Resume` | `[2]` 后 `[10]`，状态从 `Suspended` 到 `Dead` |
| 立即 `return` 的函数 | 再 `Resume` | 抛 `InvalidOperationException` |
| 协程内 `error('coroutine boom')` | `Resume` | 抛 `LuaException`，消息含 `coroutine boom` |
| 已 `Dispose` 的协程 | `Resume` | 抛 `ObjectDisposedException` |
| 连续两次 `Dispose` 协程 | 调用 | 第二次无操作、不抛异常 |

异常覆盖四类：空与边界（空脚本、空返回值、`nil` 错误对象）、非法输入（语法错误、缺失模块）、依赖失败（loader 抛异常、DLL 缺失）、重复与并发（重复 `Dispose`、跨线程调用、回调内释放）。
