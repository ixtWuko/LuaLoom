using LuaLoom;
using LuaLoom.Native;
using LuaLoom.UnitTests.Sample;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class InteropTests
{
    private const string PlayerType = "CS.LuaLoom.UnitTests.Sample.Player";

    [Fact]
    public void ConstructAndReadInstanceField()
    {
        using var env = new LuaEnv();

        var results = env.DoString($"local p = {PlayerType}(5); return p.Hp");

        Assert.Equal(5L, results[0]);
    }

    [Fact]
    public void WriteInstanceField()
    {
        using var env = new LuaEnv();

        var results = env.DoString($$"""
            local p = {{PlayerType}}(1)
            p.Hp = 42
            return p.Hp
            """);

        Assert.Equal(42L, results[0]);
    }

    [Fact]
    public void CallInstanceMethod()
    {
        using var env = new LuaEnv();

        var results = env.DoString($$"""
            local p = {{PlayerType}}(1)
            return p:Add(2, 3)
            """);

        Assert.Equal(5L, results[0]);
    }

    [Fact]
    public void ReadAndWriteStaticMembers()
    {
        using var env = new LuaEnv();

        var results = env.DoString($$"""
            {{PlayerType}}.Count = 3
            return {{PlayerType}}.Count, {{PlayerType}}.Double(4)
            """);

        Assert.Equal(3L, results[0]);
        Assert.Equal(8L, results[1]);
    }

    [Fact]
    public void ReflectionFallbackDisabled_UnregisteredTypeThrows()
    {
        using var env = new LuaEnv(new LuaEnvOptions { EnableReflectionFallback = false });

        var exception = Assert.Throws<LuaException>(
            () => env.DoString($"return {PlayerType}(1)"));

        Assert.Contains("not registered", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReflectionFallback_MissingMemberReturnsNil()
    {
        using var env = new LuaEnv();

        var results = env.DoString($"local p = {PlayerType}(1); return p.Missing");

        Assert.Single(results);
        Assert.Null(results[0]);
    }

    [Fact]
    public void CastObject_WithNestedStruct()
    {
        using var env = new LuaEnv();

        using var table = (LuaTable)env.DoString(
            "return { Hp = 10, Name = 'p', Pos = { X = 1.5, Y = 2.5 } }")[0]!;

        var config = table.Cast<Config>();

        Assert.Equal(10, config.Hp);
        Assert.Equal("p", config.Name);
        Assert.Equal(1.5f, config.Pos.X);
        Assert.Equal(2.5f, config.Pos.Y);
    }

    [Fact]
    public void CastIntArray_DoesNotBoxElements()
    {
        using var env = new LuaEnv();
        using var table = (LuaTable)env.DoString(
            "local t = {} for i = 1, 1000 do t[i] = i end return t")[0]!;

        _ = table.Cast<int[]>();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var values = table.Cast<int[]>();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(1000, values.Length);
        Assert.Equal(1, values[0]);
        Assert.True(allocated < 8000, $"allocated {allocated} bytes for int[1000]");
    }

    [Fact]
    public void CastListAndArray()
    {
        using var env = new LuaEnv();

        using var table = (LuaTable)env.DoString("return { 1, 2, 3 }")[0]!;

        Assert.Equal([1, 2, 3], table.Cast<List<int>>());
        Assert.Equal([1, 2, 3], table.Cast<int[]>());
    }

    [Fact]
    public void CastDictionary()
    {
        using var env = new LuaEnv();

        using var table = (LuaTable)env.DoString("return { a = 1, b = 2 }")[0]!;

        var dictionary = table.Cast<Dictionary<string, int>>();

        Assert.Equal(1, dictionary["a"]);
        Assert.Equal(2, dictionary["b"]);
    }

    [Fact]
    public void CastInterface_UsesDispatchProxyFallback()
    {
        using var env = new LuaEnv();

        using var table = (LuaTable)env.DoString("""
            local impl = { value = 0 }
            function impl:Add(a, b) return a + b end
            function impl:get_Value() return self.value end
            function impl:set_Value(v) self.value = v end
            return impl
            """)[0]!;

        var calculator = table.Cast<ICalculator>();
        Assert.Equal(5, calculator.Add(2, 3));

        calculator.Value = 9;
        Assert.Equal(9, calculator.Value);
    }

    [Fact]
    public void CastInterface_WithReflectionFallbackDisabled_Throws()
    {
        using var env = new LuaEnv(new LuaEnvOptions { EnableReflectionFallback = false });
        using var table = (LuaTable)env.DoString("return { Add = function(self, a, b) return a + b end }")[0]!;

        var exception = Assert.Throws<InvalidOperationException>(() => table.Cast<ICalculator>());
        Assert.Contains("Interface proxy", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisteredInterfaceFactory_TakesPrecedence()
    {
        using var env = new LuaEnv();
        var implementation = new Calculator();
        env.RegisterInterface(typeof(ICalculator), _ => implementation);

        using var table = (LuaTable)env.DoString("return {}")[0]!;

        Assert.Same(implementation, table.Cast<ICalculator>());
    }

    [Fact]
    public void UnregisteredDelegate_ThrowsWithConfigurationHint()
    {
        using var env = new LuaEnv();
        using var function = (LuaFunction)env.DoString("return function(a, b) return a + b end")[0]!;
        env.Global.Set("callback", function);

        var exception = Assert.Throws<LuaConversionException>(() => env.Global.Get<IntOp>("callback"));

        Assert.Contains("csharpCallLua", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisteredDelegate_InvokesLua()
    {
        using var env = new LuaEnv();
        using var function = (LuaFunction)env.DoString("return function(a, b) return a + b end")[0]!;
        env.Global.Set("callback", function);
        env.RegisterDelegate(typeof(IntOp), new IntOpInvoker());

        var callback = env.Global.Get<IntOp>("callback");

        Assert.Equal(5, callback(2, 3));
    }

    [Fact]
    public void ExceptionInCallback_DoesNotCorruptStack()
    {
        using var env = new LuaEnv();

        var exception = Assert.Throws<LuaException>(
            () => env.DoString("CS.LuaLoom.UnitTests.Sample.Thrower.Boom()", "boom"));

        Assert.Contains("boom", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2L, env.DoString("return 1 + 1")[0]);
    }

    [Fact]
    public void ErrorPaths_LeaveStackBalanced()
    {
        using var env = new LuaEnv();
        var state = env.Marshal.State;

        Assert.Throws<LuaException>(() => env.DoString("CS.LuaLoom.UnitTests.Sample.Thrower.Boom()"));
        Assert.Equal(0, LuaApi.lua_gettop(state));

        Assert.Throws<LuaException>(() => env.DoString("error('boom')"));
        Assert.Equal(0, LuaApi.lua_gettop(state));

        Assert.Throws<LuaException>(() => env.DoString("CS.LuaLoom.UnitTests.Sample.Player('not a number')"));
        Assert.Equal(0, LuaApi.lua_gettop(state));

        Assert.Equal(2L, env.DoString("return 1 + 1")[0]);
    }

    [Fact]
    public void RegisteredGenericDelegates_InvokeLua()
    {
        using var env = new LuaEnv();
        var seen = 0;
        env.RegisterDelegate(typeof(Action<int>), new ActionIntInvoker(value => seen = value));
        env.RegisterDelegate(typeof(Func<int, int>), new DoubleIntInvoker());

        using var action = (LuaFunction)env.DoString("return function(value) seen = value end")[0]!;
        using var function = (LuaFunction)env.DoString("return function(value) return value * 2 end")[0]!;
        env.Global.Set("on_value", action);
        env.Global.Set("double_value", function);
        env.Global.Set("seen", 0);

        var onValue = env.Global.Get<Action<int>>("on_value");
        var doubleValue = env.Global.Get<Func<int, int>>("double_value");

        onValue(7);

        Assert.Equal(7, seen);
        Assert.Equal(7L, env.Global.Get<long>("seen"));
        Assert.Equal(10, doubleValue(5));
    }

    private sealed class IntOpInvoker : ILuaDelegateInvoker
    {
        public Delegate Create(LuaFunction function) =>
            new IntOp((a, b) => checked((int)(long)function.Call(a, b)[0]));
    }

    private sealed class ActionIntInvoker : ILuaDelegateInvoker
    {
        private readonly Action<int> _callback;

        public ActionIntInvoker(Action<int> callback) => _callback = callback;

        public Delegate Create(LuaFunction function) =>
            new Action<int>(value =>
            {
                function.Call(value);
                _callback(value);
            });
    }

    private sealed class DoubleIntInvoker : ILuaDelegateInvoker
    {
        public Delegate Create(LuaFunction function) =>
            new Func<int, int>(value => checked((int)(long)function.Call(value)[0]));
    }
}
