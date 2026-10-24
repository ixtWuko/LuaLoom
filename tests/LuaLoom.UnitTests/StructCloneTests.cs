using LuaLoom;
using LuaLoom.Native;
using LuaLoom.UnitTests.Sample;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class StructCloneTests
{
    private const string VecType = "CS.LuaLoom.UnitTests.Sample.Vec2";

    [Fact]
    public void StructClone_CopiesUserdata()
    {
        using var env = new LuaEnv();
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Vec2),
            FullName = "LuaLoom.UnitTests.Sample.Vec2",
            StaticIndex = StaticIndex,
        });

        var results = env.DoString($"""
            local value = {VecType}.Make()
            local clone = xlua.structclone(value)
            return {VecType}.Read(clone)
            """);

        Assert.Equal(3d, Convert.ToDouble(results[0]));
    }

    private static readonly LuaCSFunction StaticIndexHandler = StaticIndex;
    private static readonly LuaCSFunction MakeHandler = Make;
    private static readonly LuaCSFunction ReadHandler = Read;

    private static int StaticIndex(IntPtr state)
    {
        switch (LuaBinding.ReadString(state, 2))
        {
            case "Make":
                LuaBinding.PushFunction(state, MakeHandler);
                return 1;
            case "Read":
                LuaBinding.PushFunction(state, ReadHandler);
                return 1;
            default:
                LuaBinding.PushNil(state);
                return 1;
        }
    }

    private static int Make(IntPtr state)
    {
        LuaBinding.PushStruct(state, new Vec2 { X = 1, Y = 2 });
        return 1;
    }

    private static int Read(IntPtr state)
    {
        var value = LuaBinding.ReadStruct<Vec2>(state, 1);
        LuaBinding.PushDouble(state, value.X + value.Y);
        return 1;
    }
}
