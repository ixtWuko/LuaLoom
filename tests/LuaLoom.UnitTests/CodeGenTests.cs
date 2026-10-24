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
        ],
        CSharpCallLua =
        [
            "LuaLoom.UnitTests.Sample.IntOp",
            "LuaLoom.UnitTests.Sample.ICalculator",
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
        Assert.Contains("LuaLoom_UnitTests_Sample_IntOpInvoker.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_ICalculatorImpl.g.cs", names);
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

        var generic = result.Files.Single(static f => f.FileName == "System_Action_int_Invoker.g.cs");
        var bindings = result.Files.Single(static f => f.FileName == "LuaBindings.g.cs");
        Assert.Contains("new global::System.Action<int>", generic.Content, StringComparison.Ordinal);
        Assert.Contains("typeof(global::System.Action<int>)", bindings.Content, StringComparison.Ordinal);
        Assert.Contains(
            "RegisterExtensionType(typeof(global::LuaLoom.UnitTests.Sample.PlayerExtensions))",
            bindings.Content,
            StringComparison.Ordinal);

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

        var references = new List<MetadataReference>();
        foreach (var file in Directory.GetFiles(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
        {
            try
            {
                _ = System.Reflection.AssemblyName.GetAssemblyName(file);
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

        var compilation = CSharpCompilation.Create(
            "LuaLoom.Generated.Tests",
            result.Files.Select(static file => CSharpSyntaxTree.ParseText(file.Content)),
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);

        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
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
