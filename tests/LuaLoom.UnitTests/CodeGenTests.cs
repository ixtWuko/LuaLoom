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
            "LuaLoom.UnitTests.Sample.CastProbe",
            "LuaLoom.UnitTests.Sample.NoDefaultCtor",
            "LuaLoom.UnitTests.Sample.AdvancedGenerics",
            "LuaLoom.UnitTests.Sample.GenericInstance",
            "LuaLoom.UnitTests.Sample.ArraySurface",
            "LuaLoom.UnitTests.Sample.MathValue",
            "LuaLoom.UnitTests.Sample.Vec2",
            "LuaLoom.UnitTests.Sample.NumericSurface",
            "LuaLoom.UnitTests.Sample.DefaultSurface",
            "LuaLoom.UnitTests.Sample.AmbiguousSurface",
            "LuaLoom.UnitTests.Sample.PartialSurface",
            "LuaLoom.UnitTests.Sample.KeywordSurface",
        ],
        GenericMethods =
        [
            "LuaLoom.UnitTests.Sample.Generics.Describe<int>",
            "LuaLoom.UnitTests.Sample.AdvancedGenerics.Echo<int>",
            "LuaLoom.UnitTests.Sample.AdvancedGenerics.Pair<int,string>",
            "LuaLoom.UnitTests.Sample.AdvancedGenerics.TryGeneric<int>",
            "LuaLoom.UnitTests.Sample.AdvancedGenerics.SumGeneric<int>",
            "LuaLoom.UnitTests.Sample.GenericInstance.Echo<string>",
            "LuaLoom.UnitTests.Sample.GenericInstance.Pair<int,string>",
        ],
        CSharpCallLua =
        [
            "LuaLoom.UnitTests.Sample.IntOp",
            "LuaLoom.UnitTests.Sample.ICalculator",
            "LuaLoom.UnitTests.Sample.IArrayCalculator",
            "LuaLoom.UnitTests.Sample.IGenericCalculator",
            "LuaLoom.UnitTests.Sample.IAdvancedCalculator",
            "LuaLoom.UnitTests.Sample.IConstrainedCalculator",
            "LuaLoom.UnitTests.Sample.IEventSource",
            "LuaLoom.UnitTests.Sample.IntUnary",
            "LuaLoom.UnitTests.Sample.VoidOp",
            "LuaLoom.UnitTests.Sample.ParseOp",
            "LuaLoom.UnitTests.Sample.ParamsOp",
            "LuaLoom.UnitTests.Sample.ArrayOp",
            "System.Action<int>",
            "System.Func<int, string>",
            "LuaLoom.UnitTests.Sample.Mapper<int, string>",
            "LuaLoom.UnitTests.Sample.IDerivedSurface",
            "LuaLoom.UnitTests.Sample.INamingCollision",
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
        Assert.DoesNotContain("LuaLoom_UnitTests_Sample_IConstrainedCalculatorImpl.g.cs", names);
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
        Assert.Contains("add => __RaiseEvent(\"add_Changed\", value)", eventInterface.Content, StringComparison.Ordinal);

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
            local emptyPlayer = CS.LuaLoom.UnitTests.Sample.Player()
            p.Hp = 4
            p.Name = 'strict'
            CS.LuaLoom.UnitTests.Sample.Player.Count = 11
            local playerCount = CS.LuaLoom.UnitTests.Sample.Player.Count
            local c = CS.LuaLoom.UnitTests.Sample.ComplexOverloads()
            local outValue, outExtra = c:OutGroup(2)
            local f = CS.LuaLoom.UnitTests.Sample.DelegateFactory.Make(3)
            local generic = { Echo = function(self, value) return value + 1 end }
            local events = {
                add_Changed = function(self, handler) self.seen = handler(7) end,
                remove_Changed = function(self, handler) self.removed = handler(3) end
            }
            local eventHandler = function(value) return value * 2 end
            CS.LuaLoom.UnitTests.Sample.InteropHost.Subscribe(events, eventHandler)
            CS.LuaLoom.UnitTests.Sample.InteropHost.Unsubscribe(events, eventHandler)
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
            local emitter = CS.LuaLoom.UnitTests.Sample.Emitter()
            local seen = 0
            local onChanged = function(value) seen = value end
            emitter:Changed('+', onChanged)
            emitter:Raise(5)
            emitter:Changed('-', onChanged)
            emitter:Raise(7)
            local bag = CS.LuaLoom.UnitTests.Sample.Bag()
            bag[1] = 5
            local money = CS.LuaLoom.UnitTests.Sample.Money(2) + CS.LuaLoom.UnitTests.Sample.Money(3)
            local multiplied = CS.LuaLoom.UnitTests.Sample.MathValue(6) * CS.LuaLoom.UnitTests.Sample.MathValue(2)
            local divided = CS.LuaLoom.UnitTests.Sample.MathValue(6) / CS.LuaLoom.UnitTests.Sample.MathValue(2)
            local remainder = CS.LuaLoom.UnitTests.Sample.MathValue(6) % CS.LuaLoom.UnitTests.Sample.MathValue(2)
            local lessOrEqual = CS.LuaLoom.UnitTests.Sample.MathValue(2) <= CS.LuaLoom.UnitTests.Sample.MathValue(6)
            local pickString = CS.LuaLoom.UnitTests.Sample.Overloads.Pick('x')
            local pickInteger = CS.LuaLoom.UnitTests.Sample.Overloads.Pick(1)
            local pickNumber = CS.LuaLoom.UnitTests.Sample.Overloads.Pick(1.5)
            local optionalDefault = CS.LuaLoom.UnitTests.Sample.Overloads.Optional()
            local optionalValue = CS.LuaLoom.UnitTests.Sample.Overloads.Optional(4)
            return p.Hp, p.Name, p:Add(2, 3), p:Add(5),
                   CS.LuaLoom.UnitTests.Sample.Player.Double(6),
                   c:Combo(1), c:Combo(1, 2), c:Combo(1, 'x'),
                   outValue, outExtra, c:RefGroup(2, 3),
                   f(4), CS.LuaLoom.UnitTests.Sample.InteropHost.Use(generic), events.seen,
                   CS.LuaLoom.UnitTests.Sample.ArrayMethods.SumPlayers(players),
                   #made, made[2].Hp, describe(5),
                   CS.LuaLoom.UnitTests.Sample.ArrayMethods.SumMatrix(matrix),
                   madeMatrix[2][2], combined(4), removed(4),
                   p:Triple(2), seen, bag[1], money.Amount, events.removed,
                   c:Combo(1, 2, 3),
                   multiplied.Value, divided.Value, remainder.Value, lessOrEqual,
                   pickString, pickInteger, pickNumber, optionalDefault, optionalValue,
                   playerCount, emptyPlayer.Hp
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
        Assert.Equal(8L, results[22]);
        Assert.Equal(5L, results[23]);
        Assert.Equal(5L, results[24]);
        Assert.Equal(5L, results[25]);
        Assert.Equal(6L, results[26]);
        Assert.Equal("params", results[27]);
        Assert.Equal(12L, results[28]);
        Assert.Equal(3L, results[29]);
        Assert.Equal(0L, results[30]);
        Assert.Equal(true, results[31]);
        Assert.Equal("string", results[32]);
        Assert.Equal("int", results[33]);
        Assert.Equal("object", results[34]);
        Assert.Equal(7L, results[35]);
        Assert.Equal(4L, results[36]);
        Assert.Equal(11L, results[37]);
        Assert.Equal(0L, results[38]);

        using var castTable = (LuaTable)env.DoString("return { Hp = 8, Name = 'cast' }")[0]!;
        var castPlayer = castTable.Cast<Player>();
        Assert.Equal(8, castPlayer.Hp);
        Assert.Equal("cast", castPlayer.Name);
    }

    [Fact]
    public void GeneratedBindings_AdvancedFeatures()
    {
        var result = CodeGenerator.Generate(CreateConfig(typeof(Player).Assembly.Location), AppContext.BaseDirectory);
        Assert.Empty(result.Diagnostics);

        var compilation = CreateCompilation(result, "LuaLoom.Generated.Advanced.Tests");
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
            local advanced = {
                Echo = function(self, value) return value end,
                Pair = function(self, left, right) return tostring(left) .. ':' .. tostring(right) end,
                Sum = function(self, values)
                    local total = 0
                    for i = 1, #values do total = total + values[i] end
                    return total
                end,
                TryParse = function(self, text) return true, tonumber(text) end,
                get_Value = function(self) return self.value or 0 end,
                set_Value = function(self, value) self.value = value end
            }
            local advancedText = CS.LuaLoom.UnitTests.Sample.InteropHost.UseAdvanced(advanced)
            local advancedOut = CS.LuaLoom.UnitTests.Sample.InteropHost.UseAdvancedOut(advanced)
            local advancedProperty = CS.LuaLoom.UnitTests.Sample.InteropHost.UseAdvancedProperty(advanced)
            local arrayCalculator = {
                Sum = function(self, values)
                    local total = 0
                    for i = 1, #values do total = total + values[i] end
                    return total
                end,
                Join = function(self, separator, ...)
                    return table.concat({ ... }, separator)
                end,
                TryParse = function(self, text) return true, tonumber(text) end
            }
            local arrayText = CS.LuaLoom.UnitTests.Sample.InteropHost.UseArrayCalculator(arrayCalculator)

            local pair = lua_loom_generic(
                'LuaLoom.UnitTests.Sample.AdvancedGenerics', 'Pair', 'System.Int32', 'System.String')
            local tryGeneric = lua_loom_generic(
                'LuaLoom.UnitTests.Sample.AdvancedGenerics', 'TryGeneric', 'System.Int32')
            local sumGeneric = lua_loom_generic(
                'LuaLoom.UnitTests.Sample.AdvancedGenerics', 'SumGeneric', 'System.Int32')
            local genericInstance = CS.LuaLoom.UnitTests.Sample.GenericInstance()
            local echo = lua_loom_generic(
                CS.LuaLoom.UnitTests.Sample.GenericInstance, 'Echo', 'System.String')
            local pairInstance = lua_loom_generic(
                CS.LuaLoom.UnitTests.Sample.GenericInstance, 'Pair', 'System.Int32', 'System.String')

            local strings = CS.LuaLoom.UnitTests.Sample.ArraySurface.SumLengths({ 'ab', 'c' })
            local players = { CS.LuaLoom.UnitTests.Sample.Player(5), CS.LuaLoom.UnitTests.Sample.Player(6) }
            local echoedPlayers = CS.LuaLoom.UnitTests.Sample.ArraySurface.EchoPlayers(players)
            local colors = CS.LuaLoom.UnitTests.Sample.ArraySurface.EchoColors({ 1, 2 })
            local matrix = CS.LuaLoom.UnitTests.Sample.ArraySurface.EchoMatrix({ { 1, 2 }, { 3, 4 } })
            local cube = CS.LuaLoom.UnitTests.Sample.ArraySurface.EchoCube(
                { { { 1, 2 }, { 3, 4 } }, { { 5, 6 }, { 7, 8 } } })
            local empty = CS.LuaLoom.UnitTests.Sample.ArraySurface.Empty()
            local nullArray = CS.LuaLoom.UnitTests.Sample.ArraySurface.NullArray()
            local nullSum = CS.LuaLoom.UnitTests.Sample.ArraySurface.SumNullable(nil)

            local genericOk, genericNumber = tryGeneric(5)
            local genericSum = sumGeneric(1, 2, 3)
            return advancedText, advancedOut, advancedProperty,
                   pair(1, 'x'), echo(genericInstance, 'hi'),
                   strings, echoedPlayers[2].Hp, colors[1], colors[2],
                   matrix[2][2], cube[2][2][2], #empty, nullArray == nil,
                   genericOk, genericNumber, genericSum,
                   pairInstance(genericInstance, 1, 'x'), nullSum, arrayText
            """);

        Assert.Equal("1:x:6", results[0]);
        Assert.Equal(true, results[1]);
        Assert.Equal(7L, results[2]);
        Assert.Equal("Int32:String:1:x", results[3]);
        Assert.Equal("hi", results[4]);
        Assert.Equal(3L, results[5]);
        Assert.Equal(6L, results[6]);
        Assert.Equal(1L, results[7]);
        Assert.Equal(2L, results[8]);
        Assert.Equal(4L, results[9]);
        Assert.Equal(8L, results[10]);
        Assert.Equal(0L, results[11]);
        Assert.Equal(true, results[12]);
        Assert.Equal(true, results[13]);
        Assert.Equal(6L, results[14]);
        Assert.Equal(6L, results[15]);
        Assert.Equal("Int32:String", results[16]);
        Assert.Equal(-1L, results[17]);
        Assert.Equal("6:1-2-3:True:4", results[18]);
    }

    [Fact]
    public void GeneratedCast_HandlesNestedAndCollectionMembers()
    {
        var result = CodeGenerator.Generate(CreateConfig(typeof(Player).Assembly.Location), AppContext.BaseDirectory);
        Assert.Empty(result.Diagnostics);

        var compilation = CreateCompilation(result, "LuaLoom.Generated.Cast.Tests");
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));

        stream.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
        var register = assembly
            .GetType("LuaLoom.Generated.LuaBindings", throwOnError: true)!
            .GetMethod("Register", BindingFlags.Public | BindingFlags.Static)!;

        using var env = new LuaEnv(new LuaEnvOptions { StrictReflection = true });
        register.Invoke(null, [env]);

        using var table = (LuaTable)env.DoString("""
            return {
                Number = 3,
                Name = 'probe',
                Child = { Hp = 4, Name = 'child' },
                Numbers = { 1, 2, 3 },
                Position = { X = 1.5, Y = 2.5 },
                Items = { 7, 8 }
            }
            """)[0]!;

        var probe = table.Cast<CastProbe>();
        Assert.Equal(3, probe.Number);
        Assert.Equal("probe", probe.Name);
        Assert.Equal(4, probe.Child!.Hp);
        Assert.Equal("child", probe.Child.Name);
        Assert.Equal([1, 2, 3], probe.Numbers);
        Assert.Equal(1.5f, probe.Position.X);
        Assert.Equal(2.5f, probe.Position.Y);
        Assert.Equal([7, 8], probe.Items);
        Assert.Equal(9, probe.ReadOnly);

        using var vecTable = (LuaTable)env.DoString("return { X = 3.5, Y = 4.5 }")[0]!;
        var vec = vecTable.Cast<Vec2>();
        Assert.Equal(3.5f, vec.X);
        Assert.Equal(4.5f, vec.Y);

        using var noDefaultTable = (LuaTable)env.DoString("return { Value = 1 }")[0]!;
        Assert.Throws<MissingMethodException>(() => noDefaultTable.Cast<NoDefaultCtor>());
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
    public void ReflectionFallbackDisabled_RegisteredBindingMissStillFallsBack()
    {
        using var env = new LuaEnv(new LuaEnvOptions { EnableReflectionFallback = false });
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Player),
            FullName = "LuaLoom.UnitTests.Sample.Player",
            Call = NewPlayerHandler,
            InstanceIndex = EmptyIndexHandler,
        });

        var results = env.DoString("""
            local p = CS.LuaLoom.UnitTests.Sample.Player(2)
            p.Name = 'fallback'
            return p.Name
            """);

        Assert.Equal("fallback", results[0]);
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

    [Fact]
    public void Generated_ThunkExceptionBecomesLuaError()
    {
        var result = CodeGenerator.Generate(CreateConfig(typeof(Player).Assembly.Location), AppContext.BaseDirectory);
        Assert.Empty(result.Diagnostics);

        var compilation = CreateCompilation(result, "LuaLoom.Generated.Thunk.Tests");
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

        // 参数类型不符与参数个数不符都必须变成脚本可捕获的 Lua 错误
        var results = env.DoString("""
            local okType, errType = pcall(function() return CS.LuaLoom.UnitTests.Sample.Player.Double('x') end)
            local okArity, errArity = pcall(function() return CS.LuaLoom.UnitTests.Sample.Player.Double() end)
            return okType, tostring(errType), okArity, tostring(errArity)
            """);

        Assert.Equal(false, results[0]);
        Assert.Contains("Cannot convert", (string)results[1]!, StringComparison.Ordinal);
        Assert.Equal(false, results[2]);
        Assert.Contains("No matching overload", (string)results[3]!, StringComparison.Ordinal);
        Assert.Equal(2L, env.DoString("return 1 + 1")[0]);
    }

    [Fact]
    public void Generated_HandlesInheritedInterfacesPrimitiveMembersAndDefaults()
    {
        var result = CodeGenerator.Generate(CreateConfig(typeof(Player).Assembly.Location), AppContext.BaseDirectory);
        Assert.Empty(result.Diagnostics);

        var compilation = CreateCompilation(result, "LuaLoom.Generated.Surface.Tests");
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
            local numeric = CS.LuaLoom.UnitTests.Sample.NumericSurface()
            numeric.ByteValue = 7
            numeric.SByteValue = -3
            numeric.ShortValue = 300
            numeric.UShortValue = 60000
            numeric.UIntValue = 4000000000
            numeric.ULongValue = 7
            numeric.Letter = 'A'
            local keyword = CS.LuaLoom.UnitTests.Sample.KeywordSurface()
            keyword.class = 5
            keyword.value = 6
            local baseValue = keyword:base(7)
            local emitter = CS.LuaLoom.UnitTests.Sample.Emitter()
            local emitCount = 0
            local emitFn = function(value) emitCount = emitCount + 1 end
            emitter:Changed('+', emitFn)
            emitter:Changed('+', emitFn)
            emitter:Raise(1)
            return numeric.ByteValue, numeric.SByteValue, numeric.ShortValue, numeric.UShortValue,
                   numeric.UIntValue, numeric.ULongValue, numeric.Letter,
                   CS.LuaLoom.UnitTests.Sample.DefaultSurface.WithNewline(),
                   CS.LuaLoom.UnitTests.Sample.DefaultSurface.WithBig(),
                   CS.LuaLoom.UnitTests.Sample.DefaultSurface.WithNaN(),
                   CS.LuaLoom.UnitTests.Sample.Overloads.Pick(1),
                   CS.LuaLoom.UnitTests.Sample.Overloads.Pick(4000000000),
                   CS.LuaLoom.UnitTests.Sample.PartialSurface.Stable(5),
                   keyword.class, keyword.value, baseValue, emitCount
            """);

        Assert.Equal(7L, results[0]);
        Assert.Equal(-3L, results[1]);
        Assert.Equal(300L, results[2]);
        Assert.Equal(60000L, results[3]);
        Assert.Equal(4000000000L, results[4]);
        Assert.Equal(7L, results[5]);
        Assert.Equal("A", results[6]);
        Assert.Equal("line1\nline2", results[7]);
        Assert.Equal(-1L, results[8]);
        Assert.True(double.IsNaN((double)results[9]!));
        Assert.Equal("int", results[10]);
        Assert.Equal("long", results[11]);
        Assert.Equal(5L, results[12]);
        Assert.Equal(5L, results[13]);
        Assert.Equal(6L, results[14]);
        Assert.Equal(8L, results[15]);
        Assert.Equal(1L, results[16]);

        using var collisionTable = (LuaTable)env.DoString("""
            local impl = {}
            function impl:Calc(a, b, c, d) return a + b + c + d end
            return impl
            """)[0]!;

        var collision = collisionTable.Cast<INamingCollision>();
        Assert.Equal(10, collision.Calc(1, 2, 3, 4));

        using var table = (LuaTable)env.DoString("""
            local impl = { value = 0 }
            function impl:Increment(value) return value + 1 end
            function impl:BaseValue() return 9 end
            return impl
            """)[0]!;

        var surface = table.Cast<IDerivedSurface>();
        Assert.Equal(5, surface.Increment(4));
        Assert.Equal(9, surface.BaseValue());
    }

    [Fact]
    public void Generate_SkipsOverloadGroupsItCannotDispatch()
    {
        var result = CodeGenerator.Generate(CreateConfig(typeof(Player).Assembly.Location), AppContext.BaseDirectory);
        Assert.Empty(result.Diagnostics);

        var binder = Assert.Single(
            result.Files,
            static file => file.FileName.Contains("AmbiguousSurface", StringComparison.Ordinal));

        Assert.DoesNotContain("Use", binder.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_SkipsUnsupportedOverloadsIndividually()
    {
        var result = CodeGenerator.Generate(CreateConfig(typeof(Player).Assembly.Location), AppContext.BaseDirectory);
        Assert.Empty(result.Diagnostics);

        var binder = Assert.Single(
            result.Files,
            static file => file.FileName.Contains("PartialSurface", StringComparison.Ordinal));

        Assert.Contains("Stable", binder.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("RefReturn", binder.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("List", binder.Content, StringComparison.Ordinal);
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
