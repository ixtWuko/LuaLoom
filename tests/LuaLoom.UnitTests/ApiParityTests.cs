using LuaLoom;
using LuaLoom.UnitTests.Sample;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class ApiParityTests
{
    [Fact]
    public void LoadStringGeneric_ReturnsDelegate()
    {
        using var env = new LuaEnv();
        env.RegisterDelegate(typeof(IntOp), new IntOpInvoker());

        var callback = env.LoadString<IntOp>("return function(a, b) return a + b end");

        Assert.Equal(5, callback(2, 3));
    }

    [Fact]
    public void DoStringEnvironment_IsUsed()
    {
        using var env = new LuaEnv();
        using var environment = env.NewTable();
        environment.Set("x", 2);

        var results = env.DoString("return x + 1", "env_script", environment);

        Assert.Equal(3L, results[0]);
    }

    [Fact]
    public void LuaFunctionSetEnv_IsUsed()
    {
        using var env = new LuaEnv();
        using var function = env.LoadString("return x");
        using var environment = env.NewTable();
        environment.Set("x", 7);

        function.SetEnv(environment);

        Assert.Equal(7L, function.Call()[0]);
    }

    [Fact]
    public void LuaTableGetOut_ReturnsValue()
    {
        using var env = new LuaEnv();
        using var table = env.NewTable();
        table.Set("x", 9);

        table.Get<string, int>("x", out var value);

        Assert.Equal(9, value);
    }

    private sealed class IntOpInvoker : ILuaDelegateInvoker
    {
        public Delegate Create(LuaFunction function) =>
            new IntOp((a, b) => checked((int)(long)function.Call(a, b)[0]));
    }
}
