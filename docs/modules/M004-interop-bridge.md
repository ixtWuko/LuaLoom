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
- 绑定只覆盖部分成员时，未命中的键回落到反射：继承成员、事件、含 `out`/`ref`/`params` 的方法仍可调用。
- `out` / `ref` / `params`：`TryParse('42')` 返回 `true, 42`，`Bump(1)` 返回 `2`，`Join('-', 1, 2, 3)` 得到 `"1-2-3"`。
- 扩展方法：`p:Triple(2)` 转发到已注册的 `PlayerExtensions.Triple(p, 2)`，实例作为第一个参数；未注册的扩展类不可见。
- 操作符：配置类型优先使用生成的 `Operators`，未命中再走反射；支持 `+ - * / %`、一元 `-`、`==`、`<`、`<=`。
- xLua 兼容 API：`typeof`、`cast`、`uint64.*`、`xlua.get_generic_method`。

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
| E5 | 反射重载按「精确类型 > 可转换 > 默认值」评分；生成路径按 Lua 类型条件分派，无法覆盖时回退反射 | `Pick(string)` / `Pick(object)`、`Pick(int)` / `Pick(long)` |
| E6 | 反射兜底与生成路径对同一成员调用结果一致 | 同一用例跑两遍比对 |
| E7 | 反射兜底里 C# 抛出的异常转成 Lua 错误，脚本可 `pcall` 捕获 | 脚本断言 |
| E8 | `Cast<T>` 是值拷贝，C# 侧修改不回写原表 | 修改后读表值不变 |
| E9 | `CS.Namespace.Type(...)` 能构造已注册类型的对象、访问静态成员；未注册类型走反射仍可用 | 脚本调用断言 |
| E10 | `+ - * / %`、一元 `-`、`==`、`<`、`<=` 转发到 C# 运算符；配置类型优先走生成 thunk | 构造带运算符的类型断言结果 |
| E11 | 非字符串键访问优先走绑定索引器，未命中再反射 `this[...]` | `bag[1] = 5; return bag[1]` |
| E12 | `('+', fn)` 订阅、`('-', fn)` 退订；配置类型优先走生成的事件 thunk，未配置回退反射 | 计数用例验证退订后不再触发 |
| E13 | `lua_loom_generic(type, 'Method', T1, ...)` 闭合泛型方法并调用，静态与实例都可用 | 两个用例分别验证 |
| E14 | 基本类型数组与 `List` 读取走专用路径，不逐元素装箱 | `int[1000]` 分配量小于 8KB |
| E15 | 绑定只覆盖部分成员时，未命中的键回落到反射，继承成员与事件仍可用 | 只绑定 `Hp` 后读 `Name`、订阅事件 |
| E16 | `out` 不消耗实参、`ref` 消耗实参；生成绑定与反射都按「返回值 → `ref`/`out`」返回 | `TryParse('42')` 返回 `true, 42`；`Bump(1)` 返回 `2` |
| E17 | `params` 把剩余实参打包成数组，允许 0 个实参；单方法组可由生成绑定处理 | `Join('-', 1, 2, 3)` 与 `Sum()` |
| E18 | 实例成员未命中时可调用扩展方法，实例作为首参 | `p:Triple(2)` 返回 `p.Hp * 2` |
| E19 | 一维数组参数/返回值可由生成绑定编解码，元素类型受支持 | `Sum(int[])` 与 `Double(int[])` |
| E20 | xLua 兼容 API：`typeof`、`cast`、`uint64.*`、`xlua.get_generic_method`、`xlua.private_accessible`、`xlua.structclone` | 对应脚本断言 |
| E21 | Lua table 作为方法参数时自动转换为 class/struct/interface | `PointMath.Sum({ X = 1, Y = 2 })` |

**非目标**

- 自动同步：`Cast<T>` 是值拷贝，C# 侧修改不回写 Lua 表。
- 动态类型与 `ExpandoObject` 映射。
- 自定义集合适配（只支持 `Dictionary`、`List` 与数组）。
- 泛型方法的直接调用（不在基线，后续按需求评估）。
- 生成路径的 `ref`/`out`/`params` thunk（先由反射承担）。
- 泛型扩展方法与扩展方法组的歧义消解（只取第一个匹配的公开静态方法）。

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
    public LuaCSFunction? Call { get; init; }             // __call；返回 0 表示未处理
    public LuaCSFunction? StaticIndex { get; init; }      // (类表, 键)；返回 0 表示未处理
    public LuaCSFunction? StaticNewIndex { get; init; }   // (类表, 键, 值)；返回 1 + nil 表示未处理
    public LuaCSFunction? InstanceIndex { get; init; }    // (userdata, 键)；返回 0 表示未处理
    public LuaCSFunction? InstanceNewIndex { get; init; } // (userdata, 键, 值)；返回 1 + nil 表示未处理
    public LuaCSFunction? InstanceIndexer { get; init; }  // 非字符串键索引器读取
    public LuaCSFunction? InstanceIndexerSet { get; init; } // 非字符串键索引器写入
}

public sealed partial class LuaEnv
{
    public void RegisterType(LuaTypeBinding binding);
    public void RegisterDelegate(Type delegateType, ILuaDelegateInvoker invoker);
    public void RegisterInterface(Type interfaceType, Func<LuaTable, object> factory);
    public void RegisterExtensionType(Type extensionType);
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
5. 反射兜底（Lua 调 C#）：按成员名查 `PropertyInfo`/`FieldInfo`/`MethodInfo`/`EventInfo`；绑定 thunk 返回未处理时进入这一层，事件与索引器沿用各自的闭包路径。
6. 句柄的释放与终结器由 M002 负责，本模块只消费引用 id。
7. 重载解析：反射路径按参数形状装配并评分，精确类型优先，其次可转换类型，最后是可使用默认值的候选；得分最高者胜出，同分取声明顺序第一个。生成路径对同参数个数重载按 Lua 类型条件分派，无法覆盖的组合回退反射。
8. 类型解析：先查已注册的 `LuaTypeBinding`，再按 `EnableReflectionFallback` 走反射，最后抛 Lua 错误；类表统一安装带 `fqn` 的分派入口，绑定未命中的键继续走反射。
9. 参数装配：`out` 填默认值且不消耗实参，`ref` 与普通参数各消耗一个，`params` 把剩余实参转换后写入数组；调用后先压返回值（非 `void`），再按参数顺序压 `ref`/`out`，`in` 不回写。单方法组由生成绑定执行同一规则，复杂重载组回退反射。
10. 操作符：实例 metatable 上挂 `__add`/`__sub`/`__mul`/`__div`/`__mod`/`__unm`/`__eq`/`__lt`/`__le`；先查配置类型的生成 `Operators`，未命中再按名称查 `op_*` 静态方法。`__unm` 按 Lua 约定收到两个相同操作数，只取第一个匹配单参数方法；`__eq` 无运算符时退回引用比较。
11. 索引器：键不是字符串时先查绑定的 `InstanceIndexer`/`InstanceIndexerSet`，未处理再走 `this[...]` 反射；字符串键仍按成员名解析。
12. 事件：配置类型优先返回生成的订阅 thunk，未生成时回退 `EventInfo` 反射。`('+', fn)` 用生成的委托 lambda 或 `LuaEventDelegateFactory` 包装 Lua 函数，`('-', fn)` 按 `lua_topointer` 找到同一函数对应的委托并移除，随后释放函数句柄。订阅关系存放在 `ConditionalWeakTable`，对象被回收时随之释放。
13. 泛型方法：`lua_loom_generic(type, methodName, T1, ...)` 解析泛型定义，按类型参数个数匹配，`MakeGenericMethod` 闭合后返回调用器；静态方法参数从索引 1 开始，实例方法索引 1 是对象、参数从 2 开始。
14. 扩展方法：实例成员未命中时，先查生成器注册的 `RegisterExtensionMethod` 分派表；未命中再在 `RegisterExtensionType` 注册过的 public static 类中查找 `ExtensionAttribute` 方法。要求首参类型可由实例类型赋值；反射扫描结果按 `(Type, Name)` 缓存。未注册的扩展类不会被扫描或访问。
15. xLua 兼容 API：`typeof` 返回 `Type` 对象；`cast` 校验对象可赋值后返回同一对象视图；`uint64.*` 用无符号位模式实现解析、比较、除法与取模；`xlua.get_generic_method` 是 `lua_loom_generic` 的 `xlua` 命名空间别名；`xlua.private_accessible` 把类型加入非公共成员可见集合；`xlua.structclone` 复制原始 struct userdata 字节。

**复杂度**：`Cast` O(成员数)（缓存后不再反射查找）；delegate 调用 O(1)；反射兜底按成员名查找，未做跨调用缓存。

**接口契约与错误传播**

- `Cast<T>` 只做值拷贝，文档与示例中明确标注不回写。
- 缺失键保留默认值；成员类型不匹配抛 `LuaConversionException`，消息含类型全名与键名。
- 方法参数为 class/struct/interface 且 Lua 侧给 table 时，按 `LuaTable.Cast<T>` 的规则转换；table 到 `object`/`LuaTable` 不做隐式转换。
- 生成的 invoker 与 `DispatchProxy` 代理各自做参数转换，规则同源于 M002 的 `Read`/`Push`；两条路径的行为由同一组用例覆盖。
- 绑定只提供部分 thunk 时（例如没有生成 `__call`），缺失的入口回落到反射实现，不会留下一张调不通的类表。
- 绑定 thunk 的「未处理」约定：`Call` / `StaticIndex` / `InstanceIndex` 返回 0 且不压栈；`StaticNewIndex` / `InstanceNewIndex` 压入 `nil` 并返回 1。返回值本身是 `nil` 时必须返回 1，否则会被误判为未处理并触发回落。
- 反射调用按参数形状装配：普通参数与 `ref` 消耗实参，`out` 不消耗，`params` 把剩余实参打包成数组；调用后依次返回方法返回值、`ref` 与 `out`。单方法组由生成绑定执行同一规则；与其他重载混合的复杂组跳过生成，由上述回落路径处理。
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
| 同名重载 `Foo(int)` 与 `Foo(string, out int)` | Lua 传 `"abc"` | 选中可装配的 `Foo(string, out int)` |
| 重载 `Pick(string)` 与 `Pick(object)` | Lua 传 `'x'` | 选中 `Pick(string)` |
| 重载 `Pick(int)` 与 `Pick(long)` | Lua 传 `1` | 选中 `Pick(long)` |
| 单参数带默认值 | `Optional()` | 返回默认值 `7` |
| 扩展方法 `Triple(this Player, int)` | `p:Triple(2)` | 返回 `p.Hp * 2`；生成注册优先 |
| 绑定的非字符串索引器 | `bag[1] = 5; return bag[1]` | 走 `InstanceIndexer`/`InstanceIndexerSet` |
| `int[]` 参数与返回值 | `Sum({1,2,3})` 与 `Double({1,2})` | 生成绑定与反射结果一致 |
| 配置类型的 `__add` | `Money(1) + Money(2)` | 走生成的 `Operators` |
| 配置类型的事件 | `emitter:Changed('+', fn)` / `('-', fn)` | 走生成的 `Event_*` thunk |
| `typeof`/`cast`/`uint64` | 脚本调用 | 返回 `Type`、对象视图与无符号结果 |
| `xlua.get_generic_method` | 调用泛型方法 | 与 `lua_loom_generic` 结果一致 |
| `xlua.private_accessible` | 访问私有字段/方法 | 标记后可见，标记前不可见 |
| `xlua.structclone` | 克隆 struct userdata | 副本内容与原件一致 |
| table 作为 class 参数 | `Sum({ X = 1, Y = 2 })` | 自动转成 `Point` 后返回 `3` |
| 只绑定 `Hp` 的 `Player` | 读 `Name`、调 `Join` | 回落反射成功 |
| 空绑定 + `Emitter` | 订阅 `Changed` 并 `Raise` | 回调收到参数 |
| `TryParse(string, out int)` | 传 `"42"` | 返回 `true, 42` |
| `Bump(ref int)` | 传 `1` | 返回 `2` |
| `Join(string, params object[])` | 传 `'-', 1, 2, 3` | 返回 `"1-2-3"` |
| 同一成员分别走生成与反射 | 调两次比对 | 返回值与副作用一致 |
| 反射兜底里方法抛异常 | 脚本 `pcall` 调用 | 捕获到 Lua 错误，消息含原异常文本 |
| 已释放句柄 | 调 `Cast` | 抛 `ObjectDisposedException` |
| 脚本执行 `CS.Sample.Player(1)` | 运行 | 返回 userdata，字段与方法可访问 |
| 脚本访问未注册类型 | 运行 | 反射兜底生效，输出一次告警 |

异常覆盖四类：空与边界（空表、缺失键、空数组）、非法输入（类型不符、方法签名不符）、依赖失败（delegate 未生成、代理类型不可用）、重复与并发（重复释放、终结器与 `Dispose` 竞争、回调内释放）。
