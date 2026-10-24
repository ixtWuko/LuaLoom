# M004 interop-bridge

## 功能详细描述与预期

把 Lua 的表和函数包装成 C# 句柄，并提供三座桥：Lua 表到 C# 类/结构/字典/列表、Lua 表到 C# 接口、Lua 函数到 C# delegate。同时提供 Lua 调 C# 的反射兜底路径。

**场景**

- C# 读取 Lua 配置表，映射成强类型对象。
- Lua 把回调函数传给 C#，C# 侧以 delegate 形式保存并调用。
- Lua 实现 C# 接口，交给 C# 模块使用（生成路径为主，`DispatchProxy` 兜底）。
- 未生成绑定的类型被 Lua 访问时，走反射兜底并输出告警日志。
- 建立 `CS` 命名空间表：`CS.Sample.Player(1)` 构造对象，`CS.Sample.Player.StaticField` 访问静态成员。
- 操作符与索引器：`Money(1) + Money(2)`、`money < other`、`bag[1] = 5` 转发到 C# 的运算符与索引器。
- 事件：`emitter:Changed('+', fn)` / `('-', fn)` 增删回调，回调参数与 C# 事件签名一致。
- 泛型方法：`lua_loom_generic(type, 'Method', T1, ...)` 返回已闭合的调用器，静态与实例方法都支持。

**边界**

- 不做值类型零拷贝（M002 负责），只负责按成员形状读写。
- 不生成代码（M005 负责），只消费生成物注册的桥。
- 不管理虚拟机生命周期（M003 负责）。

**预期（可观察）**

| 编号 | 预期 | 观察方式 |
| :-- | :-- | :-- |
| E1 | `Cast<T>` 支持类、struct、`Dictionary<K,V>`、`List<T>`、数组 | 五类各一条用例 |
| E2 | 接口映射生成路径返回实现类实例，方法调用直达 Lua 表 | 调方法后 Lua 侧计数变化 |
| E3 | 未生成 delegate 时抛 `LuaConversionException`，消息提示在 `lua-loom.json` 的 `csharpCallLua` 中加入该委托类型并重新生成 | 断言消息 |
| E4 | 未生成接口实现时 `DispatchProxy` 兜底可用，方法调用返回正确值 | 不生成接口绑定直接调用 |
| E5 | 重载按「精确匹配 > 可转换 > 默认值补齐」选择；无法区分时取声明顺序第一个并输出一次告警 | 构造三组重载用例 |
| E6 | 反射兜底与生成路径对同一成员调用结果一致 | 同一用例跑两遍比对 |
| E7 | 反射兜底里 C# 抛出的异常转成 Lua 错误，脚本可 `pcall` 捕获 | 脚本断言 |
| E8 | `Cast<T>` 是值拷贝，C# 侧修改不回写原表 | 修改后读表值不变 |
| E9 | `CS.Namespace.Type(...)` 能构造已注册类型的对象、访问静态成员；未注册类型走反射仍可用 | 脚本调用断言 |
| E10 | `+ - * / %`、一元 `-`、`==`、`<`、`<=` 转发到 C# 运算符 | 构造带运算符的类型断言结果 |
| E11 | 非字符串键访问走索引器 `this[...]`，读写与 C# 行为一致 | `bag[1] = 5; return bag[1]` |
| E12 | `('+', fn)` 订阅、`('-', fn)` 退订，事件触发时回调收到 C# 参数 | 计数用例验证退订后不再触发 |
| E13 | `lua_loom_generic(type, 'Method', T1, ...)` 闭合泛型方法并调用，静态与实例都可用 | 两个用例分别验证 |
| E14 | 基本类型数组与 `List` 读取走专用路径，不逐元素装箱 | `int[1000]` 分配量小于 8KB |

**非目标**

- 自动同步：`Cast<T>` 是值拷贝，C# 侧修改不回写 Lua 表。
- 动态类型与 `ExpandoObject` 映射。
- 自定义集合适配（只支持 `Dictionary`、`List` 与数组）。
- 泛型方法的直接调用（不在基线，后续按需求评估）。

## 使用与交互设计

**调用方**：宿主业务代码、M005 生成的注册代码。

**公共 API**

```csharp
// 句柄类型由 M002 定义，本模块以 partial 方式补充转换与桥接
public sealed partial class LuaTable
{
    public T Cast<T>();
    public void SetMetaTable(LuaTable meta);
}

public interface ILuaDelegateInvoker
{
    Delegate Create(LuaFunction function);
}

public sealed class LuaTypeBinding
{
    public Type Type { get; init; }
    public string FullName { get; init; }
    public LuaCSFunction? Call { get; init; }             // __call，参数从索引 2 开始
    public LuaCSFunction? StaticIndex { get; init; }      // (类表, 键)
    public LuaCSFunction? StaticNewIndex { get; init; }   // (类表, 键, 值)
    public LuaCSFunction? InstanceIndex { get; init; }    // (userdata, 键)
    public LuaCSFunction? InstanceNewIndex { get; init; } // (userdata, 键, 值)
}

public sealed partial class LuaEnv
{
    public void RegisterType(LuaTypeBinding binding);
    public void RegisterDelegate(Type delegateType, ILuaDelegateInvoker invoker);
    public void RegisterInterface(Type interfaceType, Func<LuaTable, object> factory);
}
```

**调用顺序**

```csharp
using var config = env.NewTable();
config.Set("hp", 100);
var player = config.Cast<PlayerConfig>();          // 值拷贝，不回写

using var handlers = env.Global.Get<LuaTable>("on_hit");
using var onHit = handlers.Get<LuaFunction>("callback");
var result = onHit.Call(1, "slash");                // 慢路径，返回 object[]

LuaBindings.Register(env);                          // 生成代码的注册入口，内部调 RegisterType
env.DoString("local p = CS.Sample.Player(1); p.Hp = 10", "init");
```

**异常表**

| 异常 | 触发条件 | 调用方处理 |
| :-- | :-- | :-- |
| `LuaConversionException` | 表形状与目标类型不符、delegate 未生成 | 修正配置或补生成 |
| `ObjectDisposedException` | 句柄已释放 | 调用方缺陷 |
| `TargetInvocationException` | 反射兜底调用的 C# 方法自身抛异常 | 包装成 Lua 错误供脚本捕获；穿越回 C# 时呈现为 `LuaException` |
| `InvalidOperationException` | 句柄属于其他环境 | 跨环境混用，调用方缺陷 |

**界面**：无界面，无界面状态。

## 具体实现细节

**数据结构**

`Cast<T>` 的支持矩阵：

| 目标类型 | 规则 |
| :-- | :-- |
| 类 | 无参构造 + 公共可写属性/字段逐项赋值 |
| struct | 逐字段赋值，缺失键保留默认值 |
| `Dictionary<K,V>` | 遍历表所有键值，逐个转换 |
| `List<T>` / `T[]` | 从 1 开始按 `Length` 读取 |
| 接口 | 优先用生成的实现类；否则 `DispatchProxy` 生成代理，方法调用转成表字段查找 |

`ILuaDelegateInvoker`（生成代码实现）：

| 成员 | 签名 | 说明 |
| :-- | :-- | :-- |
| `Invoke` | `object? Invoke(object?[] args)` | 把参数打包成 `object[]` 调 Lua，返回值再拆 |

成员缓存键：

| 字段 | 类型 | 含义 |
| :-- | :-- | :-- |
| `Type` | `Type` | 声明类型 |
| `Name` | `string` | 成员名 |
| `ParameterTypes` | `Type[]?` | 方法参数类型，属性为 `null` |

`CS` 表：

| 节点 | 结构 | 说明 |
| :-- | :-- | :-- |
| 根 | table + `__index` 元方法 | 按需解析命名空间 |
| 命名空间节点 | table，缓存已解析的子节点 | 解析结果常驻，不重复查找 |
| 类型节点 | table + `__call`/`__index`/`__newindex` | 分别对应构造、静态成员读、静态成员写 |

**算法**

1. `Cast<T>` 分派：类与 struct 走缓存好的成员赋值表逐项填充；`Dictionary`/`List`/数组走 M002 原语；接口走生成实现类，缺失时用 `DispatchProxy` 兜底。
2. 成员赋值表首次用反射构建，键为 `(Type, 表)`；后续直接调用缓存，不再走反射查找。
3. delegate 创建：按 delegate 类型查生成的 invoker；未命中抛 `LuaConversionException`，消息提示配置项与重新生成。创建后复用同一引用 id，不重复 `luaL_ref`。
4. 接口代理兜底：`DispatchProxy.Create<T, LuaInterfaceProxy>()`；`Invoke` 按方法名查表，参数与返回值走 M002，转换失败抛 `LuaConversionException`。
5. 反射兜底（Lua 调 C#）：按 `(Type, memberName, 参数类型)` 缓存 `MethodInfo`/`PropertyInfo`，命中后直接调用；同一成员只告警一次，避免刷屏。
6. 句柄的释放与终结器由 M002 负责，本模块只消费引用 id。
7. 重载解析：按实参个数先筛候选，再逐参数打分（精确类型 2 分、可隐式转换 1 分、默认值 0 分），取总分最高者；同分时取元数据声明顺序第一个，并输出一次告警。生成代码与反射兜底共用同一个解析器。
8. 类型解析：先查已注册的 `LuaTypeBinding`，再按 `EnableReflectionFallback` 走反射，最后抛 Lua 错误；结果缓存在 `CS` 表节点上，命名空间层级不重复解析。
9. 操作符：实例 metatable 上挂 `__add`/`__sub`/`__mul`/`__div`/`__mod`/`__unm`/`__eq`/`__lt`/`__le`，按名称查 `op_*` 静态方法并调用；`__unm` 按 Lua 约定收到两个相同操作数，只取第一个匹配单参数方法；`__eq` 无运算符时退回引用比较。
10. 索引器：键不是字符串时走 `this[...]`，读写各一个分支；字符串键仍按成员名解析。
11. 事件：`__index` 命中 `EventInfo` 时返回闭包；`('+', fn)` 用 `LuaEventDelegateFactory` 把 Lua 函数包成事件委托并登记，`('-', fn)` 按 `lua_topointer` 找到同一函数对应的委托并移除，随后释放函数句柄。订阅关系存放在 `ConditionalWeakTable`，对象被回收时随之释放。
12. 泛型方法：`lua_loom_generic(type, methodName, T1, ...)` 解析泛型定义，按类型参数个数匹配，`MakeGenericMethod` 闭合后返回调用器；静态方法参数从索引 1 开始，实例方法索引 1 是对象、参数从 2 开始。

**复杂度**：`Cast` O(成员数)（缓存后不再反射查找）；delegate 调用 O(1)；反射兜底 O(1) 命中缓存。

**接口契约与错误传播**

- `Cast<T>` 只做值拷贝，文档与示例中明确标注不回写。
- 缺失键保留默认值；成员类型不匹配抛 `LuaConversionException`，消息含类型全名与键名。
- 生成的 invoker 与 `DispatchProxy` 代理各自做参数转换，规则同源于 M002 的 `Read`/`Push`；两条路径的行为由同一组用例覆盖。
- 绑定只提供部分 thunk 时（例如没有生成 `__call`），缺失的入口回落到反射实现，不会留下一张调不通的类表。
- M005 接入前，`CS.*` 与成员访问全部走反射；`RegisterType` 已就位，生成的绑定在 M005 里优先于反射使用。
- `LuaInterfaceProxy` 必须是 `public`：`DispatchProxy` 在运行期生成子类，代理类型要能从动态程序集访问；它只作兜底。
- 事件委托用表达式树现场生成（`LuaEventDelegateFactory`），只服务事件；`Get<TDelegate>` 仍要求生成代码。
- 数组与 `List<T>` 的读取对 `int`/`long`/`double`/`float`/`bool`/`string` 走专用循环，不产生逐元素装箱；其他元素类型回落到逐项转换。

**资源**：无外部资源；生成的 invoker 与类型绑定在示例调用 `LuaBindings.Register(env)` 时注册进当前环境的缓存，不使用程序集级全局注册。

## 测试方案

| 输入 | 操作 | 预期 |
| :-- | :-- | :-- |
| 表 `{hp=100,name="p"}` | `Cast<PlayerConfig>()` | 两字段均正确 |
| 空表 | `Cast<PlayerConfig>()` | 缺失字段取默认值，不抛异常 |
| 表 `{1,2,3}` | `Cast<List<int>>()` | 得到 `[1,2,3]` |
| 表 `{1,2}` | `Cast<int[]>()` | 得到长度 2 的数组 |
| 表 `{hp="x"}` | `Cast<PlayerConfig>()` | 抛 `LuaConversionException`，消息含字段名 |
| `Cast` 后修改对象字段 | 读原表 | 表值不变 |
| 生成的接口实现 | 调方法 | Lua 侧计数变化，返回类型正确 |
| 未生成接口实现 | 调方法 | `DispatchProxy` 生效，返回正确 |
| 未生成的 delegate 类型 | `Get<Action<int>>` | 抛 `LuaConversionException`，提示配置 |
| 同名重载 `Foo(string)` 与 `Foo(object)` | Lua 传 `"a"` | 精确匹配优先，选中 `Foo(string)` |
| 同名的 `Foo(int)` 与 `Foo(double)` 重载 | Lua 传 `1` | 按声明顺序选中第一个，输出一次重载歧义告警 |
| 同一成员分别走生成与反射 | 调两次比对 | 返回值与副作用一致 |
| 反射兜底里方法抛异常 | 脚本 `pcall` 调用 | 捕获到 Lua 错误，消息含原异常文本 |
| 已释放句柄 | 调 `Cast` | 抛 `ObjectDisposedException` |
| 脚本执行 `CS.Sample.Player(1)` | 运行 | 返回 userdata，字段与方法可访问 |
| 脚本访问未注册类型 | 运行 | 反射兜底生效，输出一次告警 |

异常覆盖四类：空与边界（空表、缺失键、空数组）、非法输入（类型不符、方法签名不符）、依赖失败（delegate 未生成、代理类型不可用）、重复与并发（重复释放、终结器与 `Dispose` 竞争、回调内释放）。
