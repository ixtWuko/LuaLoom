using System.Reflection;

namespace LuaLoom;

/// <summary>接口兜底代理：把接口成员转发到 Lua 表。仅用于开发期，生产走生成实现。</summary>
public class LuaInterfaceProxy : DispatchProxy
{
    private LuaTable _table = null!;

    internal void Initialize(LuaTable table) => _table = table;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
        {
            throw new ArgumentNullException(nameof(targetMethod));
        }

        var name = targetMethod.Name;
        object?[] arguments = args ?? [];

        if (name.StartsWith("get_", StringComparison.Ordinal))
        {
            return ReadMember(name, name[4..], targetMethod.ReturnType);
        }

        if (name.StartsWith("set_", StringComparison.Ordinal))
        {
            WriteMember(name, name[4..], arguments.Length > 0 ? arguments[0] : null);
            return null;
        }

        var function = _table.Get<LuaFunction>(name)
            ?? throw new LuaConversionException(
                $"{targetMethod.DeclaringType?.Name}.{name}",
                "function",
                -1,
                name);

        using (function)
        {
            var results = function.Call(PrependSelf(arguments));
            if (targetMethod.ReturnType == typeof(void))
            {
                return null;
            }

            var result = results.Length > 0 ? results[0] : null;
            return Interop.ConvertValue(result, targetMethod.ReturnType);
        }
    }

    private object? ReadMember(string methodName, string fieldName, Type returnType)
    {
        var function = _table.Get<LuaFunction>(methodName);
        if (function is null)
        {
            return Interop.ConvertValue(_table.Get<object>(fieldName), returnType);
        }

        using (function)
        {
            var results = function.Call([_table]);
            return Interop.ConvertValue(results.Length > 0 ? results[0] : null, returnType);
        }
    }

    private void WriteMember(string methodName, string fieldName, object? value)
    {
        var function = _table.Get<LuaFunction>(methodName);
        if (function is null)
        {
            _table.Set(fieldName, value!);
            return;
        }

        using (function)
        {
            function.Call([_table, value!]);
        }
    }

    private object[] PrependSelf(object?[] arguments)
    {
        var all = new object[arguments.Length + 1];
        all[0] = _table;
        for (var i = 0; i < arguments.Length; i++)
        {
            all[i + 1] = arguments[i]!;
        }

        return all;
    }
}
