using LuaLoom;
using LuaLoom.Native;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class LuaEnvApiTests
{
    [Fact]
    public void CustomMarshaler_IsUsedForPushAndRead()
    {
        using var env = new LuaEnv();
        env.RegisterMarshaler(new VersionMarshaler());

        env.Global.Set("version", new Version(1, 2, 3));
        var roundTrip = env.Global.Get<Version>("version");
        var luaType = env.DoString("return type(version), version")[0];

        Assert.Equal(new Version(1, 2, 3), roundTrip);
        Assert.Equal("string", luaType);
    }

    [Fact]
    public void LuaFunction_ReturnsAllResults()
    {
        using var env = new LuaEnv();
        using var function = env.LoadString("return 1, 'two', true");

        var results = function.Call();

        Assert.Equal(new object[] { 1L, "two", true }, results);
    }

    [Fact]
    public void Tick_CanBeCalledRepeatedly()
    {
        using var env = new LuaEnv();

        env.Tick();
        env.Tick();
    }

    [Fact]
    public void Logger_ReceivesMissingLoaderTrace()
    {
        var messages = new List<string>();
        using var env = new LuaEnv(new LuaEnvOptions
        {
            Logger = (_, message) => messages.Add(message),
            SearchPaths = ["missing/?.lua"],
        });

        Assert.Throws<LuaException>(() => env.DoString("require('not_present')"));
        Assert.Contains(messages, static message =>
            message.Contains("No LuaLoom loader matched", StringComparison.Ordinal));
    }

    private sealed class VersionMarshaler : ITypeMarshaler
    {
        public bool CanMarshal(Type type) => type == typeof(Version);

        public void Push(IntPtr state, object value) =>
            LuaBinding.PushString(state, ((Version)value).ToString());

        public object? Read(IntPtr state, int index, Type type) =>
            Version.Parse(LuaBinding.ReadString(state, index)!);
    }
}
