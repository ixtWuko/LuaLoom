using LuaLoom.CodeGen;
using LuaLoom.UnitTests.Sample;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class CodeGenConfigTests
{
    private static GeneratorConfig CreateConfig() => new()
    {
        Assembly = typeof(Player).Assembly.Location,
        Output = "generated",
        Namespace = "LuaLoom.Generated",
    };

    private static string TypeName(Type type) => type.FullName!;

    [Fact]
    public void Coverage_ReportsObsoleteMembersAsReflection()
    {
        var config = CreateConfig();
        config.LuaCallCSharp = [TypeName(typeof(Overloads))];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Empty(result.Diagnostics);
        var legacy = result.Coverage.Single(static entry => entry.Member == "Legacy()");
        Assert.Equal("reflection", legacy.Status);
        Assert.Equal("obsolete", legacy.Reason);

        var optional = result.Coverage.Single(static entry => entry.Member == "Optional(Int32)");
        Assert.Equal("generated", optional.Status);
        Assert.Equal(string.Empty, optional.Reason);

        var binder = result.Files.Single(
            static file => file.FileName == "LuaLoom_UnitTests_Sample_OverloadsBinder.g.cs");
        Assert.DoesNotContain("Legacy", binder.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Coverage_ReportsUnsupportedMembersAndIndexerShape()
    {
        var config = CreateConfig();
        config.LuaCallCSharp =
        [
            TypeName(typeof(UnsupportedSurface)),
            TypeName(typeof(Bag)),
            TypeName(typeof(CastProbe)),
            TypeName(typeof(UnsupportedFieldSurface)),
        ];
        config.CSharpCallLua = [TypeName(typeof(IUnsupportedShape))];
        config.BlackList = [$"{TypeName(typeof(Bag))}.this[]"];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Empty(result.Diagnostics);

        var generic = result.Coverage.Single(entry =>
            entry.TypeName == TypeName(typeof(UnsupportedSurface))
            && entry.Member.StartsWith("Generic", StringComparison.Ordinal));
        Assert.Equal("reflection", generic.Status);
        Assert.False(string.IsNullOrWhiteSpace(generic.Reason));

        var unsupported = result.Coverage.Single(entry =>
            entry.TypeName == TypeName(typeof(UnsupportedSurface))
            && entry.Member.StartsWith("Unsupported(", StringComparison.Ordinal));
        Assert.Equal("reflection", unsupported.Status);
        Assert.Contains("unsupported parameter", unsupported.Reason, StringComparison.Ordinal);

        var unsupportedProperty = result.Coverage.Single(entry =>
            entry.TypeName == TypeName(typeof(CastProbe)) && entry.Member == "Items");
        Assert.Equal("reflection", unsupportedProperty.Status);
        Assert.Contains("unsupported property", unsupportedProperty.Reason, StringComparison.Ordinal);

        var indexer = result.Coverage.Single(entry =>
            entry.TypeName == TypeName(typeof(Bag)) && entry.Member == "this[]");
        Assert.Equal("skipped", indexer.Status);
        Assert.False(string.IsNullOrWhiteSpace(indexer.Reason));

        var unsupportedField = result.Coverage.Single(entry =>
            entry.TypeName == TypeName(typeof(UnsupportedFieldSurface)) && entry.Member == "Items");
        Assert.Equal("reflection", unsupportedField.Status);
        Assert.Contains("unsupported field", unsupportedField.Reason, StringComparison.Ordinal);

        var interfaceShape = result.Coverage.Single(entry =>
            entry.TypeName == TypeName(typeof(IUnsupportedShape)) && entry.Member == "<interface>");
        Assert.Equal("skipped", interfaceShape.Status);
        Assert.False(string.IsNullOrWhiteSpace(interfaceShape.Reason));

        var bagBinder = result.Files.Single(
            static file => file.FileName == "LuaLoom_UnitTests_Sample_BagBinder.g.cs");
        Assert.DoesNotContain("InstanceIndexer", bagBinder.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_DoNotGenMemberSkipsOnlyThatMember()
    {
        var config = CreateConfig();
        config.LuaCallCSharp = [TypeName(typeof(Player))];
        config.DoNotGen = [$"{TypeName(typeof(Player))}.Add"];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Empty(result.Diagnostics);
        var binder = result.Files.Single(
            static file => file.FileName == "LuaLoom_UnitTests_Sample_PlayerBinder.g.cs");
        Assert.DoesNotContain("case \"Add\":", binder.Content, StringComparison.Ordinal);
        Assert.Contains("case \"Hp\":", binder.Content, StringComparison.Ordinal);
        Assert.Contains("case \"Name\":", binder.Content, StringComparison.Ordinal);
        Assert.Contains("case \"Double\":", binder.Content, StringComparison.Ordinal);

        var skipped = result.Coverage
            .Where(static entry => entry.Member.StartsWith("Add(", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(skipped);
        Assert.All(skipped, entry =>
        {
            Assert.Equal(TypeName(typeof(Player)), entry.TypeName);
            Assert.Equal("skipped", entry.Status);
            Assert.Equal("do-not-gen member", entry.Reason);
        });

        var hp = result.Coverage.Single(static entry => entry.Member == "Hp");
        Assert.Equal("generated", hp.Status);
        Assert.Equal(string.Empty, hp.Reason);
    }

    [Fact]
    public void Generate_BlackListedInterfaceSkipsImplementation()
    {
        var config = CreateConfig();
        config.CSharpCallLua = [TypeName(typeof(IGenericCalculator))];
        config.BlackList = [TypeName(typeof(IGenericCalculator))];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(
            result.Files,
            static file => file.FileName == "LuaLoom_UnitTests_Sample_IGenericCalculatorImpl.g.cs");

        var entry = result.Coverage.Single(entry => entry.TypeName == TypeName(typeof(IGenericCalculator)));
        Assert.Equal("<type>", entry.Member);
        Assert.Equal("skipped", entry.Status);
        Assert.Equal("blacklisted type", entry.Reason);

        var bindings = result.Files.Single(static file => file.FileName == "LuaBindings.g.cs");
        Assert.DoesNotContain("IGenericCalculator", bindings.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_BlackListedDelegateSkipsInvoker()
    {
        var config = CreateConfig();
        config.CSharpCallLua = [TypeName(typeof(IntOp))];
        config.BlackList = [TypeName(typeof(IntOp))];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(
            result.Files,
            static file => file.FileName == "LuaLoom_UnitTests_Sample_IntOpInvoker.g.cs");

        var entry = result.Coverage.Single(entry => entry.TypeName == TypeName(typeof(IntOp)));
        Assert.Equal("<type>", entry.Member);
        Assert.Equal("skipped", entry.Status);
        Assert.Equal("blacklisted type", entry.Reason);

        var bindings = result.Files.Single(static file => file.FileName == "LuaBindings.g.cs");
        Assert.DoesNotContain("IntOp", bindings.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ReflectionUseAddsLinkXmlWithoutBinder()
    {
        var config = CreateConfig();
        config.ReflectionUse = [TypeName(typeof(Player))];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(
            result.Files,
            static file => file.FileName == "LuaLoom_UnitTests_Sample_PlayerBinder.g.cs");

        var link = result.Files.Single(static file => file.FileName == "link.xml");
        Assert.Contains(TypeName(typeof(Player)), link.Content, StringComparison.Ordinal);
        Assert.Contains("preserve=\"all\"", link.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ReflectionUseUnknownType_ReportsLL0001()
    {
        const string missing = "LuaLoom.UnitTests.Sample.MissingReflectionType";
        var config = CreateConfig();
        config.ReflectionUse = [missing];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == "LL0001"
                && diagnostic.Message.Contains(missing, StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_GCOptimizeValueType_AddsLinkXml()
    {
        var config = CreateConfig();
        config.GCOptimize = [TypeName(typeof(Vec2))];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(
            result.Files,
            static file => file.FileName == "LuaLoom_UnitTests_Sample_Vec2Binder.g.cs");

        var link = result.Files.Single(static file => file.FileName == "link.xml");
        Assert.Contains(TypeName(typeof(Vec2)), link.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_GCOptimizeReferenceType_ReportsLL0002()
    {
        var config = CreateConfig();
        config.GCOptimize = [TypeName(typeof(Player))];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == "LL0002"
                && diagnostic.Message.Contains("must be a value type", StringComparison.Ordinal));
        var link = result.Files.Single(static file => file.FileName == "link.xml");
        Assert.DoesNotContain(TypeName(typeof(Player)), link.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_GCOptimizeUnknownType_ReportsLL0001()
    {
        const string missing = "LuaLoom.UnitTests.Sample.MissingOptimizedType";
        var config = CreateConfig();
        config.GCOptimize = [missing];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == "LL0001"
                && diagnostic.Message.Contains(missing, StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_InvalidGenericMethod_ReportsLL0005()
    {
        var config = CreateConfig();
        config.GenericMethods = ["NoSuch.Method"];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == "LL0005"
                && diagnostic.Message.Contains("NoSuch.Method", StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.Files,
            static file => file.FileName.EndsWith("GenericBinder.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_ClosedGenericMethods_EmitBindersAndRegistrations()
    {
        var config = CreateConfig();
        config.GenericMethods =
        [
            $"{TypeName(typeof(Generics))}.Describe<int>",
            $"{TypeName(typeof(AdvancedGenerics))}.Pair<int,string>",
        ];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Empty(result.Diagnostics);

        var describeBinder = result.Files.Single(file =>
            file.FileName.StartsWith("LuaLoom_UnitTests_Sample_Generics_Describe_", StringComparison.Ordinal)
            && file.FileName.EndsWith("GenericBinder.g.cs", StringComparison.Ordinal));
        Assert.Contains("Handler", describeBinder.Content, StringComparison.Ordinal);

        var pairBinder = result.Files.Single(file =>
            file.FileName.StartsWith("LuaLoom_UnitTests_Sample_AdvancedGenerics_Pair_", StringComparison.Ordinal)
            && file.FileName.EndsWith("GenericBinder.g.cs", StringComparison.Ordinal));
        Assert.Contains("Handler", pairBinder.Content, StringComparison.Ordinal);

        var bindings = result.Files.Single(static file => file.FileName == "LuaBindings.g.cs");
        Assert.Contains("RegisterGenericMethod", bindings.Content, StringComparison.Ordinal);
        Assert.Contains(
            $"typeof(global::{TypeName(typeof(Generics))}), \"Describe\"",
            bindings.Content,
            StringComparison.Ordinal);
        Assert.Contains(
            $"typeof(global::{TypeName(typeof(AdvancedGenerics))}), \"Pair\"",
            bindings.Content,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Coverage_MixedTypes_ContainsGeneratedAndReflectionEntries()
    {
        var config = CreateConfig();
        config.LuaCallCSharp = [TypeName(typeof(Player)), TypeName(typeof(UnsupportedSurface))];

        var result = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Empty(result.Diagnostics);
        Assert.Contains(
            result.Coverage,
            entry => entry.TypeName == TypeName(typeof(Player)) && entry.Status == "generated");
        Assert.Contains(
            result.Coverage,
            entry => entry.TypeName == TypeName(typeof(UnsupportedSurface)) && entry.Status == "reflection");

        var generated = result.Coverage.Count(static entry => entry.Status == "generated");
        var reflection = result.Coverage.Count(static entry => entry.Status == "reflection");
        var skipped = result.Coverage.Count(static entry => entry.Status == "skipped");

        Assert.True(generated > 0);
        Assert.True(reflection > 0);
        Assert.Equal(result.Coverage.Count, generated + reflection + skipped);
        Assert.Equal(
            result.Coverage.Count,
            result.Coverage
                .Select(static entry => entry.TypeName + "::" + entry.Member)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(
            result.Coverage,
            static entry => Assert.Contains(entry.Status, new[] { "generated", "reflection", "skipped" }));
    }

    [Fact]
    public void Generate_WithDenyListsAndGenericMethods_IsDeterministic()
    {
        var config = CreateConfig();
        config.LuaCallCSharp = [TypeName(typeof(Player)), TypeName(typeof(Overloads))];
        config.CSharpCallLua = [TypeName(typeof(IntOp)), TypeName(typeof(IGenericCalculator))];
        config.GenericMethods =
        [
            $"{TypeName(typeof(Generics))}.Describe<int>",
            $"{TypeName(typeof(AdvancedGenerics))}.Pair<int,string>",
        ];
        config.BlackList = [$"{TypeName(typeof(Overloads))}.Legacy"];
        config.DoNotGen = [$"{TypeName(typeof(Player))}.Add"];

        var first = CodeGenerator.Generate(config, AppContext.BaseDirectory);
        var second = CodeGenerator.Generate(config, AppContext.BaseDirectory);

        Assert.Equal(first.Files.Select(static file => file.FileName), second.Files.Select(static file => file.FileName));
        Assert.Equal(first.Files.Select(static file => file.Content), second.Files.Select(static file => file.Content));
        Assert.Equal(
            first.Diagnostics.Select(static diagnostic => diagnostic.Code + ":" + diagnostic.Message),
            second.Diagnostics.Select(static diagnostic => diagnostic.Code + ":" + diagnostic.Message));
    }
}

public sealed class UnsupportedFieldSurface
{
    public List<int> Items = [];

    public int Value;
}

public interface IUnsupportedShape
{
    List<int> Values { get; }
}
