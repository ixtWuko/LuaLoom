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
        LuaCallCSharp = ["LuaLoom.UnitTests.Sample.Player"],
        CSharpCallLua = ["LuaLoom.UnitTests.Sample.IntOp", "LuaLoom.UnitTests.Sample.ICalculator"],
    };

    [Fact]
    public void Generate_ProducesExpectedFiles()
    {
        var result = CodeGenerator.Generate(CreateConfig(typeof(Player).Assembly.Location), AppContext.BaseDirectory);

        Assert.Empty(result.Diagnostics);
        var names = result.Files.Select(static f => f.FileName).OrderBy(static n => n, StringComparer.Ordinal).ToArray();
        Assert.Contains("LuaLoom_UnitTests_Sample_PlayerBinder.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_IntOpInvoker.g.cs", names);
        Assert.Contains("LuaLoom_UnitTests_Sample_ICalculatorImpl.g.cs", names);
        Assert.Contains("LuaBindings.g.cs", names);
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

        LuaBinding.PushNil(state);
        return 1;
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
