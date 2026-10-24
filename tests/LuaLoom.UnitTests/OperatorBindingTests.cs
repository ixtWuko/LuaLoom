using LuaLoom;
using LuaLoom.Native;
using LuaLoom.UnitTests.Sample;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class OperatorBindingTests
{
    private const string MoneyType = "CS.LuaLoom.UnitTests.Sample.Money";

    [Fact]
    public void RegisteredOperatorBinding_TakesPrecedence()
    {
        using var env = new LuaEnv();
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Money),
            FullName = "LuaLoom.UnitTests.Sample.Money",
            Operators = new Dictionary<string, LuaCSFunction>
            {
                ["__add"] = Add,
            },
        });

        var results = env.DoString($"""
            local left = {MoneyType}(1)
            local right = {MoneyType}(2)
            return (left + right).Amount
            """);

        Assert.Equal(3L, results[0]);
    }

    private static readonly LuaCSFunction AddHandler = Add;

    private static int Add(IntPtr state)
    {
        var left = (Money)LuaBinding.ReadObject(state, 1)!;
        var right = (Money)LuaBinding.ReadObject(state, 2)!;
        LuaBinding.PushObject(state, left + right);
        return 1;
    }
}
