using System.Linq.Expressions;
using System.Reflection;

namespace LuaLoom;

/// <summary>把 Lua 函数包成事件所需的任意 delegate 形态；只用于事件，非通用转换路径。</summary>
internal static class LuaEventDelegateFactory
{
    private static readonly MethodInfo CallMethod =
        typeof(LuaFunction).GetMethod(nameof(LuaFunction.Call), [typeof(object[])])!;

    public static Delegate Create(Type delegateType, LuaFunction function)
    {
        var invoke = delegateType.GetMethod("Invoke")
            ?? throw new ArgumentException($"'{delegateType.FullName}' is not a delegate type.", nameof(delegateType));

        var parameters = invoke.GetParameters()
            .Select(static (parameter, index) => Expression.Parameter(parameter.ParameterType, "a" + index))
            .ToArray();

        var boxed = Expression.NewArrayInit(
            typeof(object),
            parameters.Select(static parameter => Expression.Convert(parameter, typeof(object))));

        var call = Expression.Call(Expression.Constant(function), CallMethod, boxed);

        Expression body = invoke.ReturnType == typeof(void)
            ? Expression.Block(call, Expression.Empty())
            : Expression.Convert(Expression.ArrayIndex(call, Expression.Constant(0)), invoke.ReturnType);

        return Expression.Lambda(delegateType, body, parameters).Compile();
    }
}
