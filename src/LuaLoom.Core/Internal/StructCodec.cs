using System.Runtime.CompilerServices;
using LuaLoom.Native;

namespace LuaLoom.Internal;

/// <summary>纯值类型 struct 的内联读写，不产生托管分配。</summary>
internal static class StructCodec
{
    public static void Push<T>(IntPtr state, T value)
        where T : struct
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            throw new NotSupportedException(
                $"Struct '{typeof(T).FullName}' contains references and cannot be passed inline.");
        }

        var size = (nuint)Unsafe.SizeOf<T>();
        var pointer = LuaApi.Profile.NewUserdata(state, size);
        unsafe
        {
            Unsafe.Write(pointer.ToPointer(), value);
        }
    }

    public static T Read<T>(IntPtr state, int index)
        where T : struct
    {
        if (LuaApi.lua_type(state, index) != LuaApi.LuaTUserData)
        {
            throw Conversion(state, index, typeof(T));
        }

        if (LuaApi.lua_getmetatable(state, index) != 0)
        {
            LuaApi.lua_settop(state, LuaApi.lua_gettop(state) - 1);
            throw Conversion(state, index, typeof(T));
        }

        if (LuaApi.lua_rawlen(state, index) != (nuint)Unsafe.SizeOf<T>())
        {
            throw Conversion(state, index, typeof(T));
        }

        var pointer = LuaApi.lua_touserdata(state, index);
        unsafe
        {
            return Unsafe.Read<T>(pointer.ToPointer());
        }
    }

    private static LuaConversionException Conversion(IntPtr state, int index, Type expected)
    {
        var actual = LuaApi.lua_typename(state, LuaApi.lua_type(state, index));
        var actualName = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(actual) ?? "unknown";
        return new LuaConversionException(expected.Name, actualName, index);
    }
}
