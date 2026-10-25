using LuaLoom;
using LuaLoom.Native;
using LuaLoom.UnitTests.Sample;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class StrictModeTests
{
    private const string PlayerName = "LuaLoom.UnitTests.Sample.Player";

    private static readonly LuaCSFunction NewPlayerHandler = NewPlayer;
    private static readonly LuaCSFunction PlayerHpIndexHandler = PlayerHpIndex;
    private static readonly LuaCSFunction EmptyIndexHandler = EmptyIndex;
    private static readonly LuaCSFunction NewEmitterHandler = NewEmitter;
    private static readonly LuaCSFunction NewBagHandler = NewBag;
    private static readonly LuaCSFunction StaticCountHandler = StaticCount;
    private static readonly LuaCSFunction HostStaticIndexHandler = HostStaticIndex;
    private static readonly LuaCSFunction UseCalculatorCallback = UseCalculator;

    [Fact]
    public void UnregisteredType_IsBlocked()
    {
        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });

        var exception = Assert.Throws<LuaException>(
            () => env.DoString("return CS.LuaLoom.UnitTests.Sample.Money"));

        Assert.True(
            exception.Message.Contains("Strict reflection mode blocked", StringComparison.Ordinal)
                || exception.Message.Contains("not registered", StringComparison.Ordinal),
            exception.Message);
    }

    [Fact]
    public void MissingCall_RejectsConstructorFallback()
    {
        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });
        env.RegisterType(new LuaTypeBinding { Type = typeof(Player), FullName = PlayerName });

        var exception = Assert.Throws<LuaException>(() => env.DoString("return CS.LuaLoom.UnitTests.Sample.Player(1)"));

        Assert.Contains("Strict reflection mode blocked", exception.Message, StringComparison.Ordinal);
        Assert.Contains("constructor", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingStaticIndex_RejectsStaticRead()
    {
        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });
        env.RegisterType(new LuaTypeBinding { Type = typeof(Player), FullName = PlayerName });

        var exception = Assert.Throws<LuaException>(
            () => env.DoString("return CS.LuaLoom.UnitTests.Sample.Player.Count"));

        Assert.Contains("Strict reflection mode blocked", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Player.Count", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingStaticNewIndex_RejectsStaticWrite()
    {
        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });
        env.RegisterType(new LuaTypeBinding { Type = typeof(Player), FullName = PlayerName });

        var exception = Assert.Throws<LuaException>(
            () => env.DoString("CS.LuaLoom.UnitTests.Sample.Player.Count = 4"));

        Assert.Contains("Strict reflection mode blocked", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Player.Count", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingInstanceIndex_RejectsInstanceRead()
    {
        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Player),
            FullName = PlayerName,
            Call = NewPlayerHandler,
        });

        var exception = Assert.Throws<LuaException>(
            () => env.DoString("local p = CS.LuaLoom.UnitTests.Sample.Player(1); return p.Name"));

        Assert.Contains("Strict reflection mode blocked", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Player.Name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingInstanceNewIndex_RejectsInstanceWrite()
    {
        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Player),
            FullName = PlayerName,
            Call = NewPlayerHandler,
        });

        var exception = Assert.Throws<LuaException>(
            () => env.DoString("local p = CS.LuaLoom.UnitTests.Sample.Player(1); p.Name = 'x'"));

        Assert.Contains("Strict reflection mode blocked", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Player.Name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingEventThunk_RejectsEventSubscription()
    {
        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Emitter),
            FullName = "LuaLoom.UnitTests.Sample.Emitter",
            Call = NewEmitterHandler,
            InstanceIndex = EmptyIndexHandler,
        });

        var exception = Assert.Throws<LuaException>(() => env.DoString("""
            local emitter = CS.LuaLoom.UnitTests.Sample.Emitter()
            emitter:Changed('+', function(value) end)
            """));

        Assert.Contains("Strict reflection mode blocked", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Emitter.Changed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingIndexerThunks_RejectIndexerReadAndWrite()
    {
        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Bag),
            FullName = "LuaLoom.UnitTests.Sample.Bag",
            Call = NewBagHandler,
        });

        var read = Assert.Throws<LuaException>(
            () => env.DoString("local bag = CS.LuaLoom.UnitTests.Sample.Bag(); return bag[1]"));
        Assert.Contains("Strict reflection mode blocked", read.Message, StringComparison.Ordinal);
        Assert.Contains("indexer read", read.Message, StringComparison.Ordinal);

        var write = Assert.Throws<LuaException>(
            () => env.DoString("local bag = CS.LuaLoom.UnitTests.Sample.Bag(); bag[1] = 5"));
        Assert.Contains("Strict reflection mode blocked", write.Message, StringComparison.Ordinal);
        Assert.Contains("indexer write", write.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingGeneratedExtension_RejectsReflectedExtensionMethod()
    {
        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });
        env.RegisterExtensionType(typeof(PlayerExtensions));
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Player),
            FullName = PlayerName,
            Call = NewPlayerHandler,
        });

        var exception = Assert.Throws<LuaException>(
            () => env.DoString("local p = CS.LuaLoom.UnitTests.Sample.Player(2); return p:Triple(2)"));

        Assert.Contains("Strict reflection mode blocked", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Player.Triple", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UngeneratedInterface_RejectsDispatchProxyFallback()
    {
        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(InteropHost),
            FullName = "LuaLoom.UnitTests.Sample.InteropHost",
            StaticIndex = HostStaticIndexHandler,
        });

        var exception = Assert.Throws<LuaException>(() => env.DoString("""
            return CS.LuaLoom.UnitTests.Sample.InteropHost.UseCalculator(
                { Add = function(self, a, b) return a + b end })
            """));

        Assert.Contains("ICalculator", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitReflectionApis_AreStillAllowed()
    {
        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(PrivateMembers),
            FullName = "LuaLoom.UnitTests.Sample.PrivateMembers",
        });
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Player),
            FullName = PlayerName,
            Call = NewPlayerHandler,
            InstanceIndex = PlayerHpIndexHandler,
        });

        var results = env.DoString("""
            xlua.private_accessible(CS.LuaLoom.UnitTests.Sample.PrivateMembers)
            local player = CS.LuaLoom.UnitTests.Sample.Player(3)
            local playerType = typeof('LuaLoom.UnitTests.Sample.Player')
            local casted = cast(player, playerType)
            return casted.Hp
            """);
        Assert.Single(results);
        Assert.Equal(3L, results[0]);
    }

    [Fact]
    public void CompleteManualBinding_IsAllowed()
    {
        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Player),
            FullName = PlayerName,
            Call = NewPlayerHandler,
            InstanceIndex = PlayerHpIndexHandler,
            StaticIndex = StaticCountHandler,
        });

        var results = env.DoString("""
            local player = CS.LuaLoom.UnitTests.Sample.Player(3)
            return player.Hp, CS.LuaLoom.UnitTests.Sample.Player.Count
            """);

        Assert.Equal(3L, results[0]);
        Assert.Equal(99L, results[1]);
    }

    private static int NewPlayer(IntPtr state)
    {
        var hp = LuaBinding.ReadInt(state, 2);
        LuaBinding.PushObject(state, new Player(hp));
        return 1;
    }

    private static int PlayerHpIndex(IntPtr state)
    {
        if (LuaBinding.ReadString(state, 2) != "Hp")
        {
            return 0;
        }

        var player = (Player)LuaBinding.ReadObject(state, 1)!;
        LuaBinding.PushInt(state, player.Hp);
        return 1;
    }

    private static int EmptyIndex(IntPtr state) => 0;

    private static int NewEmitter(IntPtr state)
    {
        LuaBinding.PushObject(state, new Emitter());
        return 1;
    }

    private static int NewBag(IntPtr state)
    {
        LuaBinding.PushObject(state, new Bag());
        return 1;
    }

    private static int StaticCount(IntPtr state)
    {
        if (LuaBinding.ReadString(state, 2) == "Count")
        {
            LuaBinding.PushInt(state, 99);
            return 1;
        }

        return 0;
    }

    private static int HostStaticIndex(IntPtr state)
    {
        if (LuaBinding.ReadString(state, 2) != "UseCalculator")
        {
            return 0;
        }

        LuaBinding.PushFunction(state, UseCalculatorCallback);
        return 1;
    }

    private static int UseCalculator(IntPtr state)
    {
        ICalculator calculator;
        try
        {
            calculator = LuaBinding.ReadObjectAs<ICalculator>(state, 1);
        }
        catch (LuaConversionException exception)
        {
            LuaBinding.Throw(state, exception.Message);
            return 0;
        }

        LuaBinding.PushInt(state, InteropHost.UseCalculator(calculator));
        return 1;
    }
}
