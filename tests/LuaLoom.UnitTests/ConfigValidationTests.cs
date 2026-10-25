using LuaLoom.CodeGen;
using Xunit;

namespace LuaLoom.UnitTests;

/// <summary>配置文件的字段校验与未知字段检测。</summary>
public sealed class ConfigValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "lualoom-config-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ValidConfig_Loads()
    {
        var path = Write("""
            {
              "assembly": "Sample.dll",
              "output": "generated",
              "namespace": "Sample.Generated",
              "luaCallCSharp": ["Sample.Player"]
            }
            """);

        var config = CodeGenerator.LoadConfig(path);

        Assert.Equal("Sample.Generated", config.Namespace);
        Assert.Equal(new[] { "Sample.Player" }, config.LuaCallCSharp);
    }

    [Fact]
    public void UnknownKey_Throws()
    {
        var path = Write("""
            {
              "assembly": "Sample.dll",
              "output": "generated",
              "namespace": "Sample.Generated",
              "luaCallSharp": ["Sample.Player"]
            }
            """);

        var exception = Assert.Throws<CodeGenException>(() => CodeGenerator.LoadConfig(path));
        Assert.Contains("luaCallSharp", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyNamespace_Throws()
    {
        var path = Write("""
            {
              "assembly": "Sample.dll",
              "output": "generated",
              "namespace": ""
            }
            """);

        var exception = Assert.Throws<CodeGenException>(() => CodeGenerator.LoadConfig(path));
        Assert.Contains("namespace", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidNamespace_Throws()
    {
        var path = Write("""
            {
              "assembly": "Sample.dll",
              "output": "generated",
              "namespace": "123.Invalid"
            }
            """);

        var exception = Assert.Throws<CodeGenException>(() => CodeGenerator.LoadConfig(path));
        Assert.Contains("namespace", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingOutput_Throws()
    {
        var path = Write("""
            {
              "assembly": "Sample.dll",
              "namespace": "Sample.Generated"
            }
            """);

        var exception = Assert.Throws<CodeGenException>(() => CodeGenerator.LoadConfig(path));
        Assert.Contains("output", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NullArray_Throws()
    {
        var path = Write("""
            {
              "assembly": "Sample.dll",
              "output": "generated",
              "namespace": "Sample.Generated",
              "luaCallCSharp": null
            }
            """);

        var exception = Assert.Throws<CodeGenException>(() => CodeGenerator.LoadConfig(path));
        Assert.Contains("luaCallCSharp", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyEntry_Throws()
    {
        var path = Write("""
            {
              "assembly": "Sample.dll",
              "output": "generated",
              "namespace": "Sample.Generated",
              "luaCallCSharp": [""]
            }
            """);

        var exception = Assert.Throws<CodeGenException>(() => CodeGenerator.LoadConfig(path));
        Assert.Contains("luaCallCSharp", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private string Write(string json)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "lua-loom.json");
        File.WriteAllText(path, json);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
