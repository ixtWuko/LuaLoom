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

- 自动把含引用字段的 struct 做零拷贝传递（走装箱路径）。
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
    public static void PushInt(IntPtr L, int value);
    public static void PushLong(IntPtr L, long value);
    public static void PushDouble(IntPtr L, double value);
    public static void PushBoolean(IntPtr L, bool value);
    public static void PushString(IntPtr L, string? value);
    public static int ReadInt(IntPtr L, int index);
    public static long ReadLong(IntPtr L, int index);
    public static double ReadDouble(IntPtr L, int index);
    public static string? ReadString(IntPtr L, int index);
    public static bool ReadBoolean(IntPtr L, int index);
    public static void PushNil(IntPtr L);
    public static void PushObject(IntPtr L, object? value);
    public static object? ReadObject(IntPtr L, int index);
    public static void PushStruct<T>(IntPtr L, T value) where T : struct;
    public static T ReadStruct<T>(IntPtr L, int index) where T : struct;
    public static void Throw(IntPtr L, string message);       // 抛出后由 thunk 转成 Lua 错误
}
```

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

**界面**：无界面。对外暴露句柄类型 `LuaReference`/`LuaTable`/`LuaFunction`、`ITypeMarshaler` 与生成代码专用的 `LuaBinding`，其余类型 `internal`。

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
| `_marshal` | `LuaMarshal` | 所属环境的编解码器，释放时使用 |
| `_id` | `int` | 注册表引用 id |
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
| `byte[]` | string | 零拷贝读取需长度前缀，P0 走复制 |
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
5. 句柄释放：`Dispose` 立即 `luaL_unref`（C API 允许在回调内调用，不需要延迟）；只有终结器无法在 GC 线程调 Lua，才把 id 入队，由 `Tick()` 在宿主线程释放。
6. 自定义类型：`RegisterMarshaler(ITypeMarshaler)` 追加到列表尾部，查找时从后往前，后注册者优先。

**复杂度**：标量 push/read O(1)；struct 按整块拷贝 O(1)（大小固定）；对象身份查找 O(1) 均摊；弱值表的大小随活跃对象数增长。

**接口契约与错误传播**

- 所有原语显式传 `IntPtr L` 与栈索引，不使用隐式「当前栈顶」状态，避免调用顺序耦合。
- 越界索引按 `lua_type` 结果为 `LuaType.None` 处理，抛 `ArgumentOutOfRangeException`，不算类型错误。
- `LuaTable.Get` 缺失键返回 `default(T)`；类型不匹配抛 `LuaConversionException`，消息含键名与目标类型。
- struct 内联读写使用 `unsafe`，工程开启 `AllowUnsafeBlocks`；`unsafe` 代码只允许出现在本模块。
- 分配失败（`lua_checkstack` 返回 0）转 `LuaException`，不返回 false。

**资源**：无外部资源；userdata 元表在初始化时一次性创建并存入 registry。

## 测试方案

| 输入 | 操作 | 预期 |
| :-- | :-- | :-- |
| `42`、`-3.5`、`true`、`"abc"`、`null` | push 后读回 | 值相等，类型正确 |
| `long.MinValue`、`long.MaxValue`、`ulong.MaxValue` | 往返 | 值不变，无分配 |
| `float.NaN`、`double.PositiveInfinity` | 往返 | 按 IEEE-754 保真 |
| 单字段 struct 与三层嵌套 struct | 往返 100 万次 | 值相等，分配为 0 |
| 含 `string` 字段的 struct | 往返 | 走装箱路径，值正确，允许分配 |
| 同一个对象 push 两次 | Lua 侧 `rawequal` | `true`，id 相同 |
| 栈上是 string，读取 `int` | `Read<int>` | 抛 `LuaConversionException`，消息含 `String` 与期望 `Int32` |
| 栈上是 `3.7`，读取 `int` | `Read<int>` | 抛 `LuaConversionException`（小数部分不为 0） |
| 栈上是 `nil`，读取 `int` | `Read<int>` | 抛 `LuaConversionException` |
| 未注册的自定义类型 | `Push` | 抛 `NotSupportedException` |
| 引用释放后再次使用 id | 读取句柄 | 抛 `ObjectDisposedException` |
| 表 `{hp=100}` | `Get<int>("hp")` / `Get<int>("mp")` | 返回 `100` / 返回 `0` |
| 表 `{hp="x"}` | `Get<int>("hp")` | 抛 `LuaConversionException`，消息含键名 |
| 嵌套表 `{a={b={c=3}}}` | `GetInPath<int>("a.b.c")` | 返回 `3`，引用计数不变 |
| 句柄不释放后触发 GC | 调 `Tick()` | 弱引用失效，引用计数回到基线 |
| 已释放句柄 | 调 `Get` | 抛 `ObjectDisposedException` |

异常覆盖四类：空与边界（`nil`、空字符串、`long` 边界）、非法输入（错类型、非整数 number、越界索引）、依赖失败（栈空间不足）、重复与并发（重复释放、跨环境 id 混用）。
