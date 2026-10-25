using LuaLoom;
using LuaLoom.UnitTests.Sample;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class CastHandleTests
{
    [Fact]
    public void Cast_LuaTableMember_KeepsHandleAlive()
    {
        using var env = new LuaEnv();
        using var table = (LuaTable)env.DoString("return { Payload = { Value = 7 } }")[0]!;

        var holder = table.Cast<TableHolder>();

        Assert.NotNull(holder.Payload);
        Assert.Equal(7, holder.Payload!.Get<int>("Value"));
        holder.Payload.Dispose();
    }
}
