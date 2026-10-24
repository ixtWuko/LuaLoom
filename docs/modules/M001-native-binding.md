# M001 native-binding

## 功能详细描述与预期

负责与 Lua 原生库的全部交互边界：定位并加载 `lua54.dll`、声明 C API、守住每次跨边界的栈平衡、把 C 侧错误码转成托管异常。

**场景**

- 宿主进程启动时创建虚拟机（由 M003 触发），本模块先解析原生库位置并加载，再提供后续一切 `lua_*` 调用。
- 任意一次托管代码进入 Lua 或从 Lua 回到托管代码，本模块保证函数返回时栈顶与进入时一致。

**边界**

- 只处理 Lua 5.4 的 C API，不提供 5.3 或 LuaJIT 的兼容分支。
- 不实现类型映射与业务语义，`Push`/`Pop` 的具体规则属于 M002。
- 不负责虚拟机生命周期，`lua_close` 由 M003 调。

**预期（可观察）**

| 编号 | 预期 | 观察方式 |
| :-- | :-- | :-- |
| E1 | 原生库加载失败时抛 `LuaLibraryNotFoundException`，消息含尝试过的库名与搜索目录 | 断言异常类型与消息片段 |
| E2 | 进入守卫时记录 `lua_gettop`，离开时恢复到同一值 | 单测里在 `using` 前后比较 `lua_gettop` |
| E3 | 守卫体内抛托管异常，栈仍恢复 | 故意抛异常后比较 `lua_gettop` |
| E4 | `lua_pcallk` 返回非 `Ok` 时抛 `LuaException`，消息含状态码与错误文本 | 加载 `error('boom')` 后调用断言 |
| E5 | 同一进程内重复加载原生库不重复 `LoadLibrary` | 断言 `ILibraryLoader.Load` 调用次数为 1 |

**非目标**

- 动态符号查找、按需 `GetProcAddress`（P/Invoke 由运行时代劳）。
- 跨平台库名映射（P0 只支持 Windows x64，表结构预留 `PlatformLibraryName`）。

## 使用与交互设计

**调用方**：M002、M003。M002 只调 `LuaApi` 的 push/pop/get 系列；M003 调状态创建、`lua_close`、`lua_pcall`、GC 相关。

**调用顺序**

```csharp
var L = LuaApi.luaL_newstate();            // 状态创建由 M003 触发
using (var guard = LuaStackGuard.Enter(L))
{
    LuaApi.lua_pushinteger(L, 42);
    // 离开 guard 时自动 lua_settop 回进入前的 top
}
LuaApi.lua_close(L);
```

**异常表**

| 异常 | 触发条件 | 调用方处理 |
| :-- | :-- | :-- |
| `LuaLibraryNotFoundException` | 原生库找不到或位宽不符 | 终止初始化，提示安装路径 |
| `LuaException` | `lua_pcall` 返回非 `LUA_OK` | 按脚本错误处理，可继续使用虚拟机 |
| `ArgumentException` | 传入 `IntPtr.Zero` 作为状态指针 | 调用方缺陷，不捕获 |

panic 不在本模块处理：注册托管 panic 回调意味着异常可能穿过 C 栈帧，行为未定义；保持 Lua 默认的打印并终止进程，P1 再评估安全方案。

**界面**：本模块无界面。对外只暴露 `ILibraryLoader`、`LuaCSFunction` 与两个异常类型，其余为 `internal`。

## 具体实现细节

**数据结构**

`LuaStackGuard`（`readonly ref struct`）：

| 字段 | 类型 | 含义 |
| :-- | :-- | :-- |
| `_L` | `IntPtr` | Lua 状态指针 |
| `_top` | `int` | 进入时的栈顶索引 |

`ILibraryLoader`：

| 成员 | 签名 | 说明 |
| :-- | :-- | :-- |
| `Load` | `IntPtr Load(string libraryName)` | 找不到时抛 `LuaLibraryNotFoundException` |

`LuaStatus` 枚举：`Ok=0`、`Yield=1`、`ErrRun=2`、`ErrSyntax=3`、`ErrMem=4`、`ErrErr=5`，与 `lua.h` 一致。

`LuaCSFunction`：`public delegate int LuaCSFunction(IntPtr L);`，即 `lua_CFunction` 的托管形式；生成代码与反射兜底都以它为边界。

异常类型（定义在本程序集，供上层引用）：

| 类型 | 字段 | 用途 |
| :-- | :-- | :-- |
| `LuaException` | `LuaStatus Status`、`string ChunkName`、`int? Line`、`string Traceback` | 脚本编译或运行错误 |
| `LuaLibraryNotFoundException` | `string LibraryName`、`IReadOnlyList<string> AttemptedPaths`、`Exception? InnerException` | 原生库找不到或位宽不符 |

**算法**

1. 加载：注册 `NativeLibrary.SetDllImportResolver`，把 `lua54` 的解析交给 `ILibraryLoader`；按裸库名 → 程序目录 → `runtimes/win-x64/native` → 程序集目录顺序尝试，全部失败抛 `LuaLibraryNotFoundException`，消息列出完整路径。解析失败时返回 `IntPtr.Zero`，回落到运行时默认解析。
2. 守卫：`Enter(L)` 记录 `lua_gettop(L)`；`Dispose()` 调 `lua_settop(L, _top)`。不吞异常，异常照常向上传播。
3. 错误转换：`pcall` 封装读取返回值，非 `Ok` 时从栈顶取错误对象，按 `LuaType` 转成字符串，抛 `LuaException(status, message)`，并把错误对象弹出栈。

**复杂度**：加载 O(1)（进程内缓存）；守卫 O(1)；`pcall` 封装 O(1)。

**接口契约与错误传播**

- `LuaApi` 全部方法 `internal static extern`，`CallingConvention.Cdecl`，参数只用 `IntPtr`、`int`、`double`、`long`、`ulong`、`string`、`byte[]`，不出现 `bool`（避免封送歧义，用 `int`）。
- `LuaApi`/`LuaStackGuard` 通过 `InternalsVisibleTo("LuaLoom.Core")` 与 `InternalsVisibleTo("LuaLoom.UnitTests")` 开放；生成代码不在此列，只能走 M002 的 `LuaBinding`。
- 本模块不捕获异常，只做转换；栈恢复由守卫负责，二者职责不重叠。

**资源**

- 原生库由 `scripts/build-native.ps1` 产出到 `artifacts/native/win-x64/lua54.dll`，随构建复制到输出目录。
- 无图片等界面资源。

## 测试方案

| 输入 | 操作 | 预期 |
| :-- | :-- | :-- |
| 正常库路径 | 加载并创建状态 | 返回非零指针，`lua_gettop` 为 0 |
| 库名改为不存在的 `lua99` | `ILibraryLoader.Load("lua99")` | 抛 `LuaLibraryNotFoundException`，消息含 `lua99` |
| 空状态 | 守卫内 push 3 个值后退出 | `lua_gettop` 回到进入前 |
| 守卫内抛 `InvalidOperationException` | 捕获异常后查栈 | 异常原样抛出，`lua_gettop` 已恢复 |
| 嵌套两层守卫 | 内层 return、外层继续 push | 各层恢复各自的 top，互不影响 |
| 语法错误 `"local = 1"` | `luaL_loadbufferx` | 返回 `ErrSyntax`，错误文本含行号 |
| 运行时错误 `"error('boom')"` | 加载后调 `lua_pcallk` 封装 | 返回 `ErrRun`，错误对象转成 `LuaException`，消息含 `boom` |
| 连续两次 `Load("lua54")` | 观察 loader | 底层只加载一次，返回同一句柄 |

异常覆盖四类：空与边界（空状态、栈顶 0）、非法输入（错误库名、语法错误）、依赖失败（DLL 缺失、错误码非 `Ok`）、重复与并发（重复加载、嵌套守卫、异常路径恢复）。
