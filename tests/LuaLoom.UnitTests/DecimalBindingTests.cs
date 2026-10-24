using LuaLoom;
using LuaLoom.Native;
using LuaLoom.UnitTests.Sample;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class DecimalBindingTests
{
    private const string DecimalType = "CS.LuaLoom.UnitTests.Sample.DecimalMethods";

    [Fact]
    public void Decimal_ReflectionPathReadsStructUserdata()
    {
        using var env = new LuaEnv();

        var results = env.DoString($"""
            local value = {DecimalType}.Make()
            return {DecimalType}.ToInt(value)
            """);

        Assert.Equal(12L, results[0]);
    }

    [Fact]
    public void Decimal_RoundTripsThroughUserdata()
    {
        using var env = new LuaEnv();
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(DecimalMethods),
            FullName = "LuaLoom.UnitTests.Sample.DecimalMethods",
            StaticIndex = StaticIndex,
        });

        var results = env.DoString($"""
            local value = {DecimalType}.Make()
            return {DecimalType}.ToInt(value)
            """);

        Assert.Equal(12L, results[0]);
    }

    private static readonly LuaCSFunction StaticIndexHandler = StaticIndex;
    private static readonly LuaCSFunction MakeHandler = Make;
    private static readonly LuaCSFunction ToIntHandler = ToInt;

    private static int StaticIndex(IntPtr state)
    {
        switch (LuaBinding.ReadString(state, 2))
        {
            case "Make":
                LuaBinding.PushFunction(state, MakeHandler);
                return 1;
            case "ToInt":
                LuaBinding.PushFunction(state, ToIntHandler);
                return 1;
            default:
                LuaBinding.PushNil(state);
                return 1;
        }
    }

    private static int Make(IntPtr state)
    {
        LuaBinding.PushStruct(state, 12.5m);
        return 1;
    }

    private static int ToInt(IntPtr state)
    {
        var value = LuaBinding.ReadStruct<decimal>(state, 1);
        LuaBinding.PushInt(state, (int)value);
        return 1;
    }
}
