using LuaLoom;
using LuaLoom.Native;
using LuaLoom.UnitTests.Sample;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class ArrayBindingTests
{
    private const string ArrayType = "CS.LuaLoom.UnitTests.Sample.ArrayMethods";

    [Fact]
    public void ArrayArgumentsAndReturns_RoundTrip()
    {
        using var env = new LuaEnv();
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(ArrayMethods),
            FullName = "LuaLoom.UnitTests.Sample.ArrayMethods",
            StaticIndex = StaticIndex,
        });

        var results = env.DoString($$"""
            local doubled = {{ArrayType}}.Double({ 1, 2, 3 })
            return doubled[1], doubled[2], doubled[3], {{ArrayType}}.Sum({ 4, 5 })
            """);

        Assert.Equal(2L, results[0]);
        Assert.Equal(4L, results[1]);
        Assert.Equal(6L, results[2]);
        Assert.Equal(9L, results[3]);
    }

    private static readonly LuaCSFunction StaticIndexHandler = StaticIndex;
    private static readonly LuaCSFunction SumHandler = Sum;
    private static readonly LuaCSFunction DoubleHandler = Double;

    private static int StaticIndex(IntPtr state)
    {
        var key = LuaBinding.ReadString(state, 2);
        switch (key)
        {
            case "Sum":
                LuaBinding.PushFunction(state, SumHandler);
                return 1;
            case "Double":
                LuaBinding.PushFunction(state, DoubleHandler);
                return 1;
            default:
                LuaBinding.PushNil(state);
                return 1;
        }
    }

    private static int Sum(IntPtr state)
    {
        var values = LuaBinding.ReadArray<int>(state, 1);
        LuaBinding.PushInt(state, ArrayMethods.Sum(values));
        return 1;
    }

    private static int Double(IntPtr state)
    {
        var values = LuaBinding.ReadArray<int>(state, 1);
        LuaBinding.PushArray(state, ArrayMethods.Double(values));
        return 1;
    }
}
