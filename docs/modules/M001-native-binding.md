# M001 native-binding

## 功能详细描述与预期

负责与 Lua 原生库的全部交互边界：定位并加载 `lualoom.dll`、声明 C API、守住每次跨边界的栈平衡、把 C 侧错误码转成托管异常。Lua 版本差异由 `LuaRuntimeProfile` 收敛，本模块其余部分不感知版本。

**场景**

- 宿主进程启动时创建虚拟机（由 M003 触发），本模块先解析原生库位置并加载，再提供后续一切 `lua_*` 调用。
- 任意一次托管代码进入 Lua 或从 Lua 回到托管代码，本模块保证函数返回时栈顶与进入时一致。
- 构建期在 Lua 5.4.9 与 5.3.6 之间二选一；两个版本共用同一套源码与托管 API，不在一进程内混用。
- 原生库按 RID 组织，Windows x64、Linux x64、macOS arm64 使用同一套加载逻辑。

**边界**

- 只处理 Lua 5.3 / 5.4 的 C API（5.5 预留画像），不提供 LuaJIT 的兼容分支。
- 版本差异只允许出现在 `LuaRuntimeProfile` 与两个版本原生的声明里，其他代码不做版本判断。
- 不实现类型映射与业务语义，`Push`/`Pop` 的具体规则属于 M002。
- 不负责虚拟机生命周期，`lua_close` 由 M003 调。

**预期（可观察）**

| 编号 | 预期 | 观察方式 |
| :-- | :-- | :-- |
| E1 | 原生库加载失败时抛 `LuaLibraryNotFoundException`，消息含尝试过的库名与搜索目录 | 断言异常类型与消息片段 |
| E2 | 进入守卫时记录 `lua_gettop`，离开时恢复到同一值 | 单测里在 `using` 前后比较 `lua_gettop` |
| E3 | 守卫体内抛托管异常，栈仍恢复 | 故意抛异常后比较 `lua_gettop` |
| E4 | `lua_pcallk` 返回非 `Ok` 时抛 `LuaException`，消息含状态码与错误文本 | 加载 `error('boom')` 后调用断言 |
| E5 | 同一进程内重复加载原生库不重复 `LoadLibrary` | 连续两次 `Load` 返回同一指针 |
| E6 | `-p:LuaVersion=53` 使用 5.3.6 原生库构建，同一组用例通过 | 检查 `LuaApi.Profile.Version` 与测试结果 |
| E7 | 加载器按 `LUALOOM_NATIVE_DIR`、库名、`runtimes/<rid>/native/`、程序目录依次查找 | 三个平台 CI 均能创建 `LuaEnv` |
| E8 | 当前 RID 缺库时异常列出所有尝试路径 | `LuaLibraryNotFoundException.AttemptedPaths` |

**非目标**

- 动态符号查找、按需 `GetProcAddress`（P/Invoke 由运行时代劳）。
- Windows arm64、Linux arm64、iOS / Android / WebGL；LuaJIT。
- 同一进程同时加载两个 Lua 版本；NuGet 打包与发布。
- macOS 代码签名与公证。

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

**界面**：本模块无界面。对外暴露 `ILibraryLoader`、`NativeLibraryLoader`、`LuaCSFunction`、`LuaStatus` 与两个异常类型，其余为 `internal`。

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

1. 加载：注册 `NativeLibrary.SetDllImportResolver`，把 `lualoom` 的解析交给 `ILibraryLoader`；按 `LUALOOM_NATIVE_DIR` → 裸库名 → 程序目录 → `runtimes/<rid>/native` → 程序集目录 → 程序集下 `runtimes/<rid>/native` 顺序尝试，平台扩展名按 `.dll`/`.so`/`.dylib` 区分。全部失败抛 `LuaLibraryNotFoundException`，消息列出完整路径；解析失败返回 `IntPtr.Zero`，回落到运行时默认解析。
2. 守卫：`Enter(L)` 记录 `lua_gettop(L)`；`Dispose()` 调 `lua_settop(L, _top)`。不吞异常，异常照常向上传播。
3. 错误转换：`pcall` 封装读取返回值，非 `Ok` 时从栈顶取错误对象，按 `LuaType` 转成字符串，抛 `LuaException(status, message)`，并把错误对象弹出栈。
4. 版本画像：`LuaRuntimeProfile` 提供 `RegistryIndex`、`NewUserdata`、`Resume` 三个入口；`Lua53Profile` 与 `Lua54Profile` 分别绑定各自的原生声明，构建时通过程序集元数据选定。

**版本变体**

| 版本 | 构建属性 | 状态 |
| :-- | :-- | :-- |
| Lua 5.4.9 | 默认或 `54` | 已验证 |
| Lua 5.3.6 | `53` | 已验证 |
| Lua 5.5.x | `55` | 仅预留画像，未验证 |

```powershell
# 5.4（默认）
pwsh scripts/ci.ps1

# 5.3
pwsh scripts/ci.ps1 -Version 53
```

- 两个版本各自产出独立的原生库与托管构建，同一进程不能混用。
- 版本差异收敛在 `LuaRuntimeProfile`：

| Profile | 创建 userdata | 注册表伪索引 | 协程恢复 |
| :-- | :-- | :-- | :-- |
| `Lua53Profile` | `lua_newuserdata` | `-1001000` | `lua_resume(L, from, narg)` |
| `Lua54Profile` | `lua_newuserdatauv` | `-1001000` | `lua_resume` 多一个结果数出参 |
| `Lua55Profile` | `lua_newuserdatauv` | `-(INT_MAX/2 + 1000)` | 同 5.4 |

- 版本表在 `scripts/lua-versions.json`，`fetch-lua.ps1` 按表下载并校验 SHA-256，`build-native.ps1` 用独立构建目录；CMake 接收 `LUA_VERSION`，5.3 额外包含 `lbitlib.c`。
- 不引入 `LUA_VERSION_53` 这类条件编译，也不在业务代码里写版本判断。
- 接受不抹平的脚本语义差异：5.3 没有 `<close>`、`coroutine.close`、generational GC、table `__gc`。

**平台与 RID 分发**

| 平台 | RID | 原生库 |
| :-- | :-- | :-- |
| Windows x64 | `win-x64` | `artifacts/native/win-x64/lualoom.dll` |
| Linux x64 | `linux-x64` | `artifacts/native/linux-x64/liblualoom.so` |
| macOS arm64 | `osx-arm64` | `artifacts/native/osx-arm64/liblualoom.dylib` |

- 加载顺序见算法第 1 条；`RuntimeInformation.RuntimeIdentifier` 决定优先查找的 RID 目录。
- `LuaLoom.Native.csproj` 把当前 RID 的原生库复制到输出目录，`LuaNativeRid` 可由 MSBuild 属性覆盖，便于交叉构建。
- CMake 统一目标名 `lualoom`，Unix 自动加 `lib` 前缀与 `.so`/`.dylib` 后缀，启用 `POSITION_INDEPENDENT_CODE`，`LUA_BUILD_AS_DLL` 只在 Windows 定义。
- GitHub Actions 矩阵覆盖 `windows-2022`、`ubuntu-22.04`、`macos-14`，分别执行原生构建、编译、测试、生成物检查与示例；另有 `ubuntu-22.04` 的 Lua 5.3 job。
- 限制：Linux 产物最低 glibc 2.35；macOS `.dylib` 未签名；arm64 Windows/Linux 与移动端未覆盖。

**复杂度**：加载 O(1)（进程内缓存）；守卫 O(1)；`pcall` 封装 O(1)。

**接口契约与错误传播**

- `LuaApi` 全部方法 `internal static extern`，`CallingConvention.Cdecl`，参数只用 `IntPtr`、`int`、`double`、`long`、`ulong`、`string`、`byte[]`，不出现 `bool`（避免封送歧义，用 `int`）；`string` 形参统一标 `MarshalAs(UnmanagedType.LPUTF8Str)`，与 Lua 侧的 UTF-8 字节一致。
- `LuaApi`/`LuaStackGuard` 通过 `InternalsVisibleTo("LuaLoom.Core")` 与 `InternalsVisibleTo("LuaLoom.UnitTests")` 开放；生成代码不在此列，只能走 M002 的 `LuaBinding`。
- 本模块不捕获异常，只做转换；栈恢复由守卫负责，二者职责不重叠。

**资源**

- 原生库由 `scripts/build-native.ps1 -Version <53|54|55>` 产出到 `artifacts/native/<rid>/lualoom.*`，随构建复制到输出目录。
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
| 连续两次 `Load("lualoom")` | 观察 loader | 底层只加载一次，返回同一句柄 |
| Lua 5.3 构建 | `-p:LuaVersion=53` + 5.3 原生库 | 同一组用例通过，`Profile.Version` 为 `53` |
| 5.3 下协程 / 对象 userdata / 零 GC struct | 对应用例 | 与 5.4 行为一致 |
| Windows x64 / Linux x64 / macOS arm64 | CI 构建并运行 | 各自加载对应 RID 的库并通过 |
| 删除当前 RID 的原生库 | 创建 `LuaEnv` | 抛 `LuaLibraryNotFoundException`，路径含 `runtimes/<rid>/native` |
| 切换 Lua 版本 | `-Version 53` + `-p:LuaVersion=53` | RID 目录结构不变，仅库内容变化 |

异常覆盖四类：空与边界（空状态、栈顶 0）、非法输入（错误库名、语法错误）、依赖失败（DLL 缺失、错误码非 `Ok`）、重复与并发（重复加载、嵌套守卫、异常路径恢复）。
