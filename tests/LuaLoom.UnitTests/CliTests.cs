using System.Diagnostics;
using System.Text.Json;
using LuaLoom.UnitTests.Sample;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class CliTests
{
    [Fact]
    public void Generate_Coverage_And_Check()
    {
        using var workspace = new CliWorkspace();
        var configPath = workspace.WriteConfig("LuaLoom.UnitTests.Sample.Player");

        var generate = Run(workspace, "generate", "--config", configPath, "--coverage");
        Assert.Equal(0, generate.ExitCode);
        Assert.Contains("Coverage:", generate.Output);
        Assert.Contains(
            Directory.GetFiles(workspace.OutputDirectory, "*.g.cs"),
            static path => path.EndsWith("PlayerBinder.g.cs", StringComparison.Ordinal));

        var check = Run(workspace, "check", "--config", configPath);
        Assert.Equal(0, check.ExitCode);
        Assert.Contains("up to date", check.Output, StringComparison.OrdinalIgnoreCase);

        File.AppendAllText(
            Path.Combine(workspace.OutputDirectory, "LuaBindings.g.cs"),
            "\n// modified\n");
        var dirty = Run(workspace, "check", "--config", configPath);
        Assert.Equal(1, dirty.ExitCode);
        Assert.Contains("Out of date", dirty.Output);
    }

    [Fact]
    public void Check_ReportsStaleGeneratedFiles()
    {
        using var workspace = new CliWorkspace();
        var configPath = workspace.WriteConfig("LuaLoom.UnitTests.Sample.Player");

        Assert.Equal(0, Run(workspace, "generate", "--config", configPath).ExitCode);
        File.WriteAllText(Path.Combine(workspace.OutputDirectory, "StaleBinder.g.cs"), "// stale");

        var check = Run(workspace, "check", "--config", configPath);

        Assert.Equal(1, check.ExitCode);
        Assert.Contains("StaleBinder.g.cs (stale)", check.Output);
    }

    [Fact]
    public void Check_ReportsOutdatedLinkXml()
    {
        using var workspace = new CliWorkspace();
        var configPath = workspace.WriteConfig("LuaLoom.UnitTests.Sample.Player");

        Assert.Equal(0, Run(workspace, "generate", "--config", configPath).ExitCode);

        var link = Path.Combine(workspace.OutputDirectory, "link.xml");
        File.WriteAllText(link, "<linker />");

        var check = Run(workspace, "check", "--config", configPath);

        Assert.Equal(1, check.ExitCode);
        Assert.Contains("Out of date: link.xml", check.Output);
    }

    [Fact]
    public void InvalidConfig_ReturnsConfigurationError()
    {
        using var workspace = new CliWorkspace();
        var configPath = workspace.WriteConfig("LuaLoom.UnitTests.Sample.Missing");

        var result = Run(workspace, "generate", "--config", configPath);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("LL0001", result.Output);
    }

    private static readonly Lazy<string> CliAssembly = new(ResolveCliAssembly);

    private static string ResolveCliAssembly()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LuaLoom.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate LuaLoom.sln above the test output directory.");
        }

        var cliRoot = Path.Combine(directory.FullName, "artifacts", "bin", "LuaLoom.Cli");
        if (Directory.Exists(cliRoot))
        {
            foreach (var candidate in Directory
                         .EnumerateFiles(cliRoot, "lua-loom-gen.dll", SearchOption.AllDirectories)
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "lua-loom-gen.dll was not found under artifacts/bin/LuaLoom.Cli; build the solution before running CLI tests.");
    }

    private static (int ExitCode, string Output) Run(CliWorkspace workspace, params string[] args)
    {
        var cli = CliAssembly.Value;

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workspace.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(cli);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start lua-loom-gen.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private sealed class CliWorkspace : IDisposable
    {
        public CliWorkspace()
        {
            Root = Path.Combine(AppContext.BaseDirectory, "cli-temp", Guid.NewGuid().ToString("N"));
            OutputDirectory = Path.Combine(Root, "generated");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string OutputDirectory { get; }

        public string WriteConfig(string typeName)
        {
            var config = new
            {
                assembly = typeof(Player).Assembly.Location,
                output = OutputDirectory,
                @namespace = "LuaLoom.Generated.Cli",
                luaCallCSharp = new[] { typeName },
                csharpCallLua = Array.Empty<string>(),
            };
            var path = Path.Combine(Root, "lua-loom.json");
            File.WriteAllText(path, JsonSerializer.Serialize(config));
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
