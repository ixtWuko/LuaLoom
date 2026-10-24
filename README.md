# LuaLoom

在 .NET 中嵌入 Lua 5.3 / 5.4，让 C# 与 Lua 双向调用。

LuaLoom 的核心不依赖 Unity：虚拟机封装、类型编解码、绑定生成各自成层，宿主差异通过接口注入。对外 API 借鉴 [xLua](https://github.com/Tencent/xLua) 的使用习惯，已有 xLua 经验的团队迁移成本较低。

## 特性

- **多版本构建**：同一套源码在构建时选择 Lua 5.3.6 或 5.4.9，5.5 预留画像。
- **双向调用**：Lua 通过 `CS.*` 构造对象、访问成员；C# 通过 `Global.Get`、`LuaTable`、delegate 调用 Lua。
- **值类型零分配**：纯值类型 struct 与枚举在 Lua↔C# 间往返不产生托管分配。
- **生成绑定 + 反射兜底**：开发期无需生成即可运行，发布期用生成的强类型绑定替换反射路径。
- **句柄显式释放**：`IDisposable`，漏掉的由终结器入队，`Tick()` 在宿主线程回收。
- **单线程契约**：跨线程调用直接抛异常，不做隐式加锁。
- **附带工具**：Lua 函数耗时统计与内存快照、协程桥接、`lua-loom-gen` 生成器。

## 环境要求

- .NET SDK 8.0 或更高；运行 `net8.0` 目标需要 .NET 8 运行时
- CMake 3.20 以上
- C 工具链：Windows 用 Visual Studio 2022，Linux/macOS 用系统自带的 gcc/clang 与 make
- PowerShell 7

平台与验证范围：

| 平台 | 状态 |
| :-- | :-- |
| Windows x64 | 本地验证 |
| Linux x64 | GitHub Actions 工作流已配置（Ubuntu 22.04，glibc ≥ 2.35） |
| macOS arm64 | GitHub Actions 工作流已配置（macOS 14） |
| Windows arm64 / Linux arm64 | 未覆盖 |

Lua 版本在构建时选择：5.3.6 或 5.4.9，默认 5.4.9；5.5 预留了画像，尚未验证。

## 从源码构建

默认（Lua 5.4）：

```powershell
pwsh scripts/fetch-lua.ps1
pwsh scripts/build-native.ps1       # 产出 artifacts/native/<rid>/lualoom.*
dotnet build LuaLoom.sln -c Release
dotnet test  LuaLoom.sln -c Release
```

Lua 5.3：

```powershell
pwsh scripts/fetch-lua.ps1 -Version 53
pwsh scripts/build-native.ps1 -Version 53
dotnet build LuaLoom.sln -c Release -p:LuaVersion=53
dotnet test  LuaLoom.sln -c Release -p:LuaVersion=53
```

原生库统一命名为 `lualoom.dll`，同一次构建只对应一个 Lua 版本；切换版本时重新执行上面的原生构建。

运行示例：

```powershell
dotnet run --project samples/LuaLoom.Sample -c Release
```

## 快速开始

```csharp
using LuaLoom;

using var env = new LuaEnv();
var values = env.DoString("return 1 + 1");
Console.WriteLine(values[0]);          // 2
```

```lua
local player = CS.MyGame.Player(5)
player.Hp = 10
print(player:Add(1, 2))
print(CS.MyGame.Player.Double(4))
```

### C# 调用 Lua

```csharp
var hp = env.Global.Get<int>("player_hp");

using var config = env.Global.Get<LuaTable>("config");
var settings = config.Cast<GameSettings>();      // 值拷贝

using var update = env.Global.Get<LuaFunction>("on_update");
update.Call(0.016);
```

### 脚本加载

```csharp
env.AddLoader((ref string name) =>
{
    var path = Path.Combine("scripts", name.Replace('.', '/') + ".lua");
    if (!File.Exists(path)) return null;         // 交给下一个 loader
    name = Path.GetFullPath(path);               // 供调试信息使用
    return File.ReadAllBytes(path);
});

env.DoString("require 'main'", "bootstrap");
```

### 生成绑定

未生成绑定的类型走反射，适合开发期；发布前为热点类型生成强类型绑定：

```json
{
  "assembly": "artifacts/bin/MyGame/release/MyGame.dll",
  "output": "src/MyGame/Generated",
  "namespace": "MyGame.Generated",
  "luaCallCSharp": ["MyGame.Player"],
  "csharpCallLua": ["MyGame.IntCallback"]
}
```

```powershell
lua-loom-gen generate --config lua-loom.json
lua-loom-gen check    --config lua-loom.json   # CI 校验生成物是否过期
```

消费方启动时注册：

```csharp
LuaBindings.Register(env);
```

### 协程与诊断

```csharp
using var coroutine = function.CreateCoroutine();
var first = coroutine.Resume(1);                 // yield 的值
var last = coroutine.Resume(5);                  // return 的值

env.StartProfile();
env.DoString("require('hot_path')()", "bench");
Console.WriteLine(env.StopProfile(ProfileSort.Total));
Console.WriteLine(env.MemorySnapshot(top: 10));
```

## API 概览

| 类型 | 用途 |
| :-- | :-- |
| `LuaEnv` | 虚拟机入口：执行脚本、加载器、注册绑定、GC、Tick、诊断 |
| `LuaTable` / `LuaFunction` | table 与 function 句柄 |
| `LuaReference` | 句柄基类，实现 `IDisposable` |
| `LuaCoroutine` | 协程推进 |
| `LuaTypeBinding` / `ILuaDelegateInvoker` | 生成代码的注册契约 |
| `LuaException` / `LuaConversionException` | 错误类型 |

完整用法见 [docs/usage.md](docs/usage.md)。

## 目录

| 路径 | 内容 |
| :-- | :-- |
| `src/LuaLoom.Native` | P/Invoke、栈守卫、原生库加载 |
| `src/LuaLoom.Core` | 编解码、句柄、运行时、`CS.*` 桥、诊断 |
| `src/LuaLoom.CodeGen` | 配置模型与 Roslyn 生成器 |
| `src/LuaLoom.Cli` | `lua-loom-gen` 命令行 |
| `samples/` | 类型库、端到端示例、生成产物 |
| `tests/` | xUnit 用例 |

## 校验

```powershell
pwsh scripts/ci.ps1              # Lua 5.4
pwsh scripts/ci.ps1 -Version 53  # Lua 5.3
```

依次执行原生构建、编译、测试、生成物一致性检查与示例运行。

## 已知限制

- 平台：Windows x64 本地验证，Linux x64 与 macOS arm64 由 CI 验证；Windows arm64、Linux arm64、iOS/Android/WebGL 未覆盖。不含 LuaJIT。
- Lua 版本在构建时二选一（5.3.6 / 5.4.9），同一进程不能混用两个版本；5.5 仅预留画像。
- 虚拟机与句柄是单线程的，跨线程调用抛异常。
- 生成器覆盖 public 构造、字段、属性、非字符串索引器、方法（含同参数个数重载、单方法默认值/`ref`/`out`/`params`）、受支持元素类型的一维数组参数/返回值、操作符、非泛型与闭合泛型 delegate（如 `Action<int>`）以及接口；继承成员仅在基类也加入 `luaCallCSharp` 时生成。泛型类型、泛型方法、多维数组与类元素数组不在生成范围内，由反射路径处理。扩展类需要加入 `luaCallCSharp`，未配置的扩展方法不可见。绑定未命中的键会自动回落，不会变成 `nil`。已提供 `typeof`、`cast`、`uint64.*`、`xlua.get_generic_method`、`xlua.private_accessible`、`xlua.structclone` 等兼容 API；Lua table 作为方法参数时会按 `Cast<T>` 规则转换为 class/struct/interface。
- 未生成 delegate 时，`Get<TDelegate>` 会抛异常；接口有 `DispatchProxy` 兜底，事件委托由表达式树现场生成。
- 生成器会输出 `link.xml` 保留配置类型，供 Unity IL2CPP 裁剪使用；NativeAOT 的完整裁剪验证尚未覆盖。
- 不含热补丁、Unity 适配与资源版本管理。

## 文档

- [使用说明](docs/usage.md)
- [总体设计](docs/overall.md)
- 模块文档：`docs/modules/M001`–`M006`

## 相关项目

- [xLua](https://github.com/Tencent/xLua)：面向 Unity 场景的成熟绑定方案，本项目对外 API 参考了它的使用习惯。
- Lua 5.3.6 / 5.4.9：[lua.org](https://www.lua.org/)，采用 MIT 许可证。

## 许可证

[MIT](LICENSE)
