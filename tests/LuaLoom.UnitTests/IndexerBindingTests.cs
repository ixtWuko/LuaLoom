using LuaLoom;
using LuaLoom.Native;
using LuaLoom.UnitTests.Sample;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class IndexerBindingTests
{
    private const string BagType = "CS.LuaLoom.UnitTests.Sample.Bag";

    [Fact]
    public void RegisteredIndexerBinding_HandlesNonStringKeys()
    {
        using var env = new LuaEnv();
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Bag),
            FullName = "LuaLoom.UnitTests.Sample.Bag",
            InstanceIndexer = GetIndexer,
            InstanceIndexerSet = SetIndexer,
        });

        var results = env.DoString($"""
            local bag = {BagType}()
            bag[1] = 5
            return bag[1]
            """);

        Assert.Equal(5L, results[0]);
    }

    private static readonly LuaCSFunction GetIndexerHandler = GetIndexer;
    private static readonly LuaCSFunction SetIndexerHandler = SetIndexer;

    private static int GetIndexer(IntPtr state)
    {
        var bag = (Bag)LuaBinding.ReadObject(state, 1)!;
        var key = LuaBinding.ReadInt(state, 2);
        LuaBinding.PushInt(state, bag[key]);
        return 1;
    }

    private static int SetIndexer(IntPtr state)
    {
        var bag = (Bag)LuaBinding.ReadObject(state, 1)!;
        var key = LuaBinding.ReadInt(state, 2);
        bag[key] = LuaBinding.ReadInt(state, 3);
        return 0;
    }
}
