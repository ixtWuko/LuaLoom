# F002 Lua 5.3 兼容（构建变体）

## 功能详细描述与预期

让同一套源码可以针对 Lua 5.4.9 或 Lua 5.3.6 构建。两个版本各自产出独立的原生库与托管构建，不在同一进程内混用。

**场景**

- 已有项目仍在使用 5.3，希望复用 LuaLoom 而不改 Lua 版本。
- CI 需要同时验证两套 ABI，保证绑定层没有依赖 5.4 独有接口。
- 团队升级 Lua 时，可以先切到 5.4，再逐步迁移脚本。

**预期**

| 编号 | 预期 | 观察方式 |
| :-- | :-- | :-- |
| E1 | 不传参数时仍按 5.4 构建，现有用例全绿 | 默认 `dotnet test` |
| E2 | `-p:LuaVersion=53` 构建出 5.3 版原生库，并链接到同一套 C# 代码 | 构建产物与 `LuaApi.Profile` 一致 |
| E3 | 5.3 下对象 userdata、协程、零 GC struct 均可用 | 同一组用例在 5.3 下通过 |
| E4 | 原生库按版本下载、校验、编译，互不覆盖 | `fetch-lua.ps1`/`build-native.ps1` 指定版本 |
| E5 | 版本差异只体现在 `LuaRuntimeProfile` 的实现里 | 业务代码检索不到版本判断 |
| E6 | 增加 5.5 只需补版本表与 profile，不改公共 API | 版本表新增条目后按同一流程构建 |

**非目标**

- LuaJIT（5.1 语义）。
- 同一进程同时加载两个 Lua 版本。
- 抹平脚本语义差异（`<close>`、`coroutine.close`、generational GC、table `__gc`）。
- 为每个版本单独维护一套测试用例。
- **当前不为 5.5 提供验证**：只在结构上预留，见「向 5.5 扩展」。 

## 使用与交互设计

构建 5.4（默认）：

```powershell
pwsh scripts/fetch-lua.ps1
pwsh scripts/build-native.ps1
dotnet build LuaLoom.sln -c Release
```

构建 5.3：

```powershell
pwsh scripts/fetch-lua.ps1 -Version 53
pwsh scripts/build-native.ps1 -Version 53
dotnet build LuaLoom.sln -c Release -p:LuaVersion=53
dotnet test  LuaLoom.sln -c Release -p:LuaVersion=53
```

| 版本 | 原生库 | MSBuild 属性 | 状态 |
| :-- | :-- | :-- | :-- |
| 5.4.9 | `artifacts/native/<rid>/lualoom.*` | 默认或 `54` | 已验证 |
| 5.3.6 | 同上，构建时替换为 5.3 版 | `53` | 已验证 |
| 5.5.x | 同上，构建时替换为 5.5 版 | `55` | 仅预留，未验证 |

原生库统一命名为 `lualoom`（Windows `.dll`、Linux `.so`、macOS `.dylib`），所以同一次构建只能使用一个版本；需要切换时重新构建原生库。DllImport 名称固定，不随版本变化。

## 具体实现细节

**版本画像**：不引入 `LUA_VERSION_53` 这类条件编译，也不在业务代码里写 `if (version == 53)`。差异收敛为一组 `LuaRuntimeProfile`：

| Profile | 原生入口 | 注册表伪索引 | 协程结果数 |
| :-- | :-- | :-- | :-- |
| `Lua53Profile` | `lua_newuserdata` | `-1001000` | `lua_gettop` |
| `Lua54Profile` | `lua_newuserdatauv` | `-1001000` | `lua_resume` 出参 |
| `Lua55Profile` | `lua_newuserdatauv` | `-(INT_MAX/2 + 1000)` | `lua_resume` 出参 |

- 公共 P/Invoke（30 多个）放在 `LuaApi`，库名固定为 `lualoom`，不随版本变化。
- 版本独有的 P/Invoke 分别放在 `Lua53Native` 与 `Lua54Native`（`Lua55Profile` 沿用 54 的签名）。
- 构建时通过程序集元数据写入 `LuaVersion`，运行期 `LuaApi.Profile` 从画像表里取对应实例。
- 新增 5.5 只需新增一个 profile 类与版本表条目，不改调用方、不加编译开关。

**需要适配的差异**

| 差异 | 5.3 | 5.4 / 5.5 | 收敛点 |
| :-- | :-- | :-- | :-- |
| 创建 userdata | `lua_newuserdata(L, size)` | `lua_newuserdatauv(L, size, 0)` | `LuaRuntimeProfile.NewUserdata` |
| 恢复协程 | `lua_resume(L, from, narg)` | `lua_resume(L, from, narg, &nresults)` | `LuaRuntimeProfile.Resume` |
| 注册表伪索引 | `-1001000` | 5.4 同为 `-1001000`；5.5 改为 `-(INT_MAX/2 + 1000)` | `LuaRuntimeProfile.RegistryIndex` |

其余调用（`lua_getglobal`、`lua_rotate`、`lua_rawlen`、`luaL_traceback`、`luaL_loadbufferx`、`lua_pcallk`、`luaL_ref` 等）在三个版本中签名一致。

**构建脚本**

- `scripts/lua-versions.json`：版本 → 源码包、URL、SHA-256 的映射表；新增版本只加一条。
- `fetch-lua.ps1 -Version <53|54|55>`：按表下载、校验、解压。
- `build-native.ps1 -Version <53|54|55>`：独立构建目录，产物按平台命名为 `lualoom.dll` / `liblualoom.so` / `liblualoom.dylib`，同一时间只有一个版本。
- `native/CMakeLists.txt` 接收 `LUA_VERSION`：选择源码目录、5.3 追加 `lbitlib.c`、输出名固定 `lualoom`。

**测试**：同一组用例通过 `-p:LuaVersion` 切换；CI 分别跑 5.4 与 5.3 两次。

**语义差异（接受，不处理）**

- 5.3 没有 `<close>`、`coroutine.close`、generational GC、table 的 `__gc`。
- 两个版本的错误文案可能不同，用例只断言状态、行号与自有错误信息。

## 测试方案

| 输入 | 操作 | 预期 |
| :-- | :-- | :-- |
| 默认构建 | `dotnet test` | 82 条全绿，`LuaApi.Profile.Version` 为 `54` |
| `-p:LuaVersion=53` + 5.3 版原生库 | `dotnet test` | 同一组用例全绿 |
| 原生库缺失 | 构建后运行 | `LuaLibraryNotFoundException` 列出 `lualoom` 的搜索路径 |
| 5.3 下协程 | 两次 `Resume` | 与 5.4 结果一致 |
| 5.3 下对象 userdata | 构造、字段读写、`__gc` | 行为与 5.4 一致 |
| 5.3 下零 GC struct | 100 万次往返 | 分配量为 0 |

## 与基线的边界

- 公共 API 不变；新增的是构建属性与脚本参数。
- 默认版本仍是 5.4.9，现有使用方不受影响。
- 文档的支持矩阵从「仅 5.4」更新为「5.3.6 / 5.4.9，构建期二选一」。
- 5.3 专属的语义差异写入 README 的已知限制。
