using System.Linq.Expressions;
using System.Reflection;

namespace LuaLoom;

/// <summary>把 Lua 函数包成事件所需的任意 delegate 形态；只用于事件，非通用转换路径。</summary>
internal static class LuaEventDelegateFactory
{
    private static readonly MethodInfo CallMethod =
        typeof(LuaFunction).GetMethod(nameof(LuaFunction.Call), [typeof(object[])])!;

    private static readonly MethodInfo ConvertObjectMethod =
        typeof(LuaBinding).GetMethod(nameof(LuaBinding.ConvertObject))!;

    public static Delegate Create(Type delegateType, LuaFunction function)
    {
        var invoke = delegateType.GetMethod("Invoke")
            ?? throw new ArgumentException($"'{delegateType.FullName}' is not a delegate type.", nameof(delegateType));

        var parameters = invoke.GetParameters();
        if (parameters.Any(static parameter => parameter.ParameterType.IsByRef))
        {
            throw new ArgumentException(
                $"Delegate '{delegateType.FullName}' has ref/out parameters, which cannot be bound from Lua.",
                nameof(delegateType));
        }

        var expressionParameters = parameters
            .Select(static (parameter, index) => Expression.Parameter(parameter.ParameterType, "a" + index))
            .ToArray();

        var boxed = Expression.NewArrayInit(
            typeof(object),
            expressionParameters.Select(static parameter => Expression.Convert(parameter, typeof(object))));

        var call = Expression.Call(Expression.Constant(function), CallMethod, boxed);

        Expression body;
        if (invoke.ReturnType == typeof(void))
        {
            body = Expression.Block(call, Expression.Empty());
        }
        else
        {
            // Lua 回调没返回值或返回 nil 时用 default，而不是越界/空引用异常
            var results = Expression.Variable(typeof(object[]), "results");
            var first = Expression.ArrayIndex(results, Expression.Constant(0));
            body = Expression.Block(
                new[] { results },
                Expression.Assign(results, call),
                Expression.Condition(
                    Expression.AndAlso(
                        Expression.GreaterThan(Expression.ArrayLength(results), Expression.Constant(0)),
                        Expression.NotEqual(first, Expression.Constant(null, typeof(object)))),
                    Expression.Call(ConvertObjectMethod.MakeGenericMethod(invoke.ReturnType), first),
                    Expression.Default(invoke.ReturnType)));
        }

        return Expression.Lambda(delegateType, body, expressionParameters).Compile();
    }
}
