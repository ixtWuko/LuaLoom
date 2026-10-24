# F003 原生库按 RID 分发

## 功能详细描述与预期

把原生库从「写死 Windows x64」改为按运行时标识（RID）组织：构建产物落到 `artifacts/native/<rid>/`，加载器按当前进程的 RID 选择文件，包布局与 NuGet 的 `runtimes/<rid>/native/` 一致。

**场景**

- 使用方在 Windows / Linux / macOS 上引用同一套托管库，不需要自己改路径。
- CI 在三个平台上分别构建、测试，产出各自的 `lualoom` 动态库。
- 后续发布 NuGet 包时，直接按 RID 目录打包，不需要再改加载逻辑。

**预期**

| 编号 | 预期 | 观察方式 |
| :-- | :-- | :-- |
| E1 | 构建产物位于 `artifacts/native/<rid>/`，文件名符合平台约定 | `win-x64/lualoom.dll`、`linux-x64/liblualoom.so`、`osx-arm64/liblualoom.dylib` |
| E2 | 加载器按 `RuntimeInformation.RuntimeIdentifier` 查找 `runtimes/<rid>/native/` | 三个平台 CI 都能创建 `LuaEnv` |
| E3 | 缺失当前 RID 的库时，异常消息列出所有尝试路径 | `LuaLibraryNotFoundException.AttemptedPaths` |
| E4 | 自定义 `ILibraryLoader` 仍然优先于默认查找 | 单测注入自定义 loader |
| E5 | 同一套测试在三个平台通过 | GitHub Actions 矩阵全绿 |
| E6 | 公共 API 不变 | 无需修改调用方代码 |

**非目标**

- Windows arm64、Linux arm64（矩阵稳定后再加）。
- iOS / Android / WebGL（属于宿主适配包）。
- macOS 代码签名与公证。
- NuGet 发布本身（本功能只保证目录布局兼容）。

## 使用与交互设计

本地构建：

```powershell
pwsh scripts/fetch-lua.ps1
pwsh scripts/build-native.ps1
dotnet build LuaLoom.sln -c Release
```

脚本自动识别当前 RID：

| 平台 | 产物 |
| :-- | :-- |
| Windows x64 | `artifacts/native/win-x64/lualoom.dll` |
| Linux x64 | `artifacts/native/linux-x64/liblualoom.so` |
| macOS arm64 | `artifacts/native/osx-arm64/liblualoom.dylib` |

加载器查找顺序：

1. 环境变量 `LUALOOM_NATIVE_DIR` 指定目录下的库文件；
2. 裸库名（交给系统默认搜索）；
3. 程序目录；
4. 程序目录下的 `runtimes/<rid>/native/`；
5. 程序集所在目录；
6. 程序集目录下的 `runtimes/<rid>/native/`。

## 具体实现细节

**RID 与文件名**：`RuntimeInformation.RuntimeIdentifier` 给出 `win-x64`、`linux-x64`、`osx-arm64` 等；文件名按平台约定拼 `lualoom.dll`、`liblualoom.so`、`liblualoom.dylib`。

**构建脚本**：`build-native.ps1` 用 PowerShell 7 编写，三个平台共用。Windows 使用 Visual Studio 生成器（`-A x64`/`arm64`），Unix 使用 `Unix Makefiles` 并指定 `CMAKE_BUILD_TYPE=Release`。产物复制到 `artifacts/native/<rid>/`。

**CMake**：`lualoom` 目标统一命名，Unix 自动加 `lib` 前缀与 `.so`/`.dylib` 后缀；启用 `POSITION_INDEPENDENT_CODE`；`LUA_BUILD_AS_DLL` 只在 Windows 定义。

**托管侧**：`LuaLoom.Native.csproj` 按当前 RID 复制 `artifacts/native/<rid>/lualoom.*` 到输出目录；`LuaNativeRid` 可由 MSBuild 属性覆盖，便于交叉构建。

**CI**：GitHub Actions 矩阵包含 `windows-latest`、`ubuntu-22.04`、`macos-14`，每个平台执行原生构建、编译、测试、生成物检查与示例。

## 测试方案

| 输入 | 操作 | 预期 |
| :-- | :-- | :-- |
| Windows x64 | 构建并运行全部用例 | 通过，加载 `win-x64/lualoom.dll` |
| Linux x64 | CI 构建并运行全部用例 | 通过，加载 `linux-x64/liblualoom.so` |
| macOS arm64 | CI 构建并运行全部用例 | 通过，加载 `osx-arm64/liblualoom.dylib` |
| 删除当前 RID 的原生库 | 创建 `LuaEnv` | 抛 `LuaLibraryNotFoundException`，路径列表含 `runtimes/<rid>/native` |
| 自定义 `ILibraryLoader` | 注册后创建环境 | 自定义 loader 被调用，默认候选不再尝试 |
| 切换 Lua 版本 | `-Version 53` + `-p:LuaVersion=53` | 目录结构不变，仅库内容变化 |

## 与基线的边界

- 公共 API 不变；构建产物路径与加载顺序属于实现细节。
- README 的支持矩阵从「仅 Windows x64」更新为「Windows x64、Linux x64、macOS arm64（CI 验证）」。
- glibc 基线：Linux 产物在 Ubuntu 22.04 上构建，最低 glibc 2.35；更老的发行版暂不支持，写入已知限制。
