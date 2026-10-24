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
        var pointer = NativeMarshal.GetFunctionPointerForDelegate(function);
        LuaApi.lua_pushcclosure(state, pointer, 0);
    }

    /// <summary>当前栈顶索引；生成代码用它判断实参个数。</summary>
    public static int GetTop(IntPtr state) => LuaApi.lua_gettop(state);

    public static int ReadInt(IntPtr state, int index) => checked((int)ReadInteger(state, index));

    public static long ReadLong(IntPtr state, int index) => ReadInteger(state, index);

    public static double ReadDouble(IntPtr state, int index)
    {
        if (LuaApi.lua_type(state, index) != LuaApi.LuaTNumber)
        {
            throw Conversion(state, index, "double");
        }

        return LuaApi.lua_tonumberx(state, index, out _);
    }

    public static bool ReadBoolean(IntPtr state, int index)
    {
        var type = LuaApi.lua_type(state, index);
        if (type != LuaApi.LuaTBoolean && type != LuaApi.LuaTNil)
        {
            throw Conversion(state, index, "boolean");
        }

        return LuaApi.lua_toboolean(state, index) != 0;
    }

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

    public static T[] ReadArray<T>(IntPtr state, int index) =>
        LuaMarshalRegistry.Get(state).ReadArray<T>(index);

    public static void PushArray<T>(IntPtr state, T[]? values) =>
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

    private static LuaConversionException Conversion(IntPtr state, int index, string expected)
    {
        var actual = LuaApi.lua_typename(state, LuaApi.lua_type(state, index));
        var actualName = NativeMarshal.PtrToStringUTF8(actual) ?? "unknown";
        return new LuaConversionException(expected, actualName, index);
    }
}
