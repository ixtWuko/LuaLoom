using System.Text;
using LuaLoom.CodeGen;

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help")
    {
        PrintUsage();
        return args.Length == 0 ? 64 : 0;
    }

    var command = args[0];
    if (command is not ("generate" or "check"))
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 64;
    }

    string? configPath = null;
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] == "--config" && i + 1 < args.Length)
        {
            configPath = args[++i];
        }
        else
        {
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 64;
        }
    }

    if (configPath is null)
    {
        Console.Error.WriteLine("Missing --config.");
        PrintUsage();
        return 64;
    }

    try
    {
        var config = CodeGenerator.LoadConfig(configPath);
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        var result = CodeGenerator.Generate(config, baseDirectory);

        if (result.HasErrors)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
            }

            return 2;
        }

        var outputDirectory = Path.GetFullPath(Path.Combine(baseDirectory, config.Output));
        Directory.CreateDirectory(outputDirectory);

        if (command == "generate")
        {
            foreach (var file in result.Files)
            {
                var path = Path.Combine(outputDirectory, file.FileName);
                File.WriteAllText(path, file.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            Console.WriteLine($"Generated {result.Files.Count} file(s) to {outputDirectory}");
            return 0;
        }

        var differences = new List<string>();
        foreach (var file in result.Files)
        {
            var path = Path.Combine(outputDirectory, file.FileName);
            if (!File.Exists(path) || File.ReadAllText(path) != file.Content)
            {
                differences.Add(file.FileName);
            }
        }

        if (differences.Count > 0)
        {
            foreach (var file in differences)
            {
                Console.Error.WriteLine($"Out of date: {file}");
            }

            return 1;
        }

        Console.WriteLine("Generated files are up to date.");
        return 0;
    }
    catch (FileNotFoundException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 2;
    }
    catch (CodeGenException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 2;
    }
    catch (IOException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 2;
    }
}

static void PrintUsage()
{
    Console.Error.WriteLine("lua-loom-gen generate --config <path>");
    Console.Error.WriteLine("lua-loom-gen check    --config <path>");
}
