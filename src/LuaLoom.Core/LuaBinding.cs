using System.Runtime.InteropServices;
using LuaLoom.Internal;
using LuaLoom.Native;
using NativeMarshal = System.Runtime.InteropServices.Marshal;

namespace LuaLoom;

/// <summary>
/// 生成代码调用面：只提供强类型原语，不暴露栈索引以外的栈操作，也不负责栈守卫。
/// </summary>
public static class LuaBinding
{
    private static readonly LuaCSFunction ThunkCallback = OnThunk;
    private static readonly IntPtr ThunkPointer = NativeMarshal.GetFunctionPointerForDelegate(ThunkCallback);

    public static void PushNil(IntPtr state) => LuaApi.lua_pushnil(state);

    public static void PushInt(IntPtr state, int value) => LuaApi.lua_pushinteger(state, value);

    public static void PushLong(IntPtr state, long value) => LuaApi.lua_pushinteger(state, value);

    public static void PushDouble(IntPtr state, double value) => LuaApi.lua_pushnumber(state, value);

    public static void PushBoolean(IntPtr state, bool value) => LuaApi.lua_pushboolean(state, value ? 1 : 0);

    public static void PushString(IntPtr state, string? value) => LuaApi.lua_pushstring(state, value);

    public static void PushObject(IntPtr state, object? value) =>
        LuaMarshalRegistry.Get(state).PushObject(value);

    public static void PushStruct<T>(IntPtr state, T value)
        where T : struct => StructCodec.Push(state, value);

    public static void PushFunction(IntPtr state, LuaCSFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        var marshal = LuaMarshalRegistry.Get(state);
        LuaApi.lua_pushinteger(state, marshal.AddThunk(function));
        LuaApi.lua_pushcclosure(state, ThunkPointer, 1);
    }

    /// <summary>当前栈顶索引；生成代码用它判断实参个数。</summary>
    public static int GetTop(IntPtr state) => LuaApi.lua_gettop(state);

    public static int ReadInt(IntPtr state, int index) =>
        (int)ReadIntegerInRange(state, index, int.MinValue, int.MaxValue, "int");

    public static long ReadLong(IntPtr state, int index) => ReadInteger(state, index);

    public static byte ReadByte(IntPtr state, int index) =>
        (byte)ReadIntegerInRange(state, index, byte.MinValue, byte.MaxValue, "byte");

    public static sbyte ReadSByte(IntPtr state, int index) =>
        (sbyte)ReadIntegerInRange(state, index, sbyte.MinValue, sbyte.MaxValue, "sbyte");

    public static short ReadShort(IntPtr state, int index) =>
        (short)ReadIntegerInRange(state, index, short.MinValue, short.MaxValue, "short");

    public static ushort ReadUShort(IntPtr state, int index) =>
        (ushort)ReadIntegerInRange(state, index, ushort.MinValue, ushort.MaxValue, "ushort");

    public static uint ReadUInt(IntPtr state, int index) =>
        (uint)ReadIntegerInRange(state, index, uint.MinValue, uint.MaxValue, "uint");

    public static ulong ReadULong(IntPtr state, int index) => unchecked((ulong)ReadInteger(state, index));

    /// <summary>char 既接受单字符字符串（与 push 语义对称），也接受整数码点。</summary>
    public static char ReadChar(IntPtr state, int index)
    {
        if (LuaApi.lua_type(state, index) == LuaApi.LuaTString)
        {
            var text = ReadString(state, index);
            if (text is { Length: 1 })
            {
                return text[0];
            }

            throw new LuaConversionException("char", $"string of length {text?.Length ?? 0}", index);
        }

        return checked((char)ReadInteger(state, index));
    }

    public static double ReadDouble(IntPtr state, int index)
    {
        if (LuaApi.lua_type(state, index) != LuaApi.LuaTNumber)
        {
            throw Conversion(state, index, "double");
        }

        return LuaApi.lua_tonumberx(state, index, out _);
    }

    /// <summary>读取 float，超出 float 范围时报转换错误而不是得到 Infinity。</summary>
    public static float ReadFloat(IntPtr state, int index)
    {
        var value = ReadDouble(state, index);
        var single = (float)value;
        if (float.IsInfinity(single) && !double.IsInfinity(value))
        {
            throw Conversion(state, index, "float");
        }

        return single;
    }

    public static bool ReadBoolean(IntPtr state, int index) => LuaApi.lua_toboolean(state, index) != 0;

    public static string? ReadString(IntPtr state, int index)
    {
        var type = LuaApi.lua_type(state, index);
        if (type == LuaApi.LuaTNil)
        {
            return null;
        }

        if (type != LuaApi.LuaTString && type != LuaApi.LuaTNumber)
        {
            throw Conversion(state, index, "string");
        }

        var pointer = LuaApi.lua_tolstring(state, index, out var length);
        return NativeMarshal.PtrToStringUTF8(pointer, checked((int)length));
    }

    public static object? ReadObject(IntPtr state, int index) =>
        LuaMarshalRegistry.Get(state).ReadObject(index);

    public static T ConvertObject<T>(object? value) =>
        (T)Interop.ConvertValue(value, typeof(T))!;

    public static T ReadObjectAs<T>(IntPtr state, int index)
    {
        var bridged = LuaMarshal.BridgeReader?.Invoke(state, typeof(T), index);
        if (bridged is T bridgedValue)
        {
            return bridgedValue;
        }

        var value = LuaMarshalRegistry.Get(state).ReadObject(index);
        if (value is T typed)
        {
            return typed;
        }

        if (value is LuaTable table)
        {
            try
            {
                return table.Cast<T>();
            }
            finally
            {
                table.Dispose();
            }
        }

        throw new LuaConversionException(typeof(T).Name, value?.GetType().Name ?? "nil", index);
    }

    public static T[] ReadArray<T>(IntPtr state, int index)
    {
        if (LuaApi.lua_type(state, index) == LuaApi.LuaTNil)
        {
            return null!;
        }

        var marshal = LuaMarshalRegistry.Get(state);
        if (IsDirectArrayElement(typeof(T)))
        {
            return marshal.ReadArray<T>(index);
        }

        if (LuaApi.lua_type(state, index) == LuaApi.LuaTTable)
        {
            using var table = marshal.CreateTableFrom(index);
            return table.Cast<T[]>();
        }

        throw new LuaConversionException($"array of {typeof(T).Name}", "non-table", index);
    }

    public static LuaFunction CreateFunction(IntPtr state, int index) =>
        LuaMarshalRegistry.Get(state).CreateFunction(index);

    public static IntPtr GetFunctionPointer(IntPtr state, int index) =>
        LuaApi.lua_topointer(state, index);

    public static void PushArray<T>(IntPtr state, T[]? values) =>
        LuaMarshalRegistry.Get(state).PushArray(values);

    public static void PushArray(IntPtr state, Array? values) =>
        LuaMarshalRegistry.Get(state).PushArray(values);

    public static bool IsNil(IntPtr state, int index) =>
        LuaApi.lua_type(state, index) == LuaApi.LuaTNil;

    public static bool IsBoolean(IntPtr state, int index) =>
        LuaApi.lua_type(state, index) == LuaApi.LuaTBoolean;

    public static bool IsNumber(IntPtr state, int index) =>
        LuaApi.lua_type(state, index) == LuaApi.LuaTNumber;

    public static bool IsInteger(IntPtr state, int index)
    {
        if (LuaApi.lua_type(state, index) != LuaApi.LuaTNumber)
        {
            return false;
        }

        LuaApi.lua_tointegerx(state, index, out var isInteger);
        return isInteger != 0;
    }

    /// <summary>判断栈上的值是否为落在 [minimum, maximum] 内的整数；供生成代码做重载分派。</summary>
    public static bool IsIntegerInRange(IntPtr state, int index, long minimum, long maximum)
    {
        if (LuaApi.lua_type(state, index) != LuaApi.LuaTNumber)
        {
            return false;
        }

        var value = LuaApi.lua_tointegerx(state, index, out var isInteger);
        return isInteger != 0 && value >= minimum && value <= maximum;
    }

    public static bool IsString(IntPtr state, int index) =>
        LuaApi.lua_type(state, index) == LuaApi.LuaTString;

    public static bool IsTable(IntPtr state, int index) =>
        LuaApi.lua_type(state, index) == LuaApi.LuaTTable;

    public static bool IsFunction(IntPtr state, int index) =>
        LuaApi.lua_type(state, index) == LuaApi.LuaTFunction;

    public static bool IsUserdataOfType<T>(IntPtr state, int index) =>
        LuaApi.lua_type(state, index) == LuaApi.LuaTUserData
        && LuaMarshalRegistry.Get(state).GetObjectFromUserdata(index) is T;

    public static T ReadStruct<T>(IntPtr state, int index)
        where T : struct => StructCodec.Read<T>(state, index);

    public static void Throw(IntPtr state, string message) =>
        throw new LuaException(LuaStatus.ErrRun, message);

    private static bool IsDirectArrayElement(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(string)
        || type == typeof(object)
        || typeof(LuaReference).IsAssignableFrom(type)
        || typeof(Delegate).IsAssignableFrom(type)
        || type.IsInterface;

    /// <summary>
    /// 生成代码的方法入口统一经此跳板：托管异常转成 Lua 错误（脚本 <c>pcall</c> 可捕获），
    /// 而不是穿过原生栈帧抛到宿主调用方。委托由 <see cref="LuaMarshal.AddThunk"/> 保持存活。
    /// </summary>
    private static int OnThunk(IntPtr state)
    {
        var id = checked((int)LuaApi.lua_tointegerx(state, LuaApi.Profile.RegistryIndex - 1, out _));
        var thunk = LuaMarshalRegistry.Get(state).GetThunk(id);
        if (thunk is null)
        {
            LuaApi.lua_pushstring(state, "LuaLoom: generated thunk is not registered.");
            return LuaApi.lua_error(state);
        }

        try
        {
            return thunk(state);
        }
        catch (Exception exception)
        {
            LuaApi.lua_pushstring(state, exception.Message);
            return LuaApi.lua_error(state);
        }
    }

    private static long ReadInteger(IntPtr state, int index)
    {
        if (LuaApi.lua_type(state, index) != LuaApi.LuaTNumber)
        {
            throw Conversion(state, index, "integer");
        }

        var value = LuaApi.lua_tointegerx(state, index, out var isNumber);
        if (isNumber == 0)
        {
            throw Conversion(state, index, "integer");
        }

        return value;
    }

    private static long ReadIntegerInRange(
        IntPtr state,
        int index,
        long minimum,
        long maximum,
        string expected)
    {
        var value = ReadInteger(state, index);
        if (value < minimum || value > maximum)
        {
            throw Conversion(state, index, expected);
        }

        return value;
    }

    private static LuaConversionException Conversion(IntPtr state, int index, string expected)
    {
        var actual = LuaApi.lua_typename(state, LuaApi.lua_type(state, index));
        var actualName = NativeMarshal.PtrToStringUTF8(actual) ?? "unknown";
        return new LuaConversionException(expected, actualName, index);
    }
}
