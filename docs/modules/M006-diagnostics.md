# M006 diagnostics

## 功能详细描述与预期

提供两项运行期诊断：Lua 函数耗时统计与内存快照。两者都以文本报告返回，不改变业务状态。

**场景**

- 怀疑某段 Lua 逻辑慢：`StartProfile()` 采集一段时间，`StopProfile()` 拿到按总耗时、平均耗时或调用次数排序的表。
- 怀疑 Lua 内存持续增长：`MemorySnapshot()` 输出占用最大的表、全局变量与注册表项，用于定位泄漏点。

**边界**

- 不做火焰图、不做跨语言调用栈关联、不采集 C# 侧分配（用 .NET 自带工具）。
- 不常驻：仅在显式开启时生效，关闭后零开销。
- 报告只输出文本，不做持久化（由调用方决定落盘）。

**预期（可观察）**

| 编号 | 预期 | 观察方式 |
| :-- | :-- | :-- |
| E1 | `StartProfile` 后执行 1 万次函数调用，`StopProfile` 报告的调用次数与预期一致（误差 ≤ 1%） | 用例中固定循环次数 |
| E2 | 报告含函数名、源文件与行号、总时长、平均时长、占比、调用次数 | 逐列检查 |
| E3 | 关闭采集后再次执行业务，`StopProfile` 抛 `InvalidOperationException` | 断言异常 |
| E4 | `MemorySnapshot` 输出含表名、估算字节数、类型（`GLOBAL`/`REGISTRY`）、稳定的对象标识 | 逐列检查，标识在未重建时保持不变 |
| E5 | 循环往同一张表插入 1 万个元素，快照中该表尺寸单调增长 | 两次快照对比 |
| E6 | 采集期间脚本报错，`StopProfile` 仍能返回已采集数据 | 触发错误后取报告 |

**非目标**

- 采样式分析（P0 用 hook 计数）。
- 与 C# profiler 的调用栈对齐。
- 自动泄漏判定（只呈现数据，不定性）。

## 使用与交互设计

**调用方**：宿主业务代码、示例程序的诊断步骤。

**公共 API**

```csharp
public sealed partial class LuaEnv
{
    public void StartProfile();
    public string StopProfile(ProfileSort sort = ProfileSort.Total);
    public string MemorySnapshot(int top = 20);
}

public enum ProfileSort { Total, Average, Called }
```

**调用顺序**

```csharp
env.StartProfile();
env.DoString("local b = require('bench'); b.run()", "bench");
var report = env.StopProfile(ProfileSort.Total);
Console.WriteLine(report);

var memory = env.MemorySnapshot(top: 10);
```

**异常表**

| 异常 | 触发条件 | 调用方处理 |
| :-- | :-- | :-- |
| `InvalidOperationException` | 重复 `StartProfile`、未开始就 `StopProfile` | 修正调用时序 |
| `ObjectDisposedException` | 环境已释放 | 调用方缺陷 |
| `LuaException` | 诊断脚本本身执行失败 | 视为缺陷，上报 |

**界面**：无 GUI；报告为等宽文本，列之间用两个空格分隔。

## 具体实现细节

**数据结构**

Lua 侧采集表（`profiler.lua` 内部）：

| 字段 | 含义 |
| :-- | :-- |
| `name` | 函数名或 `[C]`、`[C#]` 标记 |
| `source` | 源文件路径 |
| `line` | 定义行号 |
| `total` | 累计耗时（秒，浮点） |
| `count` | 调用次数 |

C# 侧状态（挂在 `LuaEnv` 上）：

| 字段 | 类型 | 含义 |
| :-- | :-- | :-- |
| `_profiling` | `bool` | 是否采集中 |
| `_profiler` | `LuaTable?` | 已加载的 profiler 模块 |
| `_memory` | `LuaTable?` | 已加载的 memory 模块 |

**算法**

1. 首次调用诊断接口时，从嵌入资源加载 `profiler.lua` / `memory.lua`，编译并缓存为 `LuaFunction`；加载失败抛 `LuaException`。
2. `StartProfile`：注册 `lua_loom_clock`（基于 `Stopwatch` 的高分辨率时钟），调用 `profiler.start()`，内部用 `debug.sethook` 注册 `call`/`return` 钩子，按函数累计耗时与次数；匿名函数用 `源文件:行号` 代替函数名，profiler 自身的帧被过滤。
3. `StopProfile`：调用 `profiler.stop(sort)`，卸载 hook，返回格式化字符串；未开始时抛异常。
4. `MemorySnapshot`：调用 `memory.snapshot(top)`，遍历 `_G` 与 `debug.getregistry()`，用 `#table` 与键值递归估算尺寸，按字节数降序取前 N 项。
5. 报告格式固定：表头一行、分隔线一行、数据行；数值右对齐，文件名过长时保留后 40 个字符。

**开销**：hook 开启时每次函数调用与返回各触发一次回调，典型业务下开销在 2–10 倍之间；因此只在诊断时开启，`StopProfile` 后卸载 hook。内存快照为一次性遍历，开销与表规模线性相关。

**复杂度**：`StopProfile` 排序 O(n log n)，n 为被统计函数数；`MemorySnapshot` O(可达对象数)。

**接口契约与错误传播**

- 诊断脚本以嵌入资源随 `LuaLoom.Core` 发布，路径固定 `Diagnostics/profiler.lua`、`Diagnostics/memory.lua`。
- 报告只包含函数名、源路径、耗时、调用次数与表尺寸，不输出内部指针或环境地址。
- 采集期间发生的 Lua 错误不影响报告生成；`StopProfile` 内部用 `pcall` 调用采集脚本，失败时抛 `LuaException` 并保留原始消息。

**资源**：两个 Lua 脚本，UTF-8，随程序集打包；不使用外部文件。

## 测试方案

| 输入 | 操作 | 预期 |
| :-- | :-- | :-- |
| 固定循环 10000 次的函数 | `StartProfile` → 执行 → `StopProfile` | 调用次数在 `9900–10100` 之间 |
| 两个不同耗时的函数 | 按总时长排序 | 慢的在前 |
| 未开始采集 | `StopProfile` | 抛 `InvalidOperationException` |
| 已开始采集 | 再次 `StartProfile` | 抛 `InvalidOperationException` |
| 采集期间抛错 | 捕获后 `StopProfile` | 返回非空报告，含已采集条目 |
| 空环境 | `MemorySnapshot` | 返回表头与少量条目，不抛异常 |
| 向同一表插入 1 万个键 | 两次快照对比 | 该表字节数增长 |
| 关掉采集后跑 10 万次调用 | 计时对比开启前后 | 关闭后耗时回到基线量级 |
| 环境已释放 | 调用诊断接口 | 抛 `ObjectDisposedException` |

异常覆盖四类：空与边界（空环境、无采集数据）、非法输入（重复开始、未开始即结束）、依赖失败（嵌入脚本缺失、hook 安装失败）、重复与并发（采集期间重入、跨线程调用诊断接口）。
