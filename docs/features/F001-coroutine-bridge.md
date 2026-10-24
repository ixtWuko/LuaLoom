# F001 协程桥接

## 功能详细描述与预期

让 C# 侧创建并推进 Lua 协程，用于把「分段执行」的 Lua 逻辑（加载流程、对话、分帧任务）交给宿主控制节奏。

**场景**：Lua 提供一个带 `coroutine.yield` 的函数；C# 创建协程后，每帧或每步调用 `Resume`，用 `yield` 的返回值做状态判断。

**预期**

| 编号 | 预期 | 观察方式 |
| :-- | :-- | :-- |
| E1 | `LuaFunction.CreateCoroutine()` 返回可推进的协程句柄 | 断言类型与初始状态为 `Suspended` |
| E2 | `Resume(args)` 把参数传入协程，返回 `yield` 或 `return` 的值 | 连续两次 Resume 的返回值符合脚本预期 |
| E3 | 协程 `return` 后状态变为 `Dead`，再次 `Resume` 抛 `InvalidOperationException` | 两次断言 |
| E4 | 协程内部报错时 `Resume` 抛 `LuaException`，状态变为 `Dead` | 断言消息含脚本错误文本 |
| E5 | `Dispose` 释放线程引用，重复释放无副作用 | 连续两次 Dispose |

**非目标**：C# 侧 `yield`（协程只能由 Lua 发起挂起）、跨环境协程、与 `Task` 自动互转。

## 使用与交互设计

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

| 异常 | 触发条件 |
| :-- | :-- |
| `InvalidOperationException` | 对已结束的协程再次 `Resume` |
| `ObjectDisposedException` | 协程已释放 |
| `LuaException` | 协程内部抛出 Lua 错误 |

## 具体实现细节

`LuaCoroutine` 持有 `lua_State*`（由 `lua_newthread` 创建）与注册表引用；创建时把函数从主线程 `lua_xmove` 到协程栈。

`Resume` 流程：主线程压入协程与参数 → `lua_xmove` 到协程 → `lua_resume` → 把结果移回主线程 → 按 `object` 规则转换。错误时从协程栈读取错误对象构造 `LuaException`。所有栈操作在主线程的 `LuaStackGuard` 内完成。

## 测试方案

| 输入 | 操作 | 预期 |
| :-- | :-- | :-- |
| 带一次 `yield` 的函数 | 两次 `Resume` | `[2]` 后 `[10]`，状态从 `Suspended` 到 `Dead` |
| 立即 `return` 的函数 | `Resume` 后再 `Resume` | 第二次抛 `InvalidOperationException` |
| 内部 `error('coroutine boom')` | `Resume` | 抛 `LuaException`，消息含 `coroutine boom` |
| 已 `Dispose` 的协程 | `Resume` | 抛 `ObjectDisposedException` |
| 连续两次 `Dispose` | 调用 | 无异常 |

## 与基线的边界

- 新增公共类型 `LuaCoroutine`、`LuaCoroutineStatus`，`LuaFunction` 增加 `CreateCoroutine()`；均为向后兼容的新增，不改既有契约。
- 基线 M003 文档把协程列为 P1 非目标，本功能落地后其非目标条目应删除（已同步）。
