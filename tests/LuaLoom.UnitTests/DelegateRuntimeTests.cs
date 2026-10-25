using LuaLoom;
using LuaLoom.Native;
using LuaLoom.UnitTests.Sample;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class DelegateRuntimeTests
{
    private const string Factory = "CS.LuaLoom.UnitTests.Sample.DelegateFactory";

    [Fact]
    public void Make_ReturnsIntUnary_CallableFromLua()
    {
        using var env = new LuaEnv();

        var made = env.DoString($"return {Factory}.Make(3)")[0];
        Assert.IsType<IntUnary>(made);

        var results = env.DoString($$"""
            local f = {{Factory}}.Make(3)
            return f(4)
            """);

        Assert.Equal(12L, results[0]);
    }

    [Fact]
    public void MakeBinary_ReturnsIntOp_CallableFromLua()
    {
        using var env = new LuaEnv();

        var made = env.DoString($"return {Factory}.MakeBinary(2)")[0];
        Assert.IsType<IntOp>(made);

        var results = env.DoString($$"""
            local f = {{Factory}}.MakeBinary(2)
            return f(3, 4)
            """);

        Assert.Equal(14L, results[0]);
    }

    [Fact]
    public void MakeParser_ReturnsParseOp_OutResultAfterReturnValue()
    {
        using var env = new LuaEnv();

        var made = env.DoString($"return {Factory}.MakeParser()")[0];
        Assert.IsType<ParseOp>(made);

        var results = env.DoString($$"""
            local f = {{Factory}}.MakeParser()
            local ok, value = f('42')
            return ok, value
            """);

        Assert.True(results[0] is true);
        Assert.Equal(42L, results[1]);
    }

    [Fact]
    public void MakeParams_ReturnsParamsOp_AcceptsVariadicArguments()
    {
        using var env = new LuaEnv();

        var made = env.DoString($"return {Factory}.MakeParams()")[0];
        Assert.IsType<ParamsOp>(made);

        var results = env.DoString($$"""
            local f = {{Factory}}.MakeParams()
            return f(10, 1, 2, 3), f(10)
            """);

        Assert.Equal(16L, results[0]);
        Assert.Equal(10L, results[1]);
    }

    [Fact]
    public void MakeArray_ReturnsArrayOp_ConvertsLuaTableAndBack()
    {
        using var env = new LuaEnv();

        var made = env.DoString($"return {Factory}.MakeArray(2)")[0];
        Assert.IsType<ArrayOp>(made);

        var results = env.DoString($$"""
            local f = {{Factory}}.MakeArray(2)
            local r = f({1, 2, 3})
            return #r, r[1], r[2], r[3]
            """);

        Assert.Equal(3L, results[0]);
        Assert.Equal(2L, results[1]);
        Assert.Equal(4L, results[2]);
        Assert.Equal(6L, results[3]);
    }

    [Fact]
    public void MakeVoid_ReturnsVoidDelegate_WithNoResults()
    {
        using var env = new LuaEnv();

        var results = env.DoString($$"""
            local f = {{Factory}}.MakeVoid()
            local before = {{Factory}}.LastValue
            local returned = f(9)
            return before, returned, {{Factory}}.LastValue
            """);

        Assert.Equal(0L, results[0]);
        Assert.Null(results[1]);
        Assert.Equal(9L, results[2]);
    }

    [Fact]
    public void DelegateAddition_CombinesCalls_LastResultWins()
    {
        using var env = new LuaEnv();

        var results = env.DoString($$"""
            local f = {{Factory}}.Make(2)
            local g = {{Factory}}.Make(3)
            local h = f + g
            return h(4)
            """);

        Assert.Equal(12L, results[0]);
    }

    [Fact]
    public void DelegateSubtraction_RemovesMatchingDelegate_AndKeepsCallable()
    {
        using var env = new LuaEnv();

        var results = env.DoString($$"""
            local f = {{Factory}}.Make(2)
            local g = {{Factory}}.Make(3)
            local other = {{Factory}}.Make(5)

            local removed = (f + g) - g
            local untouched = (f + g) - other

            return removed(4), untouched(4), type(untouched)
            """);

        Assert.Equal(8L, results[0]);
        Assert.Equal(12L, results[1]);
        Assert.Equal("userdata", results[2]);
    }

    [Fact]
    public void CallNonDelegateUserdata_Throws()
    {
        using var env = new LuaEnv();

        var exception = Assert.Throws<LuaException>(
            () => env.DoString("CS.LuaLoom.UnitTests.Sample.Player(1)(2)"));

        Assert.Contains("Only C# delegate objects", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DelegateCall_WithInvalidArguments_Throws()
    {
        using var env = new LuaEnv();

        var wrongType = Assert.Throws<LuaException>(() => env.DoString($$"""
            local f = {{Factory}}.Make(2)
            return f('x')
            """));
        Assert.Contains("No matching arguments", wrongType.Message, StringComparison.Ordinal);

        var wrongCount = Assert.Throws<LuaException>(() => env.DoString($$"""
            local f = {{Factory}}.Make(2)
            return f(1, 2)
            """));
        Assert.Contains("No matching arguments", wrongCount.Message, StringComparison.Ordinal);

        Assert.Equal(2L, env.DoString("return 1 + 1")[0]);
    }

    [Fact]
    public void LuaFunction_ConvertsToParseOpAndParamsOp()
    {
        using var env = new LuaEnv();
        env.RegisterDelegate(typeof(ParseOp), new ParseOpInvoker());
        env.RegisterDelegate(typeof(ParamsOp), new ParamsOpInvoker());

        env.DoString("""
            function parse(text)
                local value = tonumber(text)
                if value == nil then
                    return false, 0
                end
                return true, value
            end

            function sum(seed, values)
                local total = seed
                for _, value in ipairs(values) do
                    total = total + value
                end
                return total
            end
            """);

        var parse = env.Global.Get<ParseOp>("parse");
        var sum = env.Global.Get<ParamsOp>("sum");

        Assert.True(parse("42", out var parsed));
        Assert.Equal(42, parsed);

        Assert.False(parse("nope", out var missing));
        Assert.Equal(0, missing);

        Assert.Equal(16, sum(10, [1, 2, 3]));
        Assert.Equal(10, sum(10, []));
    }

    private sealed class ParseOpInvoker : ILuaDelegateInvoker
    {
        public Delegate Create(LuaFunction function) =>
            new ParseOp((string text, out int value) =>
            {
                var results = function.Call(text);
                value = results.Length > 1 ? checked((int)(long)results[1]) : 0;
                return results.Length > 0 && results[0] is true;
            });
    }

    private sealed class ParamsOpInvoker : ILuaDelegateInvoker
    {
        public Delegate Create(LuaFunction function) =>
            new ParamsOp((seed, values) =>
                checked((int)(long)function.Call(new object[] { seed, values })[0]));
    }
}
