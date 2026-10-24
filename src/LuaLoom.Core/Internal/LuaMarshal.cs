using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LuaLoom.Native;
using NativeMarshal = System.Runtime.InteropServices.Marshal;

namespace LuaLoom.Internal;

/// <summary>
/// 唯一允许操作 Lua 栈的业务层：类型编解码、引用表、对象身份表。
/// </summary>
internal sealed class LuaMarshal : IDisposable
{
    private const string ObjectMetaName = "LuaLoom.Object";

    private static readonly LuaCSFunction ObjectGcCallback = OnObjectGc;
    private static readonly IntPtr ObjectGcPointer =
        NativeMarshal.GetFunctionPointerForDelegate(ObjectGcCallback);

    /// <summary>M004 安装的桥接读取钩子：处理 delegate 与 interface 类型。</summary>
    internal static Func<IntPtr, Type, int, object?>? BridgeReader;

    private readonly IntPtr _state;
    private readonly LuaReferenceTable _references;
    private readonly ConcurrentQueue<int> _deferredReleases = new();
    private readonly List<ITypeMarshaler> _customMarshalers = new();
    private readonly Dictionary<object, int> _objectIds = new(ReferenceEqualityComparer.Instance);
    private readonly List<object?> _objects = new();
    private readonly Stack<int> _freeObjectIds = new();
    private readonly object _objectGate = new();
    private readonly int _objectCacheRef;
    private int _disposed;

    public LuaMarshal(IntPtr state)
    {
        _state = state;
        _references = new LuaReferenceTable(state);

        using (LuaStackGuard.Enter(state))
        {
            CreateObjectMetatable();
            _objectCacheRef = CreateWeakValueCache();
        }

        LuaMarshalRegistry.Register(state, this);
    }

    public IntPtr State => _state;

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>所属环境；测试或独立使用时可为空。</summary>
    internal LuaEnv? Environment { get; set; }

    public int DeferredReleaseCount => _deferredReleases.Count;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        LuaMarshalRegistry.Unregister(_state);
        lock (_objectGate)
        {
            _objectIds.Clear();
            _objects.Clear();
        }
    }

    public int AddReference(int index) => _references.Add(index);

    public void ReleaseReference(int id)
    {
        if (IsDisposed)
        {
            return;
        }

        _references.Release(id);
    }

    public void EnqueueRelease(int id)
    {
        if (IsDisposed)
        {
            return;
        }

        _deferredReleases.Enqueue(id);
    }

    internal void EnsureUsable()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(LuaEnv));
        }

        Environment?.EnsureThread();
    }

    public int DrainDeferredReleases()
    {
        var count = 0;
        while (_deferredReleases.TryDequeue(out var id))
        {
            _references.Release(id);
            count++;
        }

        return count;
    }

    public void PushReference(int id)
    {
        LuaApi.lua_rawgeti(_state, LuaApi.LuaRegistryIndex, id);
    }

    public LuaTable CreateTable(int arraySize = 0, int recordSize = 0)
    {
        LuaApi.lua_createtable(_state, arraySize, recordSize);
        var id = _references.Add(LuaApi.lua_gettop(_state));
        return new LuaTable(this, id);
    }

    public LuaFunction CreateFunction(int index)
    {
        LuaApi.lua_pushvalue(_state, index);
        var id = _references.Add(LuaApi.lua_gettop(_state));
        return new LuaFunction(this, id);
    }

    public LuaTable CreateTableFrom(int index)
    {
        LuaApi.lua_pushvalue(_state, index);
        var id = _references.Add(LuaApi.lua_gettop(_state));
        return new LuaTable(this, id);
    }

    public void RegisterMarshaler(ITypeMarshaler marshaler) => _customMarshalers.Add(marshaler);

    public void Push<T>(T value)
    {
        var type = typeof(T);

        if (type == typeof(bool))
        {
            LuaApi.lua_pushboolean(_state, Unsafe.As<T, bool>(ref value) ? 1 : 0);
        }
        else if (type == typeof(int))
        {
            LuaApi.lua_pushinteger(_state, Unsafe.As<T, int>(ref value));
        }
        else if (type == typeof(long))
        {
            LuaApi.lua_pushinteger(_state, Unsafe.As<T, long>(ref value));
        }
        else if (type == typeof(ulong))
        {
            LuaApi.lua_pushinteger(_state, unchecked((long)Unsafe.As<T, ulong>(ref value)));
        }
        else if (type == typeof(uint))
        {
            LuaApi.lua_pushinteger(_state, Unsafe.As<T, uint>(ref value));
        }
        else if (type == typeof(short))
        {
            LuaApi.lua_pushinteger(_state, Unsafe.As<T, short>(ref value));
        }
        else if (type == typeof(ushort))
        {
            LuaApi.lua_pushinteger(_state, Unsafe.As<T, ushort>(ref value));
        }
        else if (type == typeof(byte))
        {
            LuaApi.lua_pushinteger(_state, Unsafe.As<T, byte>(ref value));
        }
        else if (type == typeof(sbyte))
        {
            LuaApi.lua_pushinteger(_state, Unsafe.As<T, sbyte>(ref value));
        }
        else if (type == typeof(float))
        {
            LuaApi.lua_pushnumber(_state, Unsafe.As<T, float>(ref value));
        }
        else if (type == typeof(double))
        {
            LuaApi.lua_pushnumber(_state, Unsafe.As<T, double>(ref value));
        }
        else if (type == typeof(string) || type == typeof(char))
        {
            var text = type == typeof(char)
                ? Unsafe.As<T, char>(ref value).ToString()
                : Unsafe.As<T, string?>(ref value);
            LuaApi.lua_pushstring(_state, text);
        }
        else if (type.IsEnum)
        {
            LuaApi.lua_pushinteger(_state, EnumToInt64(value));
        }
        else if (value is null)
        {
            LuaApi.lua_pushnil(_state);
        }
        else if (value is LuaReference reference)
        {
            EnsureSameEnvironment(reference);
            PushReference(reference.Id);
        }
        else if (type.IsValueType)
        {
            throw new NotSupportedException(
                $"Value type '{type.FullName}' must be passed through LuaBinding.PushStruct or a custom marshaler.");
        }
        else
        {
            PushObject(value);
        }
    }

    public T Read<T>(int index)
    {
        var type = typeof(T);

        if (type == typeof(object))
        {
            return (T)ReadObject(index)!;
        }

        if (type == typeof(bool))
        {
            var value = LuaApi.lua_toboolean(_state, index) != 0;
            return Unsafe.As<bool, T>(ref value);
        }

        if (type == typeof(int))
        {
            var value = checked((int)ReadInteger(index, type));
            return Unsafe.As<int, T>(ref value);
        }

        if (type == typeof(long))
        {
            var value = ReadInteger(index, type);
            return Unsafe.As<long, T>(ref value);
        }

        if (type == typeof(ulong))
        {
            var value = unchecked((ulong)ReadInteger(index, type));
            return Unsafe.As<ulong, T>(ref value);
        }

        if (type == typeof(uint))
        {
            var value = checked((uint)ReadInteger(index, type));
            return Unsafe.As<uint, T>(ref value);
        }

        if (type == typeof(short))
        {
            var value = checked((short)ReadInteger(index, type));
            return Unsafe.As<short, T>(ref value);
        }

        if (type == typeof(ushort))
        {
            var value = checked((ushort)ReadInteger(index, type));
            return Unsafe.As<ushort, T>(ref value);
        }

        if (type == typeof(byte))
        {
            var value = checked((byte)ReadInteger(index, type));
            return Unsafe.As<byte, T>(ref value);
        }

        if (type == typeof(sbyte))
        {
            var value = checked((sbyte)ReadInteger(index, type));
            return Unsafe.As<sbyte, T>(ref value);
        }

        if (type == typeof(float))
        {
            var value = (float)ReadNumber(index, type);
            return Unsafe.As<float, T>(ref value);
        }

        if (type == typeof(double))
        {
            var value = ReadNumber(index, type);
            return Unsafe.As<double, T>(ref value);
        }

        if (type == typeof(string))
        {
            var value = ReadString(index);
            return Unsafe.As<string, T>(ref value);
        }

        if (type.IsEnum)
        {
            var raw = ReadInteger(index, type);
            return Int64ToEnum<T>(raw);
        }

        if (typeof(LuaReference).IsAssignableFrom(type))
        {
            var reference = CreateReference(type, index);
            return (T)(object)reference;
        }

        if (typeof(Delegate).IsAssignableFrom(type) || type.IsInterface)
        {
            var bridged = BridgeReader?.Invoke(_state, type, index);
            if (bridged is not null)
            {
                return (T)bridged;
            }

            throw Conversion(type.Name, index);
        }

        throw new NotSupportedException(
            $"Type '{type.FullName}' cannot be read directly; use LuaTable.Get<T> with a supported type or a custom marshaler.");
    }

    public object? ReadObject(int index)
    {
        return LuaApi.lua_type(_state, index) switch
        {
            LuaApi.LuaTNil => null,
            LuaApi.LuaTBoolean => LuaApi.lua_toboolean(_state, index) != 0,
            LuaApi.LuaTNumber => ReadNumberObject(index),
            LuaApi.LuaTString => ReadString(index),
            LuaApi.LuaTTable => CreateReference(typeof(LuaTable), index),
            LuaApi.LuaTFunction => CreateReference(typeof(LuaFunction), index),
            LuaApi.LuaTUserData => ReadObjectUserdata(index),
            _ => throw Conversion("object", index),
        };
    }

    public object? Read(Type type, int index)
    {
        if (type == typeof(object))
        {
            return ReadObject(index);
        }

        if (type == typeof(bool))
        {
            return Read<bool>(index);
        }

        if (type == typeof(int))
        {
            return Read<int>(index);
        }

        if (type == typeof(long))
        {
            return Read<long>(index);
        }

        if (type == typeof(ulong))
        {
            return Read<ulong>(index);
        }

        if (type == typeof(uint))
        {
            return Read<uint>(index);
        }

        if (type == typeof(short))
        {
            return Read<short>(index);
        }

        if (type == typeof(ushort))
        {
            return Read<ushort>(index);
        }

        if (type == typeof(byte))
        {
            return Read<byte>(index);
        }

        if (type == typeof(sbyte))
        {
            return Read<sbyte>(index);
        }

        if (type == typeof(float))
        {
            return Read<float>(index);
        }

        if (type == typeof(double))
        {
            return Read<double>(index);
        }

        if (type == typeof(string))
        {
            return ReadString(index);
        }

        if (type.IsEnum)
        {
            return Enum.ToObject(type, ReadInteger(index, type));
        }

        if (typeof(LuaReference).IsAssignableFrom(type))
        {
            return CreateReference(type, index);
        }

        throw new NotSupportedException(
            $"Type '{type.FullName}' cannot be read directly; use a custom marshaler.");
    }

    public void PushObject(object? value)
    {
        switch (value)
        {
            case null:
                LuaApi.lua_pushnil(_state);
                return;
            case bool boolean:
                LuaApi.lua_pushboolean(_state, boolean ? 1 : 0);
                return;
            case int integer:
                LuaApi.lua_pushinteger(_state, integer);
                return;
            case long longValue:
                LuaApi.lua_pushinteger(_state, longValue);
                return;
            case ulong ulongValue:
                LuaApi.lua_pushinteger(_state, unchecked((long)ulongValue));
                return;
            case uint uintValue:
                LuaApi.lua_pushinteger(_state, uintValue);
                return;
            case short shortValue:
                LuaApi.lua_pushinteger(_state, shortValue);
                return;
            case ushort ushortValue:
                LuaApi.lua_pushinteger(_state, ushortValue);
                return;
            case byte byteValue:
                LuaApi.lua_pushinteger(_state, byteValue);
                return;
            case sbyte sbyteValue:
                LuaApi.lua_pushinteger(_state, sbyteValue);
                return;
            case float floatValue:
                LuaApi.lua_pushnumber(_state, floatValue);
                return;
            case double doubleValue:
                LuaApi.lua_pushnumber(_state, doubleValue);
                return;
            case char charValue:
                LuaApi.lua_pushstring(_state, charValue.ToString());
                return;
            case string text:
                LuaApi.lua_pushstring(_state, text);
                return;
            case Enum enumValue:
                LuaApi.lua_pushinteger(_state, Convert.ToInt64(enumValue, System.Globalization.CultureInfo.InvariantCulture));
                return;
            case LuaReference reference:
                EnsureSameEnvironment(reference);
                PushReference(reference.Id);
                return;
            case byte[] bytes:
                LuaApi.lua_pushlstring(_state, bytes, (nuint)bytes.Length);
                return;
        }

        var custom = FindCustomMarshaler(value.GetType());
        if (custom is not null)
        {
            custom.Push(_state, value);
            return;
        }

        if (value.GetType().IsValueType)
        {
            throw new NotSupportedException(
                $"Value type '{value.GetType().FullName}' must be passed through LuaBinding.PushStruct or a custom marshaler.");
        }

        PushObjectReference(value);
    }

    public void ThrowConversion(string expected, int index, string? key = null) => throw Conversion(expected, index, key);

    private void EnsureSameEnvironment(LuaReference reference)
    {
        if (!ReferenceEquals(reference.Marshal, this))
        {
            throw new InvalidOperationException(
                "Lua handle belongs to a different LuaEnv and cannot be used here.");
        }
    }

    private void CreateObjectMetatable()
    {
        LuaApi.luaL_newmetatable(_state, ObjectMetaName);
        LuaApi.lua_pushcclosure(_state, ObjectGcPointer, 0);
        LuaApi.lua_setfield(_state, -2, "__gc");
        LuaApi.lua_settop(_state, LuaApi.lua_gettop(_state) - 1);
    }

    private int CreateWeakValueCache()
    {
        LuaApi.lua_createtable(_state, 0, 0);
        LuaApi.lua_createtable(_state, 0, 1);
        LuaApi.lua_pushstring(_state, "v");
        LuaApi.lua_setfield(_state, -2, "__mode");
        LuaApi.lua_setmetatable(_state, -2);
        return _references.Add(LuaApi.lua_gettop(_state));
    }

    private void PushObjectReference(object value)
    {
        var (id, isNew) = GetOrCreateObjectId(value);

        PushObjectCache();
        var cacheIndex = LuaApi.lua_gettop(_state);
        LuaApi.lua_pushinteger(_state, id);
        LuaApi.lua_gettable(_state, cacheIndex);

        if (LuaApi.lua_type(_state, -1) == LuaApi.LuaTNil)
        {
            LuaApi.lua_settop(_state, cacheIndex);
            CreateObjectUserdata(id);
            LuaApi.lua_pushinteger(_state, id);
            LuaApi.lua_pushvalue(_state, -2);
            LuaApi.lua_settable(_state, cacheIndex);
        }

        RemoveAt(cacheIndex);
    }

    private void PushObjectCache()
    {
        LuaApi.lua_rawgeti(_state, LuaApi.LuaRegistryIndex, _objectCacheRef);
    }

    private void CreateObjectUserdata(int id)
    {
        var pointer = LuaApi.lua_newuserdatauv(_state, 4, 0);
        NativeMarshal.WriteInt32(pointer, id);
        LuaApi.luaL_setmetatable(_state, ObjectMetaName);
    }

    private (int Id, bool IsNew) GetOrCreateObjectId(object value)
    {
        lock (_objectGate)
        {
            if (_objectIds.TryGetValue(value, out var existing))
            {
                return (existing, false);
            }

            var id = _freeObjectIds.Count > 0 ? _freeObjectIds.Pop() : _objects.Count;
            if (id == _objects.Count)
            {
                _objects.Add(value);
            }
            else
            {
                _objects[id] = value;
            }

            _objectIds[value] = id;
            return (id, true);
        }
    }

    private object? ReadObjectUserdata(int index)
    {
        var value = GetObjectFromUserdata(index);
        if (value is null)
        {
            throw Conversion("object", index);
        }

        return value;
    }

    internal object? GetObjectFromUserdata(int index)
    {
        if (LuaApi.lua_getmetatable(_state, index) == 0)
        {
            return null;
        }

        LuaApi.lua_settop(_state, LuaApi.lua_gettop(_state) - 1);

        var pointer = LuaApi.lua_touserdata(_state, index);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        var id = NativeMarshal.ReadInt32(pointer);
        lock (_objectGate)
        {
            return id >= 0 && id < _objects.Count ? _objects[id] : null;
        }
    }

    internal void SetObjectHandlers(LuaCSFunction indexHandler, LuaCSFunction newIndexHandler)
    {
        LuaApi.luaL_newmetatable(_state, ObjectMetaName);
        LuaApi.lua_pushcclosure(_state, NativeMarshal.GetFunctionPointerForDelegate(indexHandler), 0);
        LuaApi.lua_setfield(_state, -2, "__index");
        LuaApi.lua_pushcclosure(_state, NativeMarshal.GetFunctionPointerForDelegate(newIndexHandler), 0);
        LuaApi.lua_setfield(_state, -2, "__newindex");
        LuaApi.lua_settop(_state, LuaApi.lua_gettop(_state) - 1);
    }

    internal void SetObjectMetamethod(string name, LuaCSFunction handler, string? upvalue = null)
    {
        LuaApi.luaL_newmetatable(_state, ObjectMetaName);
        if (upvalue is null)
        {
            LuaApi.lua_pushcclosure(_state, NativeMarshal.GetFunctionPointerForDelegate(handler), 0);
        }
        else
        {
            LuaApi.lua_pushstring(_state, upvalue);
            LuaApi.lua_pushcclosure(_state, NativeMarshal.GetFunctionPointerForDelegate(handler), 1);
        }

        LuaApi.lua_setfield(_state, -2, name);
        LuaApi.lua_settop(_state, LuaApi.lua_gettop(_state) - 1);
    }

    internal List<KeyValuePair<object?, object?>> ReadTableEntries(int index)
    {
        var entries = new List<KeyValuePair<object?, object?>>();
        using var guard = LuaStackGuard.Enter(_state);
        LuaApi.lua_pushvalue(_state, index);
        LuaApi.lua_pushnil(_state);

        while (LuaApi.lua_next(_state, -2) != 0)
        {
            var key = ReadObject(-2);
            var value = ReadObject(-1);
            entries.Add(new KeyValuePair<object?, object?>(key, value));
            LuaApi.lua_settop(_state, LuaApi.lua_gettop(_state) - 1);
        }

        return entries;
    }

    private void OnObjectCollected(int id)
    {
        lock (_objectGate)
        {
            if (id < 0 || id >= _objects.Count)
            {
                return;
            }

            var value = _objects[id];
            if (value is not null)
            {
                _objectIds.Remove(value);
                _objects[id] = null;
                _freeObjectIds.Push(id);
            }
        }
    }

    private static int OnObjectGc(IntPtr state)
    {
        var pointer = LuaApi.lua_touserdata(state, 1);
        if (pointer != IntPtr.Zero && LuaMarshalRegistry.TryGet(state, out var marshal))
        {
            marshal.OnObjectCollected(NativeMarshal.ReadInt32(pointer));
        }

        return 0;
    }

    private LuaReference CreateReference(Type type, int index)
    {
        LuaApi.lua_pushvalue(_state, index);
        var id = _references.Add(LuaApi.lua_gettop(_state));
        return type == typeof(LuaTable)
            ? new LuaTable(this, id)
            : (LuaReference)new LuaFunction(this, id);
    }

    private long ReadInteger(int index, Type expected)
    {
        if (LuaApi.lua_type(_state, index) != LuaApi.LuaTNumber)
        {
            throw Conversion(expected.Name, index);
        }

        var value = LuaApi.lua_tointegerx(_state, index, out var isNumber);
        if (isNumber == 0)
        {
            throw Conversion(expected.Name, index);
        }

        return value;
    }

    private double ReadNumber(int index, Type expected)
    {
        if (LuaApi.lua_type(_state, index) != LuaApi.LuaTNumber)
        {
            throw Conversion(expected.Name, index);
        }

        return LuaApi.lua_tonumberx(_state, index, out _);
    }

    private object ReadNumberObject(int index)
    {
        var integer = LuaApi.lua_tointegerx(_state, index, out var isInteger);
        return isInteger != 0 ? (object)integer : LuaApi.lua_tonumberx(_state, index, out _);
    }

    private string ReadString(int index)
    {
        if (LuaApi.lua_type(_state, index) != LuaApi.LuaTString)
        {
            throw Conversion("string", index);
        }

        var pointer = LuaApi.lua_tolstring(_state, index, out var length);
        return NativeMarshal.PtrToStringUTF8(pointer, (int)length) ?? string.Empty;
    }

    private LuaConversionException Conversion(string expected, int index, string? key = null)
    {
        var actual = LuaApi.lua_typename(_state, LuaApi.lua_type(_state, index));
        var actualName = NativeMarshal.PtrToStringUTF8(actual) ?? "unknown";
        return new LuaConversionException(expected, actualName, index, key);
    }

    private ITypeMarshaler? FindCustomMarshaler(Type type)
    {
        for (var i = _customMarshalers.Count - 1; i >= 0; i--)
        {
            if (_customMarshalers[i].CanMarshal(type))
            {
                return _customMarshalers[i];
            }
        }

        return null;
    }

    private static long EnumToInt64<T>(T value)
    {
        return Unsafe.SizeOf<T>() switch
        {
            1 => Unsafe.As<T, byte>(ref value),
            2 => Unsafe.As<T, ushort>(ref value),
            4 => Unsafe.As<T, uint>(ref value),
            _ => Unsafe.As<T, long>(ref value),
        };
    }

    private static T Int64ToEnum<T>(long value)
    {
        return Unsafe.SizeOf<T>() switch
        {
            1 => Unsafe.As<byte, T>(ref Unsafe.As<long, byte>(ref value)),
            2 => Unsafe.As<ushort, T>(ref Unsafe.As<long, ushort>(ref value)),
            4 => Unsafe.As<uint, T>(ref Unsafe.As<long, uint>(ref value)),
            _ => Unsafe.As<long, T>(ref value),
        };
    }

    private void RemoveAt(int index)
    {
        var top = LuaApi.lua_gettop(_state);
        if (index == top)
        {
            LuaApi.lua_settop(_state, top - 1);
            return;
        }

        LuaApi.lua_rotate(_state, index, -1);
        LuaApi.lua_settop(_state, top - 1);
    }
}
