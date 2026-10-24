# LuaLoom 使用说明

面向使用者的 API 与行为约定。开发与设计细节见 [总体文档](overall.md)。

## 1. 初始化与销毁

```csharp
using var env = new LuaEnv();

var sum = env.DoString("return 1 + 1")[0];
```

`LuaEnv` 对应一个 Lua 虚拟机（版本由构建决定），建议全局一个实例。`Dispose` 关闭虚拟机，之后所有调用抛 `ObjectDisposedException`。

选项：

| 选项 | 默认 | 说明 |
| :-- | :-- | :-- |
| `SearchPaths` | `["?.lua"]` | 初始 `package.path` |
| `LibraryLoader` | `NativeLibraryLoader` | 原生库加载器 |
| `Logger` | 无 | `LogLevel` + 消息 |
| `EnableReflectionFallback` | `true` | 未注册类型是否走反射 |
| `CheckThreadAffinity` | `true` | 是否校验调用线程 |

## 2. 执行脚本与加载器

```csharp
env.AddLoader((ref string name) =>
{
    var path = Path.Combine("scripts", name.Replace('.', '/') + ".lua");
    if (!File.Exists(path)) return null;      // 交给下一个 loader
    name = Path.GetFullPath(path);            // 供调试信息使用
    return File.ReadAllBytes(path);
});

env.DoString("require 'main'", "bootstrap");
```

- `DoString` 返回 `object[]`，无返回值时是空数组。
- `LoadString` 只编译不执行，返回 `LuaFunction`。
- loader 返回 `null` 表示未命中；全部未命中时 `require` 抛 `LuaException`。

## 3. C# 调用 Lua

```csharp
var name = env.Global.Get<string>("player_name");
var table = env.Global.Get<LuaTable>("config");
using (table)
{
    var hp = table.Get<int>("hp");
    var nested = table.GetInPath<int>("stats.attack");
    table.Set("hp", 120);
}
```

`LuaTable.Cast<T>` 把表按值拷贝成 C# 对象，支持类、struct、`Dictionary`、`List`、数组与接口。修改 C# 对象不会回写 Lua 表。

```csharp
var config = table.Cast<GameConfig>();
```

调用 Lua 函数：

```csharp
using var fn = env.Global.Get<LuaFunction>("on_update");
fn.Call(0.016);                       // 慢路径，返回 object[]；错误带 traceback
```

高频调用应把函数映射为 delegate（需要在 `csharpCallLua` 中配置），泛型 delegate 也一样：

```json
{
  "csharpCallLua": ["Game.IntCallback", "System.Action<int>", "System.Func<int, string>"]
}
```

```csharp
var onScore = env.Global.Get<Action<int>>("on_score");
onScore(10);
```

## 4. Lua 调用 C#

```lua
local p = CS.MyGame.Player(5)   -- 构造
p.Hp = 10                       -- 字段/属性
print(p:Add(1, 2))              -- 实例方法
print(p:Triple(2))              -- 扩展方法
print(CS.MyGame.Player.Double(4)) -- 静态方法
```

反射兜底覆盖成员访问、操作符、索引器、事件、泛型方法与 `out`/`ref`/`params`：

```lua
local c = a + b                 -- op_Addition
local v = bag[1]                -- this[int]
emitter:Changed('+', fn)        -- 订阅事件
local f = lua_loom_generic(CS.MyGame.Utils, 'Convert', CS.System.Int32)

local ok, value = CS.MyGame.Parsing.TryParse('42')  -- 返回值 + out
local next = CS.MyGame.Parsing.Bump(1)               -- ref 写回
local text = CS.MyGame.Parsing.Join('-', 1, 2, 3)    -- params 展开

local t = typeof(CS.MyGame.Player)                    -- Type 对象
local same = cast(p, t)                               -- 类型视图
local max = uint64.parse('18446744073709551615')      -- 无符号 64 位
local box = xlua.get_generic_method(CS.MyGame.Utils, 'Convert')
```

生成绑定覆盖热点成员、单方法的 `ref`/`out`/`params`、默认值参数以及受支持元素类型的一维数组；未生成的成员（包括事件、未配置基类、复杂重载组、类元素数组）会自动回落到反射。绑定自己返回 `nil` 的成员不会被误判为「未处理」。派生类只生成自身成员；基类也需要加入 `luaCallCSharp` 才会生成继承成员。非字符串键索引器由生成绑定优先处理。扩展方法所在的静态类需要加入 `luaCallCSharp`，生成的注册代码会调用 `RegisterExtensionType`；未配置的扩展方法不可见。

同名重载由反射按参数类型评分：精确类型优先，其次可转换类型；生成器对常见类型（字符串、布尔、整数、数值、userdata、`object`）生成类型条件分派，单方法默认值参数按实参个数补齐，未覆盖的组合回退反射。

## 5. delegate 与 interface

C# 侧把 Lua 函数当 delegate 用，需要在生成配置的 `csharpCallLua` 中列出该 delegate 类型：

```csharp
var callback = env.Global.Get<IntCallback>("callback");
```

接口同理，生成实现优先；未生成时用 `DispatchProxy` 兜底（`EnableReflectionFallback = true`）。

## 6. 协程

```csharp
using var fn = (LuaFunction)env.DoString("return function(a) ... end")[0]!;
using var co = fn.CreateCoroutine();

var first = co.Resume(1);        // 返回 yield 的值
var last = co.Resume(5);         // 返回 return 的值
```

`LuaCoroutine.Status` 为 `Suspended` 或 `Dead`；对已结束的协程再 `Resume` 抛 `InvalidOperationException`。

## 7. 诊断

```csharp
env.StartProfile();
env.DoString("require('hot_path')()", "bench");
var report = env.StopProfile(ProfileSort.Total);

var memory = env.MemorySnapshot(top: 10);
```

- 报告为等宽文本，函数耗时用 `Stopwatch` 计时。
- 采集期间 Lua hook 开启，业务耗时会放大，只在诊断时使用。

## 8. 线程与生命周期

- 虚拟机与所有句柄都是单线程的：跨线程调用抛 `InvalidOperationException`，`Dispose` 也一样。
- 句柄实现 `IDisposable`，建议 `using`；漏掉的由终结器入队，调用 `env.Tick()` 在宿主线程回收。
- 句柄释放后使用抛 `ObjectDisposedException`；环境释放后使用任何句柄同样抛该异常。

## 9. 异常

| 类型 | 场景 |
| :-- | :-- |
| `LuaException` | 脚本编译或运行错误；带 `Status`、`ChunkName`、`Line`、`Traceback` |
| `LuaConversionException` | Lua 值与目标类型不兼容 |
| `LuaLibraryNotFoundException` | 原生库加载失败 |
| `ObjectDisposedException` | 环境或句柄已释放 |
| `InvalidOperationException` | 跨线程调用、协程已结束、诊断重复启动 |
