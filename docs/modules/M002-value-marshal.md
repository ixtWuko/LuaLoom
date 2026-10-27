# M002 value-marshal

## 功能详细描述与预期

把 C# 值与 Lua 值互相转换：标量、字符串、枚举、纯值类型 struct、引用对象、表与函数句柄。它是唯一允许 push/pop 的业务层，所有类型规则集中在这里。

**场景**

- Lua 调 C#：参数从栈上按目标类型读出，返回值按声明类型写回栈。
- C# 调 Lua：从表或函数返回值按指定类型读取，读取失败给出可定位的错误。
- 引用对象在跨语言往返时保持同一性（同一个 C# 对象进出多次，Lua 侧看到同一个 userdata）。

**边界**

- 不决定「哪些类型该生成代码」，那是 M005 的配置职责；本模块只提供生成代码调用的原语。
- 不实现 delegate 与 interface 的代理对象，属于 M004。
- 不管理虚拟机生命周期，属于 M003。

**预期（可观察）**

| 编号 | 预期 | 观察方式 |
| :-- | :-- | :-- |
| E1 | 整型、浮点、布尔、字符串、`nil` 往返后值相等 | 单测 `Push` 后 `Read` 比较 |
| E2 | `long`、`ulong` 走 5.4 原生 integer，不丢精度、不分配 | 边界值 `long.MinValue`/`ulong.MaxValue` 往返；分配计数为 0 |
| E3 | 纯值类型 struct 与枚举往返不产生托管分配 | 100 万次循环后 `GetAllocatedBytesForCurrentThread` 增量为 0 |
| E4 | 同一 C# 对象两次 push，Lua 侧 `rawequal` 为真 | 脚本断言两个 userdata 相等 |
| E5 | 类型不匹配时抛 `LuaConversionException`，消息含期望类型、实际 `LuaType` 与栈索引 | 触发后断言消息字段 |
| E6 | 句柄释放后引用 id 进入空闲表并被复用；未释放的句柄 id 不重复 | 连续创建/释放 1000 次，id 上界不超过峰值并发数 |
| E7 | `LuaTable.Get` 缺失键返回 `default(T)`；类型不匹配抛 `LuaConversionException` | 存在、缺失、错类型三条用例 |
| E8 | `GetInPath("a.b.c")` 等价于逐级读取，不产生中间句柄 | 调用前后引用计数相等 |
| E9 | 未 `Dispose` 的句柄在 GC 后由 `Tick()` 回收 | 弱引用在 `Tick` 后失效 |

**非目标**

- 含引用字段的 struct 既不做内联也不走装箱回退，直接抛 `NotSupportedException`。
- 自动同步：`Cast<T>` 是值拷贝，C# 侧修改不回写 Lua 表。

## 使用与交互设计

**调用方**：M003（初始化时构造）、M004（表转换与桥接）、M005 生成的代码（按类型调用原语）。

**公共句柄类型**

```csharp
public abstract class LuaReference : IDisposable
{
    public int Id { get; }
    public bool IsDisposed { get; }
    public void Dispose();
}

public sealed partial class LuaTable : LuaReference
{
    public int Length { get; }
    public T Get<T>(string key);
    public TValue Get<TKey, TValue>(TKey key);
    public void Get<TKey, TValue>(TKey key, out TValue value);
    public void Set<TKey, TValue>(TKey key, TValue value);
    public T GetInPath<T>(string path);
    public void SetInPath<T>(string path, T value);
}

public sealed class LuaFunction : LuaReference
{
    public object[] Call(params object[] args);
    public object[] Call(object[] args, Type[] returnTypes);
    public void SetEnv(LuaTable environment);
}
```

`LuaTable.Cast<T>` 由 M004 以 `partial` 方式补充。生成代码只允许调用 `LuaBinding` 的强类型原语，不接触 `LuaApi`，也不自行管理栈顶。`LuaFunction.Call` 出错时抛 `LuaException`，消息与 `Traceback` 包含 Lua 栈回溯。

```csharp
public static class LuaBinding            // 生成代码调用面，参数索引从 1 开始
{
    // 压栈
    public static void PushNil(IntPtr state);
    public static void PushInt(IntPtr state, int value);
    public static void PushLong(IntPtr state, long value);
    public static void PushDouble(IntPtr state, double value);
    public static void PushBoolean(IntPtr state, bool value);
    public static void PushString(IntPtr state, string? value);
    public static void PushObject(IntPtr state, object? value);
    public static void PushStruct<T>(IntPtr state, T value) where T : struct;
    public static void PushFunction(IntPtr state, LuaCSFunction function);
    public static void PushArray<T>(IntPtr state, T[]? values);
    public static void PushArray(IntPtr state, Array? values);

    // 读栈
    public static int GetTop(IntPtr state);
    public static int ReadInt(IntPtr state, int index);
    public static long ReadLong(IntPtr state, int index);
    public static byte ReadByte(IntPtr state, int index);
    public static sbyte ReadSByte(IntPtr state, int index);
    public static short ReadShort(IntPtr state, int index);
    public static ushort ReadUShort(IntPtr state, int index);
    public static uint ReadUInt(IntPtr state, int index);
    public static ulong ReadULong(IntPtr state, int index);
    public static char ReadChar(IntPtr state, int index);
    public static float ReadFloat(IntPtr state, int index);
    public static double ReadDouble(IntPtr state, int index);
    public static bool ReadBoolean(IntPtr state, int index);
    public static string? ReadString(IntPtr state, int index);
    public static object? ReadObject(IntPtr state, int index);
    public static T ReadObjectAs<T>(IntPtr state, int index);   // 含 userdata、table 转换与桥接读取
    public static T[] ReadArray<T>(IntPtr state, int index);
    public static T ReadStruct<T>(IntPtr state, int index) where T : struct;
    public static T ConvertObject<T>(object? value);

    // 句柄
    public static LuaFunction CreateFunction(IntPtr state, int index);
    public static IntPtr GetFunctionPointer(IntPtr state, int index);

    // 重载分派用的类型判断
    public static bool IsNil(IntPtr state, int index);
    public static bool IsBoolean(IntPtr state, int index);
    public static bool IsNumber(IntPtr state, int index);
    public static bool IsInteger(IntPtr state, int index);
    public static bool IsIntegerInRange(IntPtr state, int index, long minimum, long maximum);
    public static bool IsString(IntPtr state, int index);
    public static bool IsTable(IntPtr state, int index);
    public static bool IsFunction(IntPtr state, int index);
    public static bool IsUserdataOfType<T>(IntPtr state, int index);

    // 抛错：由 thunk 转成 Lua 错误
    public static void Throw(IntPtr state, string message);
}
```

上表是全部 42 个公共原语，新增成员必须同步登记。`LuaBinding` 自身不做栈守卫，调用方是核心的 `LuaCSFunction` thunk（见 M005）。`ReadInt` / `ReadByte` 等窄类型读取做范围检查，越界抛 `LuaConversionException`；`ReadString` 接受 string 与 number（按 `tostring` 语义），`ReadBoolean` 按 Lua 真值语义。

**调用顺序（生成代码视角）**

```csharp
// 由核心的 LuaCSFunction thunk 调用，thunk 已经套好栈守卫
static int Player_Sum(IntPtr L)
{
    var a = LuaBinding.ReadInt(L, 1);
    var b = LuaBinding.ReadInt(L, 2);
    LuaBinding.PushInt(L, Player.Sum(a, b));
    return 1;                                  // 返回给 Lua 的值个数
}
```

**异常表**

| 异常 | 触发条件 | 调用方处理 |
| :-- | :-- | :-- |
| `LuaConversionException` | 栈上类型与目标类型不兼容，或数值越界 | 转成 Lua 错误，脚本可捕获 |
| `NotSupportedException` | 目标类型没有注册 marshaler | 生成器应提前拦截，属配置缺陷 |
| `ObjectDisposedException` | 使用已释放的引用 id | 调用方缺陷 |
| `InvalidOperationException` | 引用 id 不属于当前环境 | 跨环境混用，调用方缺陷 |

**界面**：无界面。对外暴露句柄类型 `LuaReference`/`LuaTable`/`LuaFunction`、`ITypeMarshaler`、异常类型 `LuaConversionException`，以及生成代码专用的 `LuaBinding`，其余类型 `internal`。

## 具体实现细节

**数据结构**

`LuaReferenceTable`（每个环境一份）：

| 字段 | 类型 | 含义 |
| :-- | :-- | :-- |
| `_state` | `IntPtr` | 状态指针 |

id 的复用交给 `luaL_ref` 自带的空闲链，不再维护第二套。

`ObjectRegistry`（并入 `LuaMarshal` 实现）：

| 字段 | 类型 | 含义 |
| :-- | :-- | :-- |
| `_objectIds` | `Dictionary<object,int>`（`ReferenceEqualityComparer`） | 对象到 id |
| `_objects` | `List<object?>` | id 到对象，`__gc` 后置空 |
| `_freeObjectIds` | `Stack<int>` | 已回收的对象 id，push 时优先复用 |
| `_objectGate` | `object` | 保护上述三张表，`__gc` 回调与宿主线程共用 |
| `_objectCacheRef` | `int` | 弱值表的注册表引用，保存 id → userdata |

`LuaMarshal`：

| 字段 | 类型 | 含义 |
| :-- | :-- | :-- |
| `_state` | `IntPtr` | 状态指针 |
| `_references` | `LuaReferenceTable` | 引用 id 分配 |
| `_deferredReleases` | `ConcurrentQueue<int>` | 终结器入队、待 `Tick` 释放的 id |
| `_customMarshalers` | `List<ITypeMarshaler>` | 自定义类型扩展点 |
| `_objectIds` / `_objects` | 见上表 | 对象身份表 |

`LuaMarshalRegistry`：`IntPtr` → `LuaMarshal` 的静态边界索引。Lua 回调只拿到状态指针，靠它反查所属环境；环境释放时移除，不承载共享可变状态。

`ITypeMarshaler`（扩展点）：

| 成员 | 签名 | 说明 |
| :-- | :-- | :-- |
| `CanMarshal` | `bool CanMarshal(Type type)` | 是否处理该类型 |
| `Push` | `void Push(IntPtr L, object value)` | 写入栈 |
| `Read` | `object? Read(IntPtr L, int index, Type type)` | 从栈读取 |

`LuaReference`（不持有 `LuaEnv`，避免与 M003 形成反向依赖）：

| 字段 | 类型 | 含义 |
| :-- | :-- | :-- |
| `Marshal` | `LuaMarshal` | 所属环境的编解码器（`internal` 属性） |
| `Id` | `int` | 注册表引用 id（`public` 只读属性） |
| `_disposed` | `int` | `Interlocked` 释放标记 |
| 终结器 | — | 只把 id 入队到 `_deferredReleases`，不在 GC 线程调 Lua |

**类型映射规则**

| C# 类型 | Lua 表示 | 备注 |
| :-- | :-- | :-- |
| `sbyte`…`uint` | number | 直接转 `lua_Integer` |
| `long`、`ulong` | integer | 5.4 原生 64 位；`ulong` 超出 `long` 范围时按位重解释，读取时还原 |
| `float`、`double` | number | `float` 以 double 传递，读回时检查范围 |
| `bool` | boolean | |
| `string` | string | UTF-8 编码 |
| `byte[]` | string | 零拷贝读取需长度前缀，当前版本走复制 |
| 枚举 | integer | 底层类型转换 |
| `decimal` | userdata（内联 16 字节） | 生成路径用 `PushStruct`/`ReadStruct` 无损传递；反射路径把无 metatable 的 16 字节 userdata 还原为 decimal |
| 纯值类型 struct | userdata（内联字节） | 仅 blittable 字段，可嵌套；要求 `LayoutKind.Sequential` |
| 类实例 | userdata + 对象 id | `__gc` 时释放 id |
| `LuaTable`/`LuaFunction` | table/function | 通过引用 id 关联 |
| `null` | `nil` | 值类型不接受 `nil` |

读取为 `object`（未指定目标类型）时，`integer` 返回 `long`，`number` 返回 `double`，`string`/`boolean` 原样返回，`table`/`function` 返回句柄。

**算法**

1. `Push` 按 `Type` 分派：标量直接调 `lua_push*`；enum 按底层整数压栈；string 走 UTF-8；struct 由 `LuaBinding.PushStruct` 写入 userdata；引用对象查 `ObjectRegistry`，命中复用 userdata。
2. `Read<T>` 先看栈上 `LuaType`，与目标类型做兼容检查（`number`→整型需检查小数部分与范围），再转换；失败抛 `LuaConversionException`。
3. struct 读写不走托管对象：`RuntimeHelpers.IsReferenceOrContainsReferences<T>()` 判定失败直接抛 `NotSupportedException`；通过后按 `Unsafe.SizeOf<T>()` 分配 userdata，用 `Unsafe.Write/Read` 整块拷贝。
4. 对象身份：id → userdata 存在弱值表里，C# 侧不持引用；userdata 被回收时 `__gc` 回调把 id 从身份表里移除，允许 id 复用。若对象仍被 C# 持有，下次 push 会重建 userdata。
5. 句柄释放：在创建线程上 `Dispose` 立即 `luaL_unref`（C API 允许在回调内调用，不需要延迟）；在其它线程调用时改为把 id 入队，由 `Tick()` 在宿主线程释放；终结器无法在 GC 线程调 Lua，同样走入队路径。
6. 自定义类型：`RegisterMarshaler(ITypeMarshaler)` 追加到列表尾部，查找时从后往前，后注册者优先；`Push<T>` 与 `Read<T>`/`Read(Type)` 都会先查自定义 marshaler。

**复杂度**：标量 push/read O(1)；struct 按整块拷贝 O(1)（大小固定）；对象身份查找 O(1) 均摊；弱值表的大小随活跃对象数增长。

**接口契约与错误传播**

- 所有原语显式传状态指针 `IntPtr state` 与栈索引，不使用隐式「当前栈顶」状态，避免调用顺序耦合。
- 栈索引越界或类型不符统一抛 `LuaConversionException`，不做越界读取。
- `LuaTable.Get` 缺失键返回 `default(T)`（显式写入 `nil` 的键同样得到 `default`）；类型不匹配抛 `LuaConversionException`，字符串键重载 `Get<T>(string)` 的消息附带键名，泛型键重载 `Get<TKey,TValue>` 只带期望类型与实际类型。
- struct 内联读写使用 `unsafe`，工程开启 `AllowUnsafeBlocks`；`unsafe` 代码只允许出现在本模块。
- 含引用字段的 struct 没有内联或装箱实现，直接抛 `NotSupportedException`。

**资源**：无外部资源；userdata 元表在初始化时一次性创建并存入 registry。

## 测试方案

| 输入 | 操作 | 预期 |
| :-- | :-- | :-- |
| `42`、`-3.5`、`true`、`"abc"`、`null` | push 后读回 | 值相等，类型正确 |
| `long.MinValue`、`long.MaxValue`、`ulong.MaxValue` | 往返 | 值不变，无分配 |
| `float.NaN`、`double.PositiveInfinity` | 往返 | 按 IEEE-754 保真 |
| 单字段 struct 与三层嵌套 struct | 往返 100 万次 | 值相等，分配为 0 |
| 含 `string` 字段的 struct | 往返 | 抛 `NotSupportedException`（不支持内联） |
| 同一个对象 push 两次 | Lua 侧 `rawequal` | `true`，id 相同 |
| 栈上是 string，读取 `int` | `Read<int>` | 抛 `LuaConversionException`，消息含 `String` 与期望 `Int32` |
| 栈上是 `3.7`，读取 `int` | `Read<int>` | 抛 `LuaConversionException`（小数部分不为 0） |
| 栈上是 `nil`，读取 `int` | `Read<int>` | 抛 `LuaConversionException` |
| 栈上是数字或其它类型，读取 `bool` | `Read<bool>` | 按 Lua 真值语义：`nil`/`false` 为假，其余为真 |
| 栈上是 `4000000000`，读取 `int` | `Read<int>` | 抛 `LuaConversionException`（不再抛 `OverflowException`） |
| 栈上是 `1e300`，读取 `float` | `Read<float>` | 抛 `LuaConversionException`（不再得到 `Infinity`） |
| 栈上是数字，读取 `string` | `Read<string>` | 返回数字的字符串形式（Lua `tostring` 语义）；`nil` 读作 `null` |
| 未注册的值类型（无自定义 marshaler） | `Push` | 抛 `NotSupportedException` |
| 引用释放后再次使用 id | 读取句柄 | 抛 `ObjectDisposedException` |
| 表 `{hp=100}` | `Get<int>("hp")` / `Get<int>("mp")` | 返回 `100` / 返回 `0` |
| 表 `{hp="x"}` | `Get<int>("hp")` | 抛 `LuaConversionException`，消息含键名 |
| 嵌套表 `{a={b={c=3}}}` | `GetInPath<int>("a.b.c")` | 返回 `3`，引用计数不变 |
| 含中文的字符串与表键 | C# 写入后 Lua 侧 `utf8.len` / `t["键"]` | 按 UTF-8 计长与命中 |
| `__index` / `__newindex` 元方法报错、键为 `nil` | `Get` / `Set` | 抛 `LuaException`，环境仍可用 |
| 栈上是字符串 | `Get<LuaTable>` | 抛 `LuaConversionException`（不产生假句柄） |
| 句柄不释放后触发 GC | 调 `Tick()` | 弱引用失效，引用计数回到基线 |
| 已释放句柄 | 调 `Get` | 抛 `ObjectDisposedException` |

异常覆盖四类：空与边界（`nil`、空字符串、`long` 边界）、非法输入（错类型、非整数 number、越界索引）、依赖失败（栈空间不足）、重复与并发（重复释放、跨环境 id 混用）。
