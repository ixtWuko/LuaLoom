using System.Reflection;
using System.Runtime.Loader;
using LuaLoom;
using LuaLoom.CodeGen;
using LuaLoom.Native;
using LuaLoom.UnitTests.Sample;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class CodeGenTests
{
    private static GeneratorConfig CreateConfig(string assemblyPath) => new()
    {
        Assembly = assemblyPath,
        Output = "generated",
        Namespace = "LuaLoom.Generated",
        LuaCallCSharp =
        [
            "LuaLoom.UnitTests.Sample.Player",
            "LuaLoom.UnitTests.Sample.DerivedPlayer",
            "LuaLoom.UnitTests.Sample.Overloads",
            "LuaLoom.UnitTests.Sample.PlayerExtensions",
            "LuaLoom.UnitTests.Sample.Bag",
            "LuaLoom.UnitTests.Sample.Parsing",
            "LuaLoom.UnitTests.Sample.ArrayMethods",
            "LuaLoom.UnitTests.Sample.Money",
            "LuaLoom.UnitTests.Sample.PointMath",
            "LuaLoom.UnitTests.Sample.Emitter",
            "LuaLoom.UnitTests.Sample.DecimalMethods",
            "LuaLoom.UnitTests.Sample.ComplexOverloads",
            "LuaLoom.UnitTests.Sample.DelegateFactory",
            "LuaLoom.UnitTests.Sample.InteropHost",
            "LuaLoom.UnitTests.Sample.UnsupportedSurface",
        ],
        GenericMethods =
        [
            "LuaLoom.UnitTests.Sample.Generics.Describe<int>",
        ],
        CSharpCallLua =
        [
            "LuaLoom.UnitTests.Sample.IntOp",
            "LuaLoom.UnitTests.Sample.ICalculator",
            "LuaLoom.UnitTests.Sample.IArrayCalculator",
            "LuaLoom.UnitTests.Sample.IGenericCalculator",
            "LuaLoom.UnitTests.Sample.IEventSource",
            "LuaLoom.UnitTests.Sample.IntUnary",
            "LuaLoom.UnitTests.Sample.ParseOp",
            "LuaLoom.UnitTests.Sample.ParamsOp",
            "LuaLoom.UnitTests.Sample.ArrayOp",
            "System.Action<int>",
            "System.Func<int, string>",
            "LuaLoom.UnitTests.Sample.Mapper<int, string>",
        ],
    };

    [Fact]
    public void Generate_ProducesExpectedFiles()
    {
        var result = CodeGenerator.Generate(CreateConfig(typeof(Player).Assembly.Location), AppContext.BaseDirectory);

        Assert.Empty(result.Diagnostics);
        var names = result.Files.Select(static f => f.FileName).OrderBy(static n => n, StringComparer.Ordinal).ToArray();
        Assert.Contains("LuaLoom_UnitTests_Sample_PlayerBinder.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_DerivedPlayerBinder.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_OverloadsBinder.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_BagBinder.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_ParsingBinder.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_ArrayMethodsBinder.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_MoneyBinder.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_PointMathBinder.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_EmitterBinder.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_DecimalMethodsBinder.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_ComplexOverloadsBinder.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_UnsupportedSurfaceBinder.g.cs", names);
        Assert.Contains(
            names,
            static name => name.StartsWith("LuaLoom_UnitTests_Sample_Generics_Describe_", StringComparison.Ordinal)
                && name.EndsWith("GenericBinder.g.cs", StringComparison.Ordinal));
        Assert.Contains(
            result.Coverage,
            static entry => entry.Member.Contains("Generic", StringComparison.Ordinal)
                && entry.Status == "reflection"
                && entry.Reason == "generic method");
        Assert.Contains(
            result.Coverage,
            static entry => entry.Member.Contains("Unsupported", StringComparison.Ordinal)
                && entry.Status == "reflection"
                && entry.Reason.Contains("unsupported parameter", StringComparison.Ordinal));
        Assert.Contains(
            result.Coverage,
            static entry => entry.Member == "Value" && entry.Status == "generated");
        Assert.Contains(
            result.Coverage,
            static entry => entry.Member == "Describe<Int32>" && entry.Status == "generated");
        Assert.Contains("LuaLoom_UnitTests_Sample_IntOpInvoker.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_ParseOpInvoker.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_ParamsOpInvoker.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_ArrayOpInvoker.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_ICalculatorImpl.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_IArrayCalculatorImpl.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_IGenericCalculatorImpl.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_IEventSourceImpl.g.cs", names);
        Assert.Contains("System_Action_int_Invoker.g.cs", names);
        Assert.Contains(
            names,
            static name => name.StartsWith("System_Func_", StringComparison.Ordinal)
                && name.EndsWith("Invoker.g.cs", StringComparison.Ordinal));
        Assert.Contains(
            names,
            static name => name.StartsWith("LuaLoom_UnitTests_Sample_Mapper_", StringComparison.Ordinal)
                && name.EndsWith("Invoker.g.cs", StringComparison.Ordinal));
        Assert.Contains("LuaBindings.g.cs", names);
        Assert.Contains("link.xml", names);

        var generic = result.Files.Single(static f => f.FileName == "System_Action_int_Invoker.g.cs");
        var bindings = result.Files.Single(static f => f.FileName == "LuaBindings.g.cs");
        Assert.Contains("new global::System.Action<int>", generic.Content, StringComparison.Ordinal);
        Assert.Contains("typeof(global::System.Action<int>)", bindings.Content, StringComparison.Ordinal);
        Assert.Contains(
            "RegisterExtensionType(typeof(global::LuaLoom.UnitTests.Sample.PlayerExtensions))",
            bindings.Content,
            StringComparison.Ordinal);
        Assert.Contains(
            "RegisterCast(typeof(global::LuaLoom.UnitTests.Sample.Player)",
            bindings.Content,
            StringComparison.Ordinal);
        Assert.Contains(
            "PlayerExtensionsBinder.RegisterExtensions(env)",
            bindings.Content,
            StringComparison.Ordinal);

        var genericInterface = result.Files.Single(
            static f => f.FileName == "LuaLoom_UnitTests_Sample_IGenericCalculatorImpl.g.cs");
        Assert.Contains("T Echo<T>", genericInterface.Content, StringComparison.Ordinal);

        var eventInterface = result.Files.Single(
            static f => f.FileName == "LuaLoom_UnitTests_Sample_IEventSourceImpl.g.cs");
        Assert.Contains("add => CallEvent(\"add_Changed\", value)", eventInterface.Content, StringComparison.Ordinal);

        var bag = result.Files.Single(static f => f.FileName == "LuaLoom_UnitTests_Sample_BagBinder.g.cs");
        Assert.Contains("InstanceIndexer = IndexerGet", bag.Content, StringComparison.Ordinal);
        Assert.Contains("InstanceIndexerSet = IndexerSet", bag.Content, StringComparison.Ordinal);

        var parsing = result.Files.Single(static f => f.FileName == "LuaLoom_UnitTests_Sample_ParsingBinder.g.cs");
        Assert.Contains("out a1", parsing.Content, StringComparison.Ordinal);
        Assert.Contains("ref a0", parsing.Content, StringComparison.Ordinal);
        Assert.Contains("count -", parsing.Content, StringComparison.Ordinal);

        var arrays = result.Files.Single(static f => f.FileName == "LuaLoom_UnitTests_Sample_ArrayMethodsBinder.g.cs");
        Assert.Contains("ReadArray<int>", arrays.Content, StringComparison.Ordinal);
        Assert.Contains("PushArray<int>", arrays.Content, StringComparison.Ordinal);

        var money = result.Files.Single(static f => f.FileName == "LuaLoom_UnitTests_Sample_MoneyBinder.g.cs");
        Assert.Contains("Operators = new", money.Content, StringComparison.Ordinal);
        Assert.Contains("Operator_Addition", money.Content, StringComparison.Ordinal);

        var emitter = result.Files.Single(static f => f.FileName == "LuaLoom_UnitTests_Sample_EmitterBinder.g.cs");
        Assert.Contains("Event_Changed", emitter.Content, StringComparison.Ordinal);
        Assert.Contains("LuaEventBridge.Add", emitter.Content, StringComparison.Ordinal);

        var decimals = result.Files.Single(static f => f.FileName == "LuaLoom_UnitTests_Sample_DecimalMethodsBinder.g.cs");
        Assert.Contains("ReadStruct<decimal>", decimals.Content, StringComparison.Ordinal);
        Assert.Contains("PushStruct<decimal>", decimals.Content, StringComparison.Ordinal);
        Assert.Matches("^[A-Za-z0-9_]+\\.g\\.cs$", generic.FileName);
    }

    [Fact]
    public void Generate_EmitsOverloadDispatchAndSkipsObsoleteAndOptionalMethods()
    {
        var config = CreateConfig(typeof(Player).Assembly.Location);
        config.LuaCallCSharp = ["LuaLoom.UnitTests.Sample.Overloads"];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Empty(result.Diagnostics);
        var binder = result.Files.Single(static f => f.FileName == "LuaLoom_UnitTests_Sample_OverloadsBinder.g.cs");
        Assert.Contains("Pick", binder.Content, StringComparison.Ordinal);
        Assert.Contains("IsInteger", binder.Content, StringComparison.Ordinal);
        Assert.Contains("Optional", binder.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Legacy", binder.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ReflectionUseOnlyPreservesMetadata()
    {
        var config = CreateConfig(typeof(Player).Assembly.Location);
        config.LuaCallCSharp = [];
        config.CSharpCallLua = [];
        config.GenericMethods = [];
        config.ReflectionUse = ["LuaLoom.UnitTests.Sample.UnsupportedSurface"];
        config.GCOptimize = ["LuaLoom.UnitTests.Sample.Vec2"];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(
            result.Files,
            static f => f.FileName == "LuaLoom_UnitTests_Sample_UnsupportedSurfaceBinder.g.cs");
        var link = result.Files.Single(static f => f.FileName == "link.xml");
        Assert.Contains("LuaLoom.UnitTests.Sample.UnsupportedSurface", link.Content, StringComparison.Ordinal);
        Assert.Contains("LuaLoom.UnitTests.Sample.Vec2", link.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_BlackListAndDoNotGenSkipMembers()
    {
        var config = CreateConfig(typeof(Player).Assembly.Location);
        config.LuaCallCSharp = ["LuaLoom.UnitTests.Sample.Player"];
        config.CSharpCallLua = [];
        config.BlackList = ["LuaLoom.UnitTests.Sample.Player.Add"];
        config.DoNotGen = ["LuaLoom.UnitTests.Sample.Player.Name"];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Empty(result.Diagnostics);
        var binder = result.Files.Single(static f => f.FileName == "LuaLoom_UnitTests_Sample_PlayerBinder.g.cs");
        Assert.DoesNotContain("case \"Add\":", binder.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("case \"Name\":", binder.Content, StringComparison.Ordinal);
        Assert.Contains("case \"Hp\":", binder.Content, StringComparison.Ordinal);
        Assert.Contains(
            result.Coverage,
            static entry => entry.Member.StartsWith("Add(", StringComparison.Ordinal)
                && entry.Status == "skipped"
                && entry.Reason == "blacklisted member");
        Assert.Contains(
            result.Coverage,
            static entry => entry.Member == "Name"
                && entry.Status == "skipped"
                && entry.Reason == "do-not-gen member");
    }

    [Fact]
    public void Generate_BlackListedTypeSkipsBinder()
    {
        var config = CreateConfig(typeof(Player).Assembly.Location);
        config.LuaCallCSharp = ["LuaLoom.UnitTests.Sample.ComplexOverloads"];
        config.CSharpCallLua = [];
        config.DoNotGen = ["LuaLoom.UnitTests.Sample.ComplexOverloads"];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(
            result.Files,
            static f => f.FileName == "LuaLoom_UnitTests_Sample_ComplexOverloadsBinder.g.cs");
        Assert.Contains(
            result.Coverage,
            static entry => entry.Member == "<type>"
                && entry.Status == "skipped"
                && entry.Reason == "do-not-gen type");
    }

    [Fact]
    public void Generate_InheritedMembersOnlyWhenBaseIsConfigured()
    {
        var config = CreateConfig(typeof(Player).Assembly.Location);
        config.LuaCallCSharp = ["LuaLoom.UnitTests.Sample.DerivedPlayer"];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Empty(result.Diagnostics);
        var binder = result.Files.Single(static f => f.FileName == "LuaLoom_UnitTests_Sample_DerivedPlayerBinder.g.cs");
        Assert.Contains("Bonus", binder.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("case \"Hp\":", binder.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("case \"Name\":", binder.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_InheritedMembersWhenBaseIsConfigured()
    {
        var config = CreateConfig(typeof(Player).Assembly.Location);
        config.LuaCallCSharp = ["LuaLoom.UnitTests.Sample.Player", "LuaLoom.UnitTests.Sample.DerivedPlayer"];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Empty(result.Diagnostics);
        var binder = result.Files.Single(static f => f.FileName == "LuaLoom_UnitTests_Sample_DerivedPlayerBinder.g.cs");
        Assert.Contains("Bonus", binder.Content, StringComparison.Ordinal);
        Assert.Contains("case \"Hp\":", binder.Content, StringComparison.Ordinal);
        Assert.Contains("case \"Name\":", binder.Content, StringComparison.Ordinal);
        Assert.Contains("Double", binder.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_OpenGenericDelegate_ReportsLL0003()
    {
        var config = CreateConfig(typeof(Player).Assembly.Location);
        config.CSharpCallLua = ["System.Action`1"];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Contains(result.Diagnostics, static d => d.Code == "LL0003");
    }

    [Fact]
    public void Generate_AngleBracketOpenGeneric_ReportsLL0003()
    {
        var config = CreateConfig(typeof(Player).Assembly.Location);
        config.CSharpCallLua = ["System.Action<>"];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Contains(result.Diagnostics, static d => d.Code == "LL0003");
    }

    [Fact]
    public void Generate_IsDeterministic()
    {
        var config = CreateConfig(typeof(Player).Assembly.Location);

        var first = CodeGenerator.Generate(config, AppContext.BaseDirectory);
        var second = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Equal(
            first.Files.Select(static f => f.Content),
            second.Files.Select(static f => f.Content));
    }

    [Fact]
    public void Generate_MissingType_ReportsLL0001()
    {
        var config = CreateConfig(typeof(Player).Assembly.Location);
        config.LuaCallCSharp = ["LuaLoom.UnitTests.Sample.Missing"];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Contains(result.Diagnostics, static d => d.Code == "LL0001");
    }

    [Fact]
    public void GeneratedCode_Compiles()
    {
        var result = CodeGenerator.Generate(CreateConfig(typeof(Player).Assembly.Location), AppContext.BaseDirectory);
        Assert.Empty(result.Diagnostics);

        var compilation = CreateCompilation(result, "LuaLoom.Generated.Tests");
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);

        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
    }

    [Fact]
    public void GeneratedBindings_WorkUnderStrictReflection()
    {
        var result = CodeGenerator.Generate(CreateConfig(typeof(Player).Assembly.Location), AppContext.BaseDirectory);
        Assert.Empty(result.Diagnostics);

        var compilation = CreateCompilation(result, "LuaLoom.Generated.Strict.Tests");
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));

        stream.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
        var register = assembly
            .GetType("LuaLoom.Generated.LuaBindings", throwOnError: true)!
            .GetMethod("Register", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("LuaBindings.Register was not generated.");

        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });
        register.Invoke(null, [env]);

        var results = env.DoString("""
            local p = CS.LuaLoom.UnitTests.Sample.Player(3)
            p.Hp = 4
            p.Name = 'strict'
            local c = CS.LuaLoom.UnitTests.Sample.ComplexOverloads()
            local outValue, outExtra = c:OutGroup(2)
            local f = CS.LuaLoom.UnitTests.Sample.DelegateFactory.Make(3)
            local generic = { Echo = function(self, value) return value + 1 end }
            local events = { add_Changed = function(self, handler) self.seen = handler(7) end }
            CS.LuaLoom.UnitTests.Sample.InteropHost.Subscribe(events, function(value) return value * 2 end)
            local players = { CS.LuaLoom.UnitTests.Sample.Player(2), CS.LuaLoom.UnitTests.Sample.Player(3) }
            local made = CS.LuaLoom.UnitTests.Sample.ArrayMethods.MakePlayers(5)
            local describe = lua_loom_generic(
                'LuaLoom.UnitTests.Sample.Generics', 'Describe', 'System.Int32')
            local matrix = { { 1, 2 }, { 3, 4 } }
            local madeMatrix = CS.LuaLoom.UnitTests.Sample.ArrayMethods.MakeMatrix(2)
            local f2 = CS.LuaLoom.UnitTests.Sample.DelegateFactory.Make(2)
            local f3 = CS.LuaLoom.UnitTests.Sample.DelegateFactory.Make(3)
            local combined = f2 + f3
            local removed = combined - f3
            return p.Hp, p.Name, p:Add(2, 3), p:Add(5),
                   CS.LuaLoom.UnitTests.Sample.Player.Double(6),
                   c:Combo(1), c:Combo(1, 2), c:Combo(1, 'x'),
                   outValue, outExtra, c:RefGroup(2, 3),
                   f(4), CS.LuaLoom.UnitTests.Sample.InteropHost.Use(generic), events.seen,
                   CS.LuaLoom.UnitTests.Sample.ArrayMethods.SumPlayers(players),
                   #made, made[2].Hp, describe(5),
                   CS.LuaLoom.UnitTests.Sample.ArrayMethods.SumMatrix(matrix),
                   madeMatrix[2][2], combined(4), removed(4)
            """);

        Assert.Equal(4L, results[0]);
        Assert.Equal("strict", results[1]);
        Assert.Equal(5L, results[2]);
        Assert.Equal(9L, results[3]);
        Assert.Equal(12L, results[4]);
        Assert.Equal("int", results[5]);
        Assert.Equal("optional", results[6]);
        Assert.Equal("params", results[7]);
        Assert.Equal(3L, results[8]);
        Assert.Equal(3L, results[9]);
        Assert.Equal(5L, results[10]);
        Assert.Equal(12L, results[11]);
        Assert.Equal(6L, results[12]);
        Assert.Equal(14L, results[13]);
        Assert.Equal(5L, results[14]);
        Assert.Equal(2L, results[15]);
        Assert.Equal(6L, results[16]);
        Assert.Equal("Int32:5", results[17]);
        Assert.Equal(10L, results[18]);
        Assert.Equal(5L, results[19]);
        Assert.Equal(12L, results[20]);
        Assert.Equal(8L, results[21]);

        using var castTable = (LuaTable)env.DoString("return { Hp = 8, Name = 'cast' }")[0]!;
        var castPlayer = castTable.Cast<Player>();
        Assert.Equal(8, castPlayer.Hp);
        Assert.Equal("cast", castPlayer.Name);
    }

    [Fact]
    public void Generate_OpenGeneric_ReportsLL0003()
    {
        var config = CreateConfig(typeof(Player).Assembly.Location);
        config.LuaCallCSharp = ["LuaLoom.UnitTests.Sample.Box`1"];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Contains(result.Diagnostics, static d => d.Code == "LL0003");
    }

    [Fact]
    public void Generate_DuplicateType_ReportsLL0004()
    {
        var config = CreateConfig(typeof(Player).Assembly.Location);
        config.LuaCallCSharp = ["LuaLoom.UnitTests.Sample.Player", "LuaLoom.UnitTests.Sample.Player"];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Contains(result.Diagnostics, static d => d.Code == "LL0004");
    }

    [Fact]
    public void GeneratedBinding_TakesPrecedenceOverReflection()
    {
        using var env = new LuaEnv();

        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Player),
            FullName = "LuaLoom.UnitTests.Sample.Player",
            StaticIndex = StaticCountNinetyNine,
        });

        var results = env.DoString("return CS.LuaLoom.UnitTests.Sample.Player.Count");

        Assert.Equal(99L, results[0]);
    }

    [Fact]
    public void BindingPath_MatchesReflectionPath()
    {
        const string script = "local p = CS.LuaLoom.UnitTests.Sample.Player(1); return p.Hp";

        using var reflection = new LuaEnv();
        var expected = reflection.DoString(script)[0];

        using var bound = new LuaEnv();
        bound.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Player),
            FullName = "LuaLoom.UnitTests.Sample.Player",
            Call = NewPlayerHandler,
            InstanceIndex = PlayerIndexHandler,
        });
        var actual = bound.DoString(script)[0];

        Assert.Equal(1L, expected);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Binding_UnhandledMembersFallBackToReflection()
    {
        using var env = new LuaEnv();
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Player),
            FullName = "LuaLoom.UnitTests.Sample.Player",
            Call = NewPlayerHandler,
            InstanceIndex = PlayerIndexHandler,
        });

        var results = env.DoString("""
            local p = CS.LuaLoom.UnitTests.Sample.Player(2)
            p.Name = 'fallback'
            CS.LuaLoom.UnitTests.Sample.Player.Count = 7
            return p.Hp, p.Name, p:Add(2, 3),
                   CS.LuaLoom.UnitTests.Sample.Player.Double(4),
                   CS.LuaLoom.UnitTests.Sample.Player.Count
            """);

        Assert.Equal(2L, results[0]);
        Assert.Equal("fallback", results[1]);
        Assert.Equal(5L, results[2]);
        Assert.Equal(8L, results[3]);
        Assert.Equal(7L, results[4]);
    }

    [Fact]
    public void Binding_UnhandledEventFallsBackToReflection()
    {
        using var env = new LuaEnv();
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Emitter),
            FullName = "LuaLoom.UnitTests.Sample.Emitter",
            InstanceIndex = EmptyIndexHandler,
        });

        var results = env.DoString("""
            local emitter = CS.LuaLoom.UnitTests.Sample.Emitter()
            local seen = 0
            emitter:Changed('+', function(value) seen = value end)
            emitter:Raise(5)
            return seen
            """);

        Assert.Equal(5L, results[0]);
    }

    [Fact]
    public void StrictReflection_BindingMissThrows()
    {
        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Player),
            FullName = "LuaLoom.UnitTests.Sample.Player",
            Call = NewPlayerHandler,
            InstanceIndex = PlayerIndexHandler,
        });

        var exception = Assert.Throws<LuaException>(
            () => env.DoString("local p = CS.LuaLoom.UnitTests.Sample.Player(1); return p.Name"));

        Assert.Contains("Strict reflection mode blocked", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Player.Name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StrictReflection_AllowsGeneratedPath()
    {
        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Player),
            FullName = "LuaLoom.UnitTests.Sample.Player",
            Call = NewPlayerHandler,
            InstanceIndex = PlayerIndexHandler,
            StaticIndex = StaticCountNinetyNine,
        });

        var results = env.DoString("""
            local p = CS.LuaLoom.UnitTests.Sample.Player(3)
            return p.Hp, CS.LuaLoom.UnitTests.Sample.Player.Count
            """);

        Assert.Equal(3L, results[0]);
        Assert.Equal(99L, results[1]);
    }

    [Fact]
    public void StrictReflection_ReflectionOperatorThrows()
    {
        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Player),
            FullName = "LuaLoom.UnitTests.Sample.Player",
            Call = NewPlayerHandler,
        });

        var exception = Assert.Throws<LuaException>(
            () => env.DoString("local p = CS.LuaLoom.UnitTests.Sample.Player(1); return p + p"));

        Assert.Contains("Strict reflection mode blocked", exception.Message, StringComparison.Ordinal);
        Assert.Contains("op_Addition", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StrictReflection_GenericInvokeThrows()
    {
        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });

        var exception = Assert.Throws<LuaException>(
            () => env.DoString(
                "return lua_loom_generic('LuaLoom.UnitTests.Sample.Player', 'Missing', 'System.Int32')()"));

        Assert.Contains("generic method", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Binding_HandledNilDoesNotFallBack()
    {
        using var env = new LuaEnv();
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Player),
            FullName = "LuaLoom.UnitTests.Sample.Player",
            Call = NewPlayerHandler,
            InstanceIndex = NilNameHandler,
        });

        var results = env.DoString("""
            local p = CS.LuaLoom.UnitTests.Sample.Player(1)
            p.Name = 'reflection'
            return p.Name
            """);

        Assert.Single(results);
        Assert.Null(results[0]);
    }

    private static CSharpCompilation CreateCompilation(GenerationResult result, string assemblyName)
    {
        var references = new List<MetadataReference>();
        foreach (var file in Directory.GetFiles(
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
            "*.dll"))
        {
            try
            {
                _ = AssemblyName.GetAssemblyName(file);
                references.Add(MetadataReference.CreateFromFile(file));
            }
            catch (BadImageFormatException)
            {
                // 原生库没有托管元数据，跳过
            }
        }

        references.Add(MetadataReference.CreateFromFile(typeof(Player).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(LuaEnv).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(LuaCSFunction).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName,
            result.Files.Where(static file => file.IsCode)
                .Select(static file => CSharpSyntaxTree.ParseText(file.Content)),
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static readonly LuaCSFunction EmptyIndexHandler = EmptyIndex;
    private static readonly LuaCSFunction NilNameHandler = NilName;

    private static int EmptyIndex(IntPtr state) => 0;

    private static int NilName(IntPtr state)
    {
        if (LuaBinding.ReadString(state, 2) == "Name")
        {
            LuaBinding.PushNil(state);
            return 1;
        }

        return 0;
    }

    private static readonly LuaCSFunction NewPlayerHandler = NewPlayer;
    private static readonly LuaCSFunction PlayerIndexHandler = PlayerIndex;

    private static int NewPlayer(IntPtr state)
    {
        var hp = LuaBinding.ReadInt(state, 2);
        LuaBinding.PushObject(state, new Player(hp));
        return 1;
    }

    private static int PlayerIndex(IntPtr state)
    {
        var instance = (Player)LuaBinding.ReadObject(state, 1)!;
        var key = LuaBinding.ReadString(state, 2);
        if (key == "Hp")
        {
            LuaBinding.PushInt(state, instance.Hp);
            return 1;
        }

        return 0;
    }

    private static readonly LuaCSFunction StaticCountNinetyNine = StaticIndexHandler;

    private static int StaticIndexHandler(IntPtr state)
    {
        var key = LuaBinding.ReadString(state, 2);
        if (key == "Count")
        {
            LuaBinding.PushInt(state, 99);
            return 1;
        }

        LuaBinding.PushNil(state);
        return 1;
    }
}
