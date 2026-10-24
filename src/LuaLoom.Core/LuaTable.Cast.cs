using System.Collections;
using System.Reflection;
using LuaLoom.Internal;
using LuaLoom.Native;

namespace LuaLoom;

public sealed partial class LuaTable
{
    /// <summary>把表转换成 C# 对象。值拷贝，不回写。</summary>
    public T Cast<T>()
    {
        EnsureAlive();
        var type = typeof(T);

        if (Marshal.Environment is { } environment
            && environment.TryGetCast(type, out var generatedCast))
        {
            return (T)generatedCast(this);
        }

        if (type.IsInterface)
        {
            return (T)CastInterface(type);
        }

        if (IsDictionary(type))
        {
            return (T)CastDictionary(type);
        }

        if (IsList(type))
        {
            return (T)CastList(type);
        }

        if (type.IsArray)
        {
            return (T)CastArray(type);
        }

        if (type.IsClass || (type.IsValueType && !type.IsPrimitive && !type.IsEnum))
        {
            return (T)CastObject(type);
        }

        throw new NotSupportedException($"Cast<{type.Name}> is not supported.");
    }

    private object CastInterface(Type type)
    {
        var environment = Marshal.Environment
            ?? throw new InvalidOperationException("Table is not attached to a LuaEnv.");

        if (environment.TryGetInterfaceFactory(type, out var factory))
        {
            return factory(this);
        }

        var proxy = DispatchProxy.Create(type, typeof(LuaInterfaceProxy));
        ((LuaInterfaceProxy)proxy).Initialize(this);
        return proxy;
    }

    private object CastDictionary(Type type)
    {
        var arguments = type.GetGenericArguments();
        var dictionary = (IDictionary)Activator.CreateInstance(type)!;

        foreach (var entry in ReadEntries())
        {
            var key = ConvertMember(entry.Key, arguments[0]);
            var value = ConvertMember(entry.Value, arguments[1]);
            dictionary[key!] = value;
        }

        return dictionary;
    }

    private object CastList(Type type)
    {
        var elementType = type.GetGenericArguments()[0];
        var length = Length;
        var state = Marshal.State;

        using var guard = LuaStackGuard.Enter(state);
        Marshal.PushReference(Id);
        var table = LuaApi.lua_gettop(state);

        if (elementType == typeof(int))
        {
            return new List<int>(ReadArray<int>(state, table, length, static (s, i) => LuaBinding.ReadInt(s, i)));
        }

        if (elementType == typeof(long))
        {
            return new List<long>(ReadArray<long>(state, table, length, static (s, i) => LuaBinding.ReadLong(s, i)));
        }

        if (elementType == typeof(double))
        {
            return new List<double>(ReadArray<double>(state, table, length, static (s, i) => LuaBinding.ReadDouble(s, i)));
        }

        if (elementType == typeof(float))
        {
            return new List<float>(ReadArray<float>(state, table, length, static (s, i) => (float)LuaBinding.ReadDouble(s, i)));
        }

        if (elementType == typeof(bool))
        {
            return new List<bool>(ReadArray<bool>(state, table, length, static (s, i) => LuaBinding.ReadBoolean(s, i)));
        }

        if (elementType == typeof(string))
        {
            return new List<string?>(ReadArray<string?>(state, table, length, static (s, i) => LuaBinding.ReadString(s, i)));
        }

        var list = (IList)Activator.CreateInstance(type)!;
        for (var i = 0; i < length; i++)
        {
            LuaApi.lua_rawgeti(state, table, i + 1);
            var value = LuaApi.lua_type(state, -1) == LuaApi.LuaTNil ? null : Marshal.ReadObject(-1);
            LuaApi.lua_settop(state, table);
            list.Add(ConvertMember(value, elementType));
        }

        return list;
    }

    private object CastArray(Type type)
    {
        if (type.GetArrayRank() > 1)
        {
            return CastMultiArray(type);
        }

        var elementType = type.GetElementType()!;
        var length = Length;
        var state = Marshal.State;

        using var guard = LuaStackGuard.Enter(state);
        Marshal.PushReference(Id);
        var table = LuaApi.lua_gettop(state);

        if (elementType == typeof(int))
        {
            return ReadArray<int>(state, table, length, static (s, i) => LuaBinding.ReadInt(s, i));
        }

        if (elementType == typeof(long))
        {
            return ReadArray<long>(state, table, length, static (s, i) => LuaBinding.ReadLong(s, i));
        }

        if (elementType == typeof(double))
        {
            return ReadArray<double>(state, table, length, static (s, i) => LuaBinding.ReadDouble(s, i));
        }

        if (elementType == typeof(float))
        {
            return ReadArray<float>(state, table, length, static (s, i) => (float)LuaBinding.ReadDouble(s, i));
        }

        if (elementType == typeof(bool))
        {
            return ReadArray<bool>(state, table, length, static (s, i) => LuaBinding.ReadBoolean(s, i));
        }

        if (elementType == typeof(string))
        {
            return ReadArray<string?>(state, table, length, static (s, i) => LuaBinding.ReadString(s, i));
        }

        var array = Array.CreateInstance(elementType, length);
        for (var i = 0; i < length; i++)
        {
            LuaApi.lua_rawgeti(state, table, i + 1);
            var value = LuaApi.lua_type(state, -1) == LuaApi.LuaTNil ? null : Marshal.ReadObject(-1);
            LuaApi.lua_settop(state, table);
            array.SetValue(ConvertMember(value, elementType), i);
        }

        return array;
    }

    private object CastMultiArray(Type type)
    {
        var state = Marshal.State;
        using var guard = LuaStackGuard.Enter(state);
        Marshal.PushReference(Id);
        var table = LuaApi.lua_gettop(state);

        var rank = type.GetArrayRank();
        var lengths = new int[rank];
        var current = table;
        for (var dimension = 0; dimension < rank; dimension++)
        {
            lengths[dimension] = checked((int)LuaApi.lua_rawlen(state, current));
            if (lengths[dimension] == 0 || dimension == rank - 1)
            {
                break;
            }

            LuaApi.lua_rawgeti(state, current, 1);
            if (LuaApi.lua_type(state, -1) != LuaApi.LuaTTable)
            {
                throw new LuaConversionException("array", "non-table", -1);
            }

            current = LuaApi.lua_gettop(state);
        }

        var array = Array.CreateInstance(type.GetElementType()!, lengths);
        FillMultiArray(array, new int[rank], 0, table, lengths);
        return array;
    }

    private void FillMultiArray(
        Array array,
        int[] indices,
        int dimension,
        int table,
        int[] lengths)
    {
        var state = Marshal.State;
        for (var i = 0; i < lengths[dimension]; i++)
        {
            LuaApi.lua_rawgeti(state, table, i + 1);
            indices[dimension] = i;
            if (dimension == lengths.Length - 1)
            {
                var value = LuaApi.lua_type(state, -1) == LuaApi.LuaTNil
                    ? null
                    : Marshal.ReadObject(-1);
                array.SetValue(ConvertMember(value, array.GetType().GetElementType()!), indices);
            }
            else
            {
                if (LuaApi.lua_type(state, -1) != LuaApi.LuaTTable)
                {
                    throw new LuaConversionException("array", "non-table", -1);
                }

                FillMultiArray(array, indices, dimension + 1, LuaApi.lua_gettop(state), lengths);
            }

            LuaApi.lua_settop(state, table);
        }
    }

    private T[] ReadArray<T>(IntPtr state, int table, int length, ArrayReader<T> read)
    {
        var values = new T[length];
        for (var i = 0; i < length; i++)
        {
            LuaApi.lua_rawgeti(state, table, i + 1);
            values[i] = read(state, -1);
            LuaApi.lua_settop(state, table);
        }

        return values;
    }

    private delegate T ArrayReader<T>(IntPtr state, int index);

    private object CastObject(Type type)
    {
        var instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Type '{type.FullName}' requires a parameterless constructor.");

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

        foreach (var property in type.GetProperties(flags))
        {
            if (!property.CanWrite || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            property.SetValue(instance, ConvertMember(Get<object>(property.Name), property.PropertyType));
        }

        foreach (var field in type.GetFields(flags))
        {
            if (field.IsInitOnly)
            {
                continue;
            }

            field.SetValue(instance, ConvertMember(Get<object>(field.Name), field.FieldType));
        }

        return instance;
    }

    private object? ConvertIndex(int index, Type target)
    {
        var value = ReadIndexObject(index);
        return ConvertMember(value, target);
    }

    private object? ReadIndexObject(int index)
    {
        var state = Marshal.State;
        using var guard = LuaStackGuard.Enter(state);
        Marshal.PushReference(Id);
        LuaApi.lua_rawgeti(state, -1, index);
        return LuaApi.lua_type(state, -1) == LuaApi.LuaTNil ? null : Marshal.ReadObject(-1);
    }

    internal List<KeyValuePair<object?, object?>> ReadEntries()
    {
        var state = Marshal.State;
        using var guard = LuaStackGuard.Enter(state);
        Marshal.PushReference(Id);
        return Marshal.ReadTableEntries(-1);
    }

    private object? ConvertMember(object? value, Type target)
    {
        if (value is LuaTable nested)
        {
            try
            {
                if (target != typeof(LuaTable)
                    && !typeof(LuaFunction).IsAssignableFrom(target)
                    && (target.IsClass || (target.IsValueType && !target.IsPrimitive && !target.IsEnum)))
                {
                    return typeof(LuaTable)
                        .GetMethod(nameof(Cast))!
                        .MakeGenericMethod(target)
                        .Invoke(nested, null);
                }
            }
            finally
            {
                nested.Dispose();
            }
        }

        return Interop.ConvertValue(value, target);
    }

    private static bool IsDictionary(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>);

    private static bool IsList(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
}
