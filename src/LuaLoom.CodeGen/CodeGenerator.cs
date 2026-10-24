using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;

namespace LuaLoom.CodeGen;

public sealed class GeneratorConfig
{
    public string Assembly { get; set; } = string.Empty;

    public string Output { get; set; } = string.Empty;

    public string Namespace { get; set; } = "LuaLoom.Generated";

    public string[] LuaCallCSharp { get; set; } = [];

    public string[] CSharpCallLua { get; set; } = [];
}

public sealed record GeneratedFile(string FileName, string Content);

public sealed record Diagnostic(string Code, string Message);

public sealed class CodeGenException : Exception
{
    public CodeGenException(string message)
        : base(message)
    {
    }
}

public sealed class GenerationResult
{
    public GenerationResult(IReadOnlyList<GeneratedFile> files, IReadOnlyList<Diagnostic> diagnostics)
    {
        Files = files;
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<GeneratedFile> Files { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public bool HasErrors => Diagnostics.Count > 0;
}

public static class CodeGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static GeneratorConfig LoadConfig(string configPath)
    {
        var fullPath = Path.GetFullPath(configPath);
        if (!File.Exists(fullPath))
        {
            throw new CodeGenException($"Config file not found: {fullPath}");
        }

        var config = JsonSerializer.Deserialize<GeneratorConfig>(File.ReadAllText(fullPath), JsonOptions)
            ?? throw new CodeGenException($"Config file is empty or invalid: {fullPath}");

        if (string.IsNullOrWhiteSpace(config.Assembly))
        {
            throw new CodeGenException("Config field 'assembly' is required.");
        }

        return config;
    }

    public static GenerationResult Generate(GeneratorConfig config, string baseDirectory)
    {
        var assemblyPath = Path.GetFullPath(Path.Combine(baseDirectory, config.Assembly));
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"Target assembly not found: {assemblyPath}", assemblyPath);
        }

        var files = new List<GeneratedFile>();
        var diagnostics = new List<Diagnostic>();
        var registrations = new List<string>();

        using var context = CreateContext(assemblyPath);
        var assembly = context.LoadFromAssemblyPath(assemblyPath);

        var typeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in config.LuaCallCSharp)
        {
            if (!typeNames.Add(name))
            {
                diagnostics.Add(new Diagnostic("LL0004", $"Type '{name}' is configured more than once."));
                continue;
            }

            var type = assembly.GetType(name);
            if (type is null)
            {
                diagnostics.Add(new Diagnostic("LL0001", $"Type '{name}' not found in '{assemblyPath}'."));
                continue;
            }

            if (type.IsGenericTypeDefinition || type.IsGenericType)
            {
                diagnostics.Add(new Diagnostic("LL0003", $"Open generic type '{name}' is not supported."));
                continue;
            }

            if (type.IsNested)
            {
                diagnostics.Add(new Diagnostic("LL0002", $"Nested type '{name}' is not supported."));
                continue;
            }

            var binder = Sanitize(type.FullName!) + "Binder";
            files.Add(new GeneratedFile(binder + ".g.cs", EmitTypeBinder(type, binder, config)));
            registrations.Add($"        env.RegisterType(global::{config.Namespace}.{binder}.Create());");
        }

        foreach (var name in config.CSharpCallLua.Distinct(StringComparer.Ordinal).OrderBy(static n => n, StringComparer.Ordinal))
        {
            var type = ResolveConfiguredType(context, assembly, name);
            if (type is null)
            {
                if (name.Contains("<>", StringComparison.Ordinal))
                {
                    diagnostics.Add(new Diagnostic("LL0003", $"Open generic type '{name}' is not supported."));
                }
                else
                {
                    diagnostics.Add(new Diagnostic("LL0001", $"Type '{name}' not found in '{assemblyPath}'."));
                }

                continue;
            }

            if (type.IsGenericTypeDefinition)
            {
                diagnostics.Add(new Diagnostic("LL0003", $"Open generic type '{name}' is not supported."));
                continue;
            }

            if (type.IsGenericType && !IsDelegateType(type) && !type.IsInterface)
            {
                diagnostics.Add(new Diagnostic("LL0003", $"Generic type '{name}' is only supported for delegates and interfaces."));
                continue;
            }

            if (IsDelegateType(type))
            {
                var invoker = FileStem(type) + "Invoker";
                files.Add(new GeneratedFile(invoker + ".g.cs", EmitDelegateInvoker(type, invoker, config)));
                registrations.Add(
                    $"        env.RegisterDelegate(typeof({TypeRef(type)}), new global::{config.Namespace}.{invoker}());");
            }
            else if (type.IsInterface)
            {
                var implementation = FileStem(type) + "Impl";
                files.Add(new GeneratedFile(implementation + ".g.cs", EmitInterfaceImplementation(type, implementation, config)));
                registrations.Add(
                    $"        env.RegisterInterface(typeof({TypeRef(type)}), static t => new global::{config.Namespace}.{implementation}(t));");
            }
            else
            {
                diagnostics.Add(new Diagnostic("LL0002", $"'{name}' is neither a delegate nor an interface."));
            }
        }

        files.Add(new GeneratedFile("LuaBindings.g.cs", EmitRegistration(config, registrations)));

        foreach (var file in files)
        {
            var tree = CSharpSyntaxTree.ParseText(file.Content);
            foreach (var error in tree.GetDiagnostics().Where(static d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
            {
                diagnostics.Add(new Diagnostic("LL0006", $"{file.FileName}: {error.GetMessage()}"));
            }
        }

        files = files
            .Select(static f => new GeneratedFile(f.FileName, f.Content.Replace("\r\n", "\n", StringComparison.Ordinal)))
            .ToList();
        files.Sort(static (a, b) => string.CompareOrdinal(a.FileName, b.FileName));
        return new GenerationResult(files, diagnostics);
    }

    private static Type? ResolveConfiguredType(
        MetadataLoadContext context,
        Assembly assembly,
        string name)
    {
        name = name.Trim();
        var simple = ResolveSimpleType(context, assembly, name);
        if (simple is not null)
        {
            return simple;
        }

        var open = name.IndexOf('<');
        if (open < 0 || !name.EndsWith('>'))
        {
            return null;
        }

        var definitionName = name[..open].Trim();
        var arguments = SplitGenericArguments(name[(open + 1)..^1]);
        if (arguments.Count == 0)
        {
            return null;
        }

        var definition = ResolveSimpleType(context, assembly, definitionName + "`" + arguments.Count);
        if (definition is null || !definition.IsGenericTypeDefinition)
        {
            return null;
        }

        var resolved = new Type[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = ResolveConfiguredType(context, assembly, arguments[i]);
            if (argument is null)
            {
                return null;
            }

            resolved[i] = argument;
        }

        try
        {
            return definition.MakeGenericType(resolved);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static Type? ResolveSimpleType(
        MetadataLoadContext context,
        Assembly assembly,
        string name)
    {
        if (name.IndexOfAny(['<', '>']) >= 0)
        {
            return null;
        }

        var alias = TypeAlias(name);
        if (alias is not null)
        {
            name = alias;
        }

        var type = TryGetType(assembly, name);
        if (type is not null)
        {
            return type;
        }

        type = context.CoreAssembly is null ? null : TryGetType(context.CoreAssembly, name);
        if (type is not null)
        {
            return type;
        }

        foreach (var candidate in context.GetAssemblies())
        {
            type = TryGetType(candidate, name);
            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }

    private static Type? TryGetType(Assembly assembly, string name)
    {
        try
        {
            return assembly.GetType(name);
        }
        catch (Exception exception) when (
            exception is System.IO.FileNotFoundException
                or ArgumentException
                or TypeLoadException
                or BadImageFormatException)
        {
            return null;
        }
    }

    private static string? TypeAlias(string name) => name switch
    {
        "bool" => "System.Boolean",
        "byte" => "System.Byte",
        "sbyte" => "System.SByte",
        "short" => "System.Int16",
        "ushort" => "System.UInt16",
        "int" => "System.Int32",
        "uint" => "System.UInt32",
        "long" => "System.Int64",
        "ulong" => "System.UInt64",
        "float" => "System.Single",
        "double" => "System.Double",
        "decimal" => "System.Decimal",
        "char" => "System.Char",
        "string" => "System.String",
        "object" => "System.Object",
        _ => null,
    };

    private static List<string> SplitGenericArguments(string text)
    {
        var arguments = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    arguments.Add(text[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        var last = text[start..].Trim();
        if (last.Length > 0)
        {
            arguments.Add(last);
        }

        return arguments;
    }

    private static MetadataLoadContext CreateContext(string assemblyPath)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        paths.UnionWith(Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"));

        var directory = Path.GetDirectoryName(assemblyPath);
        if (!string.IsNullOrEmpty(directory))
        {
            paths.UnionWith(Directory.GetFiles(directory, "*.dll"));
        }

        paths.Add(typeof(LuaEnv).Assembly.Location);
        return new MetadataLoadContext(new PathAssemblyResolver(paths));
    }

    private static string EmitTypeBinder(Type type, string binder, GeneratorConfig config)
    {
        var builder = new StringBuilder();
        WriteHeader(builder, config.Namespace);

        var constructors = type.GetConstructors()
            .GroupBy(static c => c.GetParameters().Length)
            .Where(static g => g.Count() == 1)
            .Select(static g => g.Single())
            .Where(static c => !IsObsolete(c)
                && c.GetParameters().All(static p => IsSupportedParameter(p.ParameterType) && !p.HasDefaultValue))
            .OrderBy(static c => c.GetParameters().Length)
            .ToList();

        var instanceMethods = SupportedMethods(type, BindingFlags.Instance);
        var staticMethods = SupportedMethods(type, BindingFlags.Static);
        var instanceProperties = SupportedProperties(type, BindingFlags.Instance);
        var staticProperties = SupportedProperties(type, BindingFlags.Static);
        var instanceFields = SupportedFields(type, BindingFlags.Instance);
        var staticFields = SupportedFields(type, BindingFlags.Static);

        var hasInstance = instanceMethods.Count > 0 || instanceProperties.Count > 0 || instanceFields.Count > 0;
        var hasStatic = staticMethods.Count > 0 || staticProperties.Count > 0 || staticFields.Count > 0;

        builder.AppendLine($"internal static class {binder}");
        builder.AppendLine("{");
        builder.AppendLine($"    public static global::LuaLoom.LuaTypeBinding Create() => new()");
        builder.AppendLine("    {");
        builder.AppendLine($"        Type = typeof({TypeRef(type)}),");
        builder.AppendLine($"        FullName = \"{type.FullName}\",");

        if (constructors.Count > 0)
        {
            builder.AppendLine("        Call = New,");
        }

        if (hasStatic)
        {
            builder.AppendLine("        StaticIndex = StaticGet,");
            if (staticProperties.Any(static p => p.SetMethod is not null) || staticFields.Any(static f => !f.IsInitOnly))
            {
                builder.AppendLine("        StaticNewIndex = StaticSet,");
            }
        }

        if (hasInstance)
        {
            builder.AppendLine("        InstanceIndex = InstanceGet,");
            if (instanceProperties.Any(static p => p.SetMethod is not null) || instanceFields.Any(static f => !f.IsInitOnly))
            {
                builder.AppendLine("        InstanceNewIndex = InstanceSet,");
            }
        }

        builder.AppendLine("    };");

        if (constructors.Count > 0)
        {
            EmitConstructor(builder, type, constructors);
        }

        if (hasStatic)
        {
            EmitStaticHandlers(builder, type, staticMethods, staticProperties, staticFields);
        }

        if (hasInstance)
        {
            EmitInstanceHandlers(builder, type, instanceMethods, instanceProperties, instanceFields);
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void EmitConstructor(StringBuilder builder, Type type, List<ConstructorInfo> constructors)
    {
        builder.AppendLine();
        builder.AppendLine("    private static int New(global::System.IntPtr L)");
        builder.AppendLine("    {");
        builder.AppendLine("        switch (global::LuaLoom.LuaBinding.GetTop(L) - 1)");
        builder.AppendLine("        {");

        foreach (var constructor in constructors)
        {
            var parameters = constructor.GetParameters();
            builder.AppendLine($"            case {parameters.Length}:");
            builder.AppendLine("            {");
            for (var i = 0; i < parameters.Length; i++)
            {
                builder.AppendLine(
                    $"                var a{i} = {ReadExpression(parameters[i].ParameterType, i + 2)};");
            }

            var arguments = string.Join(", ", Enumerable.Range(0, parameters.Length).Select(static i => "a" + i));
            builder.AppendLine($"                var result = new {TypeRef(type)}({arguments});");
            builder.AppendLine("                global::LuaLoom.LuaBinding.PushObject(L, result);");
            builder.AppendLine("                return 1;");
            builder.AppendLine("            }");
        }

        builder.AppendLine("            default:");
        builder.AppendLine("                return 0;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static void EmitStaticHandlers(
        StringBuilder builder,
        Type type,
        List<MethodInfo> methods,
        List<PropertyInfo> properties,
        List<FieldInfo> fields)
    {
        foreach (var group in methods.GroupBy(static m => m.Name, StringComparer.Ordinal))
        {
            EmitMethodGroup(builder, type, group.Key, group.ToList(), isStatic: true);
        }

        builder.AppendLine();
        builder.AppendLine("    private static int StaticGet(global::System.IntPtr L)");
        builder.AppendLine("    {");
        builder.AppendLine("        var key = global::LuaLoom.LuaBinding.ReadString(L, 2);");
        builder.AppendLine("        switch (key)");
        builder.AppendLine("        {");

        foreach (var property in properties)
        {
            builder.AppendLine($"            case \"{property.Name}\":");
            builder.AppendLine($"                {PushStatement(property.PropertyType, $"{TypeRef(type)}.{property.Name}")}");
            builder.AppendLine("                return 1;");
        }

        foreach (var field in fields)
        {
            builder.AppendLine($"            case \"{field.Name}\":");
            builder.AppendLine($"                {PushStatement(field.FieldType, $"{TypeRef(type)}.{field.Name}")}");
            builder.AppendLine("                return 1;");
        }

        foreach (var group in methods.GroupBy(static m => m.Name, StringComparer.Ordinal))
        {
            builder.AppendLine($"            case \"{group.Key}\":");
            builder.AppendLine(
                $"                global::LuaLoom.LuaBinding.PushFunction(L, {HandlerBase(group.Key, isStatic: true)});");
            builder.AppendLine("                return 1;");
        }

        builder.AppendLine("            default:");
        builder.AppendLine("                return 0;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");

        var writableProperties = properties.Where(static p => p.SetMethod is not null).ToList();
        var writableFields = fields.Where(static f => !f.IsInitOnly).ToList();
        if (writableProperties.Count == 0 && writableFields.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("    private static int StaticSet(global::System.IntPtr L)");
        builder.AppendLine("    {");
        builder.AppendLine("        var key = global::LuaLoom.LuaBinding.ReadString(L, 2);");
        builder.AppendLine("        switch (key)");
        builder.AppendLine("        {");

        foreach (var property in writableProperties)
        {
            builder.AppendLine($"            case \"{property.Name}\":");
            builder.AppendLine(
                $"                {TypeRef(type)}.{property.Name} = {ReadExpression(property.PropertyType, 3)};");
            builder.AppendLine("                return 0;");
        }

        foreach (var field in writableFields)
        {
            builder.AppendLine($"            case \"{field.Name}\":");
            builder.AppendLine(
                $"                {TypeRef(type)}.{field.Name} = {ReadExpression(field.FieldType, 3)};");
            builder.AppendLine("                return 0;");
        }

        builder.AppendLine("            default:");
        builder.AppendLine("                global::LuaLoom.LuaBinding.PushNil(L);");
        builder.AppendLine("                return 1;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static void EmitInstanceHandlers(
        StringBuilder builder,
        Type type,
        List<MethodInfo> methods,
        List<PropertyInfo> properties,
        List<FieldInfo> fields)
    {
        foreach (var group in methods.GroupBy(static m => m.Name, StringComparer.Ordinal))
        {
            EmitMethodGroup(builder, type, group.Key, group.ToList(), isStatic: false);
        }

        builder.AppendLine();
        builder.AppendLine("    private static int InstanceGet(global::System.IntPtr L)");
        builder.AppendLine("    {");
        builder.AppendLine($"        var self = ({TypeRef(type)})global::LuaLoom.LuaBinding.ReadObject(L, 1)!;");
        builder.AppendLine("        var key = global::LuaLoom.LuaBinding.ReadString(L, 2);");
        builder.AppendLine("        switch (key)");
        builder.AppendLine("        {");

        foreach (var property in properties)
        {
            builder.AppendLine($"            case \"{property.Name}\":");
            builder.AppendLine($"                {PushStatement(property.PropertyType, $"self.{property.Name}")}");
            builder.AppendLine("                return 1;");
        }

        foreach (var field in fields)
        {
            builder.AppendLine($"            case \"{field.Name}\":");
            builder.AppendLine($"                {PushStatement(field.FieldType, $"self.{field.Name}")}");
            builder.AppendLine("                return 1;");
        }

        foreach (var group in methods.GroupBy(static m => m.Name, StringComparer.Ordinal))
        {
            builder.AppendLine($"            case \"{group.Key}\":");
            builder.AppendLine(
                $"                global::LuaLoom.LuaBinding.PushFunction(L, {HandlerBase(group.Key, isStatic: false)});");
            builder.AppendLine("                return 1;");
        }

        builder.AppendLine("            default:");
        builder.AppendLine("                return 0;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");

        var writableProperties = properties.Where(static p => p.SetMethod is not null).ToList();
        var writableFields = fields.Where(static f => !f.IsInitOnly).ToList();
        if (writableProperties.Count == 0 && writableFields.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("    private static int InstanceSet(global::System.IntPtr L)");
        builder.AppendLine("    {");
        builder.AppendLine($"        var self = ({TypeRef(type)})global::LuaLoom.LuaBinding.ReadObject(L, 1)!;");
        builder.AppendLine("        var key = global::LuaLoom.LuaBinding.ReadString(L, 2);");
        builder.AppendLine("        switch (key)");
        builder.AppendLine("        {");

        foreach (var property in writableProperties)
        {
            builder.AppendLine($"            case \"{property.Name}\":");
            builder.AppendLine($"                self.{property.Name} = {ReadExpression(property.PropertyType, 3)};");
            builder.AppendLine("                return 0;");
        }

        foreach (var field in writableFields)
        {
            builder.AppendLine($"            case \"{field.Name}\":");
            builder.AppendLine($"                self.{field.Name} = {ReadExpression(field.FieldType, 3)};");
            builder.AppendLine("                return 0;");
        }

        builder.AppendLine("            default:");
        builder.AppendLine("                global::LuaLoom.LuaBinding.PushNil(L);");
        builder.AppendLine("                return 1;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static void EmitMethodGroup(StringBuilder builder, Type type, string name, List<MethodInfo> overloads, bool isStatic)
    {
        var byArity = overloads
            .GroupBy(static m => m.GetParameters().Length)
            .Select(static g => g.First())
            .OrderBy(static m => m.GetParameters().Length)
            .ToList();

        var dispatch = HandlerBase(name, isStatic);
        builder.AppendLine();
        builder.AppendLine($"    private static readonly global::LuaLoom.Native.LuaCSFunction {dispatch} = {dispatch}Dispatch;");
        builder.AppendLine();
        builder.AppendLine($"    private static int {dispatch}Dispatch(global::System.IntPtr L)");
        builder.AppendLine("    {");
        builder.AppendLine(
            isStatic
                ? "        switch (global::LuaLoom.LuaBinding.GetTop(L))"
                : "        switch (global::LuaLoom.LuaBinding.GetTop(L) - 1)");
        builder.AppendLine("        {");

        for (var i = 0; i < byArity.Count; i++)
        {
            var count = byArity[i].GetParameters().Length;
            builder.AppendLine($"            case {count}: return {HandlerName(i, name, isStatic)}(L);");
        }

        builder.AppendLine("            default:");
        builder.AppendLine(
            $"                global::LuaLoom.LuaBinding.Throw(L, \"No matching overload for '{type.Name}.{name}'.\");");
        builder.AppendLine("                return 0;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");

        for (var i = 0; i < byArity.Count; i++)
        {
            EmitMethodImplementation(builder, type, byArity[i], isStatic, HandlerName(i, name, isStatic));
        }
    }

    private static void EmitMethodImplementation(
        StringBuilder builder,
        Type type,
        MethodInfo method,
        bool isStatic,
        string handler)
    {
        var parameters = method.GetParameters();
        builder.AppendLine();
        builder.AppendLine($"    private static readonly global::LuaLoom.Native.LuaCSFunction {handler} = {handler}Impl;");
        builder.AppendLine();
        builder.AppendLine($"    private static int {handler}Impl(global::System.IntPtr L)");
        builder.AppendLine("    {");

        var argumentOffset = isStatic ? 1 : 2;
        if (!isStatic)
        {
            builder.AppendLine($"        var self = ({TypeRef(type)})global::LuaLoom.LuaBinding.ReadObject(L, 1)!;");
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            builder.AppendLine($"        var a{i} = {ReadExpression(parameters[i].ParameterType, argumentOffset + i)};");
        }

        var arguments = string.Join(", ", Enumerable.Range(0, parameters.Length).Select(static i => "a" + i));
        var invocation = isStatic
            ? $"{TypeRef(type)}.{method.Name}({arguments})"
            : $"self.{method.Name}({arguments})";

        if (Is(method.ReturnType, typeof(void)))
        {
            builder.AppendLine($"        {invocation};");
            builder.AppendLine("        return 0;");
        }
        else
        {
            builder.AppendLine($"        var result = {invocation};");
            builder.AppendLine($"        {PushStatement(method.ReturnType, "result")}");
            builder.AppendLine("        return 1;");
        }

        builder.AppendLine("    }");
    }

    private static string EmitDelegateInvoker(Type type, string invoker, GeneratorConfig config)
    {
        var invoke = type.GetMethod("Invoke")
            ?? throw new CodeGenException($"Delegate '{type.FullName}' has no Invoke method.");

        var parameters = invoke.GetParameters();
        if (parameters.Any(static p => p.ParameterType.IsByRef))
        {
            throw new CodeGenException($"Delegate '{type.FullName}' has by-ref parameters, which are not supported.");
        }

        var builder = new StringBuilder();
        WriteHeader(builder, config.Namespace);
        builder.AppendLine($"internal sealed class {invoker} : global::LuaLoom.ILuaDelegateInvoker");
        builder.AppendLine("{");
        builder.AppendLine("    public global::System.Delegate Create(global::LuaLoom.LuaFunction function)");
        builder.AppendLine("    {");

        var argumentNames = Enumerable.Range(0, parameters.Length).Select(static i => "a" + i).ToArray();
        var call = $"function.Call({string.Join(", ", argumentNames)})";

        if (Is(invoke.ReturnType, typeof(void)))
        {
            builder.AppendLine($"        return new {TypeRef(type)}(({string.Join(", ", argumentNames)}) =>");
            builder.AppendLine("        {");
            builder.AppendLine($"            {call};");
            builder.AppendLine("        });");
        }
        else
        {
            builder.AppendLine(
                $"        return new {TypeRef(type)}(({string.Join(", ", argumentNames)}) => {ConvertResult(invoke.ReturnType, call)});");
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string EmitInterfaceImplementation(Type type, string implementation, GeneratorConfig config)
    {
        var builder = new StringBuilder();
        WriteHeader(builder, config.Namespace);
        builder.AppendLine($"internal sealed class {implementation} : {TypeRef(type)}");
        builder.AppendLine("{");
        builder.AppendLine("    private readonly global::LuaLoom.LuaTable _table;");
        builder.AppendLine();
        builder.AppendLine($"    public {implementation}(global::LuaLoom.LuaTable table) => _table = table;");

        foreach (var property in SupportedProperties(type, BindingFlags.Instance, forBinding: false))
        {
            builder.AppendLine();
            builder.AppendLine($"    public {TypeRef(property.PropertyType)} {property.Name}");
            builder.AppendLine("    {");
            if (property.GetMethod is not null)
            {
                builder.AppendLine("        get");
                builder.AppendLine("        {");
                builder.AppendLine(
                    $"            using var function = _table.Get<global::LuaLoom.LuaFunction>(\"get_{property.Name}\")!;");
                builder.AppendLine(
                    $"            return {ConvertResult(property.PropertyType, "function.Call(_table)")};");
                builder.AppendLine("        }");
            }

            if (property.SetMethod is not null)
            {
                builder.AppendLine("        set");
                builder.AppendLine("        {");
                builder.AppendLine(
                    $"            using var function = _table.Get<global::LuaLoom.LuaFunction>(\"set_{property.Name}\")!;");
                builder.AppendLine("            function.Call(_table, value);");
                builder.AppendLine("        }");
            }

            builder.AppendLine("    }");
        }

        foreach (var method in SupportedMethods(type, BindingFlags.Instance, forBinding: false))
        {
            var parameters = method.GetParameters();
            var signature = string.Join(
                ", ",
                parameters.Select(static p => $"{TypeRef(p.ParameterType)} {p.Name}"));
            var arguments = string.Join(", ", parameters.Select(static p => p.Name));
            var call = string.IsNullOrEmpty(arguments) ? "function.Call(_table)" : $"function.Call(_table, {arguments})";

            builder.AppendLine();
            builder.AppendLine($"    public {TypeRef(method.ReturnType)} {method.Name}({signature})");
            builder.AppendLine("    {");
            builder.AppendLine(
                $"        using var function = _table.Get<global::LuaLoom.LuaFunction>(\"{method.Name}\")!;");

            if (Is(method.ReturnType, typeof(void)))
            {
                builder.AppendLine($"        {call};");
            }
            else
            {
                builder.AppendLine($"        return {ConvertResult(method.ReturnType, call)};");
            }

            builder.AppendLine("    }");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string EmitRegistration(GeneratorConfig config, List<string> registrations)
    {
        var builder = new StringBuilder();
        WriteHeader(builder, config.Namespace);
        builder.AppendLine("public static class LuaBindings");
        builder.AppendLine("{");
        builder.AppendLine("    public static void Register(global::LuaLoom.LuaEnv env)");
        builder.AppendLine("    {");

        foreach (var registration in registrations)
        {
            builder.AppendLine(registration);
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void WriteHeader(StringBuilder builder, string ns)
    {
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.AppendLine($"namespace {ns};");
        builder.AppendLine();
    }

    private static List<MethodInfo> SupportedMethods(Type type, BindingFlags flags, bool forBinding = true)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.DeclaredOnly;
        var methods = type.GetMethods(flags | all)
            .Where(static m => !m.IsSpecialName && !m.IsGenericMethodDefinition)
            .ToList();

        if (!forBinding)
        {
            return methods
                .Where(static m => IsSupportedMethod(m))
                .OrderBy(static m => m.Name, StringComparer.Ordinal)
                .ThenBy(static m => m.GetParameters().Length)
                .ToList();
        }

        var supported = new List<MethodInfo>();
        foreach (var group in methods.GroupBy(static m => m.Name, StringComparer.Ordinal))
        {
            var overloads = group.ToList();
            if (overloads.Any(static m => IsObsolete(m) || !IsSupportedMethod(m)))
            {
                continue;
            }

            if (overloads.GroupBy(static m => m.GetParameters().Length).Any(static g => g.Count() > 1))
            {
                continue;
            }

            supported.AddRange(overloads);
        }

        return supported
            .OrderBy(static m => m.Name, StringComparer.Ordinal)
            .ThenBy(static m => m.GetParameters().Length)
            .ToList();
    }

    private static List<PropertyInfo> SupportedProperties(Type type, BindingFlags flags, bool forBinding = true)
    {
        return type.GetProperties(flags | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(static p => p.GetIndexParameters().Length == 0 && p.GetMethod is not null)
            .Where(p => !forBinding
                || (!IsObsolete(p)
                    && !IsObsolete(p.GetMethod)
                    && (p.SetMethod is null || !IsObsolete(p.SetMethod))))
            .Where(static p => IsSupportedParameter(p.PropertyType))
            .OrderBy(static p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static List<FieldInfo> SupportedFields(Type type, BindingFlags flags)
    {
        return type.GetFields(flags | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(static f => !IsObsolete(f))
            .Where(static f => IsSupportedParameter(f.FieldType))
            .OrderBy(static f => f.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsSupportedMethod(MethodInfo method) =>
        method.GetParameters().All(static p => IsSupportedParameter(p.ParameterType) && !p.HasDefaultValue);

    private static bool IsObsolete(MemberInfo? member) =>
        member is not null
        && member.GetCustomAttributesData()
            .Any(static attribute => attribute.AttributeType.FullName == typeof(ObsoleteAttribute).FullName);

    private static bool IsSupportedParameter(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsGenericParameter || type.IsGenericType || type.IsArray)
        {
            return false;
        }

        return type.IsPrimitive
            || type.IsEnum
            || Is(type, typeof(string))
            || type.IsClass
            || type.IsValueType;
    }

    private static string HandlerBase(string name, bool isStatic) =>
        $"{(isStatic ? "Static" : "Instance")}_{name}";

    private static string HandlerName(int index, string name, bool isStatic) =>
        $"{HandlerBase(name, isStatic)}_{index}";

    private static string TypeRef(Type type)
    {
        if (Is(type, typeof(void)))
        {
            return "void";
        }

        if (Is(type, typeof(int)))
        {
            return "int";
        }

        if (Is(type, typeof(long)))
        {
            return "long";
        }

        if (Is(type, typeof(double)))
        {
            return "double";
        }

        if (Is(type, typeof(float)))
        {
            return "float";
        }

        if (Is(type, typeof(bool)))
        {
            return "bool";
        }

        if (Is(type, typeof(string)))
        {
            return "string";
        }

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            var definition = type.GetGenericTypeDefinition();
            var name = StripArity(definition.FullName ?? definition.Name).Replace('+', '.');
            var arguments = string.Join(", ", type.GetGenericArguments().Select(TypeRef));
            return $"global::{name}<{arguments}>";
        }

        return "global::" + (type.FullName ?? type.Name).Replace('+', '.');
    }

    private static string StripArity(string name)
    {
        var builder = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] != '`')
            {
                builder.Append(name[i]);
                continue;
            }

            while (i + 1 < name.Length && char.IsDigit(name[i + 1]))
            {
                i++;
            }
        }

        return builder.ToString();
    }

    private static string ReadExpression(Type type, int index)
    {
        if (Is(type, typeof(int)))
        {
            return $"global::LuaLoom.LuaBinding.ReadInt(L, {index})";
        }

        if (Is(type, typeof(long)))
        {
            return $"global::LuaLoom.LuaBinding.ReadLong(L, {index})";
        }

        if (Is(type, typeof(double)))
        {
            return $"global::LuaLoom.LuaBinding.ReadDouble(L, {index})";
        }

        if (Is(type, typeof(float)))
        {
            return $"(float)global::LuaLoom.LuaBinding.ReadDouble(L, {index})";
        }

        if (Is(type, typeof(bool)))
        {
            return $"global::LuaLoom.LuaBinding.ReadBoolean(L, {index})";
        }

        if (Is(type, typeof(string)))
        {
            return $"global::LuaLoom.LuaBinding.ReadString(L, {index})!";
        }

        if (type.IsEnum)
        {
            return $"({TypeRef(type)})global::LuaLoom.LuaBinding.ReadLong(L, {index})";
        }

        return $"({TypeRef(type)})global::LuaLoom.LuaBinding.ReadObject(L, {index})!";
    }

    private static string PushStatement(Type type, string expression)
    {
        if (Is(type, typeof(int)))
        {
            return $"global::LuaLoom.LuaBinding.PushInt(L, {expression});";
        }

        if (Is(type, typeof(long)))
        {
            return $"global::LuaLoom.LuaBinding.PushLong(L, {expression});";
        }

        if (Is(type, typeof(double)) || Is(type, typeof(float)))
        {
            return $"global::LuaLoom.LuaBinding.PushDouble(L, {expression});";
        }

        if (Is(type, typeof(bool)))
        {
            return $"global::LuaLoom.LuaBinding.PushBoolean(L, {expression});";
        }

        if (Is(type, typeof(string)))
        {
            return $"global::LuaLoom.LuaBinding.PushString(L, {expression});";
        }

        if (type.IsEnum)
        {
            return $"global::LuaLoom.LuaBinding.PushLong(L, (long){expression});";
        }

        return $"global::LuaLoom.LuaBinding.PushObject(L, {expression});";
    }

    private static string ConvertResult(Type type, string call)
    {
        if (Is(type, typeof(void)))
        {
            return $"{call};";
        }

        if (Is(type, typeof(int)))
        {
            return $"checked((int)(long){call}[0])";
        }

        if (Is(type, typeof(long)))
        {
            return $"(long){call}[0]";
        }

        if (Is(type, typeof(double)))
        {
            return $"(double){call}[0]";
        }

        if (Is(type, typeof(float)))
        {
            return $"(float)(double){call}[0]";
        }

        if (Is(type, typeof(bool)))
        {
            return $"(bool){call}[0]";
        }

        if (Is(type, typeof(string)))
        {
            return $"(string){call}[0]";
        }

        if (type.IsEnum)
        {
            return $"({TypeRef(type)})(long){call}[0]";
        }

        return $"({TypeRef(type)}){call}[0]!";
    }

    private static string FileStem(Type type) => Sanitize(
        type.IsGenericType
            ? TypeRef(type).Replace("global::", string.Empty, StringComparison.Ordinal)
            : type.FullName ?? type.Name);

    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }

    private static bool Is(Type type, Type runtime) => type.FullName == runtime.FullName;

    private static bool IsDelegateType(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.FullName is "System.MulticastDelegate" or "System.Delegate")
            {
                return true;
            }
        }

        return false;
    }
}
