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
    private static readonly (string LuaName, string MethodName)[] OperatorBindings =
    [
        ("__add", "op_Addition"),
        ("__sub", "op_Subtraction"),
        ("__mul", "op_Multiply"),
        ("__div", "op_Division"),
        ("__mod", "op_Modulus"),
        ("__unm", "op_UnaryNegation"),
        ("__eq", "op_Equality"),
        ("__lt", "op_LessThan"),
        ("__le", "op_LessThanOrEqual"),
    ];

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

            if (IsExtensionClass(type))
            {
                registrations.Add($"        env.RegisterExtensionType(typeof({TypeRef(type)}));");
                registrations.Add($"        global::{config.Namespace}.{binder}.RegisterExtensions(env);");
            }
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

        var includeBase = type.BaseType is not null
            && config.LuaCallCSharp.Any(name =>
                string.Equals(name, type.BaseType.FullName, StringComparison.Ordinal));

        var instanceMethods = SupportedMethods(type, BindingFlags.Instance, includeBase: includeBase);
        var staticMethods = SupportedMethods(type, BindingFlags.Static, includeBase: includeBase);
        var instanceProperties = SupportedProperties(type, BindingFlags.Instance, includeBase: includeBase);
        var staticProperties = SupportedProperties(type, BindingFlags.Static, includeBase: includeBase);
        var instanceFields = SupportedFields(type, BindingFlags.Instance, includeBase: includeBase);
        var staticFields = SupportedFields(type, BindingFlags.Static, includeBase: includeBase);
        var events = SupportedEvents(type, includeBase);
        var extensionMethods = staticMethods.Where(IsExtensionMethod).ToList();
        var indexer = FindIndexer(type);
        var operators = FindOperators(type);

        var hasInstance = instanceMethods.Count > 0
            || instanceProperties.Count > 0
            || instanceFields.Count > 0
            || events.Count > 0
            || indexer is not null;
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

        if (indexer is not null)
        {
            builder.AppendLine("        InstanceIndexer = IndexerGet,");
            if (indexer.SetMethod is not null)
            {
                builder.AppendLine("        InstanceIndexerSet = IndexerSet,");
            }
        }

        if (operators.Count > 0)
        {
            builder.AppendLine("        Operators = new global::System.Collections.Generic.Dictionary<string, global::LuaLoom.Native.LuaCSFunction>");
            builder.AppendLine("        {");
            foreach (var (luaName, method) in operators)
            {
                builder.AppendLine($"            [\"{luaName}\"] = {OperatorHandlerName(method)},");
            }

            builder.AppendLine("        },");
        }

        builder.AppendLine("    };");

        if (extensionMethods.Count > 0)
        {
            EmitExtensionRegistration(builder, extensionMethods);
        }

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
            EmitInstanceHandlers(builder, type, instanceMethods, instanceProperties, instanceFields, events);
        }

        if (events.Count > 0)
        {
            EmitEventHandlers(builder, type, events);
        }

        if (indexer is not null)
        {
            EmitIndexerHandlers(builder, type, indexer);
        }

        if (operators.Count > 0)
        {
            EmitOperatorHandlers(builder, type, operators);
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
        List<FieldInfo> fields,
        List<EventInfo> events)
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

        foreach (var eventInfo in events)
        {
            builder.AppendLine($"            case \"{eventInfo.Name}\":");
            builder.AppendLine(
                $"                global::LuaLoom.LuaBinding.PushFunction(L, {EventHandlerName(eventInfo)});");
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

    private static void EmitIndexerHandlers(StringBuilder builder, Type type, PropertyInfo indexer)
    {
        var keyType = indexer.GetIndexParameters()[0].ParameterType;
        var valueType = indexer.PropertyType;

        builder.AppendLine();
        builder.AppendLine("    private static int IndexerGet(global::System.IntPtr L)");
        builder.AppendLine("    {");
        builder.AppendLine($"        var self = ({TypeRef(type)})global::LuaLoom.LuaBinding.ReadObject(L, 1)!;");
        builder.AppendLine($"        var key = {ReadExpression(keyType, 2)};");
        builder.AppendLine("        var result = self[key];");
        builder.AppendLine($"        {PushStatement(valueType, "result")}");
        builder.AppendLine("        return 1;");
        builder.AppendLine("    }");

        if (indexer.SetMethod is null)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("    private static int IndexerSet(global::System.IntPtr L)");
        builder.AppendLine("    {");
        builder.AppendLine($"        var self = ({TypeRef(type)})global::LuaLoom.LuaBinding.ReadObject(L, 1)!;");
        builder.AppendLine($"        self[{ReadExpression(keyType, 2)}] = {ReadExpression(valueType, 3)};");
        builder.AppendLine("        return 0;");
        builder.AppendLine("    }");
    }

    private static List<(string LuaName, MethodInfo Method)> FindOperators(Type type)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        var operators = new List<(string LuaName, MethodInfo Method)>();

        foreach (var (luaName, methodName) in OperatorBindings)
        {
            var parameterCount = methodName == "op_UnaryNegation" ? 1 : 2;
            var method = methods
                .Where(m => m.Name == methodName)
                .Where(m => m.GetParameters().Length == parameterCount)
                .Where(m => m.GetParameters().All(static p => IsSupportedOperatorType(p.ParameterType)))
                .Where(m => IsSupportedOperatorType(m.ReturnType))
                .OrderBy(static m => m.GetParameters().Length)
                .FirstOrDefault();

            if (method is not null)
            {
                operators.Add((luaName, method));
            }
        }

        return operators;
    }

    private static bool IsSupportedOperatorType(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsGenericParameter || type.IsGenericType || type.IsArray)
        {
            return false;
        }

        if (type.IsValueType && !type.IsPrimitive && !type.IsEnum)
        {
            return false;
        }

        return type.IsClass
            || type.IsPrimitive
            || type.IsEnum
            || Is(type, typeof(string))
            || Is(type, typeof(object));
    }

    private static void EmitOperatorHandlers(
        StringBuilder builder,
        Type type,
        List<(string LuaName, MethodInfo Method)> operators)
    {
        foreach (var (_, method) in operators)
        {
            var parameters = method.GetParameters();
            builder.AppendLine();
            builder.AppendLine($"    private static int {OperatorHandlerName(method)}(global::System.IntPtr L)");
            builder.AppendLine("    {");
            builder.AppendLine($"        var left = ({TypeRef(parameters[0].ParameterType)})global::LuaLoom.LuaBinding.ReadObject(L, 1)!;");

            if (parameters.Length == 2)
            {
                builder.AppendLine(
                    $"        var right = {ReadExpression(parameters[1].ParameterType, 2)};");
                builder.AppendLine(
                    $"        var result = left {OperatorToken(method.Name)} right;");
            }
            else
            {
                builder.AppendLine(
                    $"        var result = {OperatorToken(method.Name)}left;");
            }

            builder.AppendLine($"        {PushStatement(method.ReturnType, "result")}");
            builder.AppendLine("        return 1;");
            builder.AppendLine("    }");
        }
    }

    private static string OperatorHandlerName(MethodInfo method) =>
        "Operator_" + method.Name.Replace("op_", string.Empty, StringComparison.Ordinal);

    private static string OperatorToken(string methodName) => methodName switch
    {
        "op_Addition" => "+",
        "op_Subtraction" => "-",
        "op_Multiply" => "*",
        "op_Division" => "/",
        "op_Modulus" => "%",
        "op_UnaryNegation" => "-",
        "op_Equality" => "==",
        "op_LessThan" => "<",
        "op_LessThanOrEqual" => "<=",
        _ => throw new CodeGenException($"Unsupported operator '{methodName}'."),
    };

    private static PropertyInfo? FindIndexer(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(static p =>
            {
                var parameters = p.GetIndexParameters();
                return parameters.Length == 1
                    && p.GetMethod is not null
                    && parameters[0].ParameterType != typeof(string)
                    && !parameters[0].ParameterType.IsArray
                    && IsSupportedParameter(parameters[0].ParameterType)
                    && IsSupportedParameter(p.PropertyType);
            });

    private static void EmitMethodGroup(StringBuilder builder, Type type, string name, List<MethodInfo> overloads, bool isStatic)
    {
        if (overloads.Count == 1 && HasComplexParameters(overloads[0]))
        {
            EmitComplexMethodGroup(builder, type, name, overloads[0], isStatic);
            return;
        }

        if (overloads.Count == 1
            && overloads[0].GetParameters().Any(static p => p.HasDefaultValue))
        {
            EmitOptionalMethodGroup(builder, type, name, overloads[0], isStatic);
            return;
        }

        var groups = overloads
            .GroupBy(static m => m.GetParameters().Length)
            .OrderBy(static g => g.Key)
            .Select(group => group
                .OrderByDescending(Specificity)
                .ToList())
            .ToList();

        var handlerIndex = 0;
        var handlers = new List<(MethodInfo Method, string Handler)>();
        var groupHandlers = new List<List<(MethodInfo Method, string Handler)>>();
        foreach (var group in groups)
        {
            var groupEntries = new List<(MethodInfo Method, string Handler)>();
            foreach (var method in group)
            {
                var entry = (method, HandlerName(handlerIndex++, name, isStatic));
                handlers.Add(entry);
                groupEntries.Add(entry);
            }

            groupHandlers.Add(groupEntries);
        }

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

        for (var i = 0; i < groups.Count; i++)
        {
            var count = groups[i][0].GetParameters().Length;
            var entries = groupHandlers[i];

            if (entries.Count == 1)
            {
                builder.AppendLine($"            case {count}: return {entries[0].Handler}(L);");
                continue;
            }

            builder.AppendLine($"            case {count}:");
            foreach (var entry in entries)
            {
                var condition = BuildCondition(entry.Method, isStatic ? 1 : 2);
                if (condition == "true")
                {
                    builder.AppendLine($"                return {entry.Handler}(L);");
                }
                else
                {
                    builder.AppendLine($"                if ({condition}) return {entry.Handler}(L);");
                }
            }

            builder.AppendLine("                break;");
        }

        builder.AppendLine("            default:");
        builder.AppendLine(
            $"                global::LuaLoom.LuaBinding.Throw(L, \"No matching overload for '{type.Name}.{name}'.\");");
        builder.AppendLine("                return 0;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");

        foreach (var entry in handlers)
        {
            EmitMethodImplementation(builder, type, entry.Method, isStatic, entry.Handler);
        }
    }

    private static void EmitOptionalMethodGroup(
        StringBuilder builder,
        Type type,
        string name,
        MethodInfo method,
        bool isStatic)
    {
        var parameters = method.GetParameters();
        var requiredCount = parameters.Count(static p => !p.HasDefaultValue);
        var dispatch = HandlerBase(name, isStatic);
        var handlers = new List<(int Count, string Handler)>();

        for (var count = requiredCount; count <= parameters.Length; count++)
        {
            handlers.Add((count, HandlerName(count - requiredCount, name, isStatic)));
        }

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

        foreach (var (count, handler) in handlers)
        {
            builder.AppendLine($"            case {count}: return {handler}(L);");
        }

        builder.AppendLine("            default:");
        builder.AppendLine(
            $"                global::LuaLoom.LuaBinding.Throw(L, \"No matching overload for '{type.Name}.{name}'.\");");
        builder.AppendLine("                return 0;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");

        foreach (var (count, handler) in handlers)
        {
            EmitMethodImplementation(builder, type, method, isStatic, handler, count);
        }
    }

    private static void EmitComplexMethodGroup(
        StringBuilder builder,
        Type type,
        string name,
        MethodInfo method,
        bool isStatic)
    {
        var parameters = method.GetParameters();
        var hasParams = parameters.Any(IsParamArray);
        var fixedCount = parameters.Count(static p => !p.IsOut && !IsParamArray(p));
        var handler = HandlerName(0, name, isStatic);
        var dispatch = HandlerBase(name, isStatic);

        builder.AppendLine();
        builder.AppendLine($"    private static readonly global::LuaLoom.Native.LuaCSFunction {dispatch} = {dispatch}Dispatch;");
        builder.AppendLine();
        builder.AppendLine($"    private static int {dispatch}Dispatch(global::System.IntPtr L)");
        builder.AppendLine("    {");
        builder.AppendLine(
            isStatic
                ? "        var count = global::LuaLoom.LuaBinding.GetTop(L);"
                : "        var count = global::LuaLoom.LuaBinding.GetTop(L) - 1;");

        if (hasParams)
        {
            builder.AppendLine($"        if (count < {fixedCount})");
        }
        else
        {
            builder.AppendLine($"        if (count != {fixedCount})");
        }

        builder.AppendLine("        {");
        builder.AppendLine(
            $"            global::LuaLoom.LuaBinding.Throw(L, \"No matching overload for '{type.Name}.{name}'.\");");
        builder.AppendLine("            return 0;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine($"        return {handler}(L);");
        builder.AppendLine("    }");

        EmitMethodImplementation(builder, type, method, isStatic, handler);
    }

    private static string BuildCondition(MethodInfo method, int firstIndex)
    {
        var conditions = new List<string>();
        var parameters = method.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            var condition = ConditionFor(parameters[i].ParameterType, firstIndex + i);
            if (condition is not null)
            {
                conditions.Add(condition);
            }
        }

        return conditions.Count == 0 ? "true" : string.Join(" && ", conditions);
    }

    private static string DefaultExpression(ParameterInfo parameter)
    {
        var value = parameter.RawDefaultValue;
        if (value is null || value is DBNull || value == Type.Missing)
        {
            return "null";
        }

        var type = parameter.ParameterType;
        if (type.IsEnum)
        {
            return $"({TypeRef(type)})({Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)})";
        }

        if (value is string text)
        {
            return "\"" + text
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }

        if (value is char character)
        {
            return "'" + (character switch
            {
                '\\' => "\\\\",
                '\'' => "\\'",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => character.ToString(),
            }) + "'";
        }

        if (value is bool boolean)
        {
            return boolean ? "true" : "false";
        }

        var textValue = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
            ?? "default";
        if (type == typeof(float))
        {
            return textValue + "f";
        }

        if (type == typeof(decimal))
        {
            return textValue + "m";
        }

        if (type == typeof(long))
        {
            return textValue + "L";
        }

        if (type == typeof(uint))
        {
            return textValue + "U";
        }

        if (type == typeof(ulong))
        {
            return textValue + "UL";
        }

        return textValue;
    }

    private static string? ConditionFor(Type type, int index)
    {
        if (Is(type, typeof(object)))
        {
            return null;
        }

        if (Is(type, typeof(string)) || Is(type, typeof(char)))
        {
            return $"global::LuaLoom.LuaBinding.IsString(L, {index})";
        }

        if (Is(type, typeof(bool)))
        {
            return $"global::LuaLoom.LuaBinding.IsBoolean(L, {index})";
        }

        if (Is(type, typeof(long))
            || Is(type, typeof(int))
            || Is(type, typeof(short))
            || Is(type, typeof(byte))
            || Is(type, typeof(ushort))
            || Is(type, typeof(sbyte))
            || type.IsEnum)
        {
            return $"global::LuaLoom.LuaBinding.IsInteger(L, {index})";
        }

        if (Is(type, typeof(double)) || Is(type, typeof(float)) || Is(type, typeof(decimal)))
        {
            return $"global::LuaLoom.LuaBinding.IsNumber(L, {index})";
        }

        if (Is(type, typeof(LuaTable)))
        {
            return $"global::LuaLoom.LuaBinding.IsTable(L, {index})";
        }

        if (Is(type, typeof(LuaFunction)))
        {
            return $"global::LuaLoom.LuaBinding.IsFunction(L, {index})";
        }

        if (type.IsClass || type.IsInterface)
        {
            return $"global::LuaLoom.LuaBinding.IsUserdataOfType<{TypeRef(type)}>(L, {index})";
        }

        return null;
    }

    private static int Specificity(MethodInfo method) =>
        method.GetParameters().Sum(static parameter => ParameterSpecificity(parameter.ParameterType));

    private static int ParameterSpecificity(Type type)
    {
        if (Is(type, typeof(string)) || Is(type, typeof(char)))
        {
            return 100;
        }

        if (Is(type, typeof(bool)))
        {
            return 90;
        }

        if (Is(type, typeof(long)))
        {
            return 80;
        }

        if (Is(type, typeof(int)))
        {
            return 70;
        }

        if (Is(type, typeof(double)))
        {
            return 60;
        }

        if (Is(type, typeof(float)))
        {
            return 50;
        }

        if (type.IsEnum)
        {
            return 40;
        }

        if (Is(type, typeof(LuaTable)) || Is(type, typeof(LuaFunction)))
        {
            return 35;
        }

        if (Is(type, typeof(object)))
        {
            return 0;
        }

        return type.IsClass || type.IsInterface ? 30 : 10;
    }

    private static void EmitMethodImplementation(
        StringBuilder builder,
        Type type,
        MethodInfo method,
        bool isStatic,
        string handler,
        int? providedCount = null)
    {
        var parameters = method.GetParameters();
        var count = providedCount ?? parameters.Length;
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

        var complex = HasComplexParameters(method);
        var arguments = new List<string>();
        if (complex)
        {
            var cursor = 0;
            if (parameters.Any(IsParamArray))
            {
                builder.AppendLine(
                    $"        var count = global::LuaLoom.LuaBinding.GetTop(L) - {argumentOffset - 1};");
            }

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (IsParamArray(parameter))
                {
                    var elementType = parameter.ParameterType.GetElementType()!;
                    builder.AppendLine($"        var a{i} = new {TypeRef(elementType)}[count - {cursor}];");
                    builder.AppendLine($"        for (var j = 0; j < a{i}.Length; j++)");
                    builder.AppendLine("        {");
                    builder.AppendLine(
                        $"            a{i}[j] = {ReadExpression(elementType, $"{argumentOffset + cursor} + j")};");
                    builder.AppendLine("        }");

                    arguments.Add($"a{i}");
                    continue;
                }

                if (parameter.ParameterType.IsByRef)
                {
                    var elementType = parameter.ParameterType.GetElementType()!;
                    if (parameter.IsOut)
                    {
                        builder.AppendLine($"        {TypeRef(elementType)} a{i};");
                        arguments.Add($"out a{i}");
                        continue;
                    }

                    builder.AppendLine($"        var a{i} = {ReadExpression(elementType, argumentOffset + cursor)};");
                    cursor++;
                    arguments.Add(parameter.IsIn ? $"in a{i}" : $"ref a{i}");
                    continue;
                }

                if (parameter.ParameterType.IsArray && !IsParamArray(parameter))
                {
                    var elementType = parameter.ParameterType.GetElementType()!;
                    builder.AppendLine(
                        $"        var a{i} = global::LuaLoom.LuaBinding.ReadArray<{TypeRef(elementType)}>(L, {argumentOffset + cursor});");
                    cursor++;
                    arguments.Add($"a{i}");
                    continue;
                }

                builder.AppendLine(
                    $"        var a{i} = {ReadExpression(parameter.ParameterType, argumentOffset + cursor)};");
                cursor++;
                arguments.Add($"a{i}");
            }
        }
        else
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i < count)
                {
                    builder.AppendLine($"        var a{i} = {ReadExpression(parameters[i].ParameterType, argumentOffset + i)};");
                    continue;
                }

                var defaultExpression = DefaultExpression(parameters[i]);
                if (defaultExpression == "null" && !parameters[i].ParameterType.IsValueType)
                {
                    builder.AppendLine($"        var a{i} = ({TypeRef(parameters[i].ParameterType)}?)null;");
                }
                else
                {
                    builder.AppendLine($"        var a{i} = {defaultExpression};");
                }
            }

            arguments.AddRange(Enumerable.Range(0, parameters.Length).Select(static i => "a" + i));
        }

        var invocation = isStatic
            ? $"{TypeRef(type)}.{method.Name}({string.Join(", ", arguments)})"
            : $"self.{method.Name}({string.Join(", ", arguments)})";

        var resultCount = 0;
        if (Is(method.ReturnType, typeof(void)))
        {
            builder.AppendLine($"        {invocation};");
        }
        else
        {
            builder.AppendLine($"        var result = {invocation};");
            builder.AppendLine($"        {PushStatement(method.ReturnType, "result")}");
            resultCount++;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (!parameter.ParameterType.IsByRef || (parameter.IsIn && !parameter.IsOut))
            {
                continue;
            }

            builder.AppendLine(
                $"        {PushStatement(parameter.ParameterType.GetElementType()!, $"a{i}")}");
            resultCount++;
        }

        builder.AppendLine($"        return {resultCount};");
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

    private static List<MethodInfo> SupportedMethods(
        Type type,
        BindingFlags flags,
        bool forBinding = true,
        bool includeBase = true)
    {
        var all = BindingFlags.Public | (includeBase ? BindingFlags.FlattenHierarchy : BindingFlags.DeclaredOnly);
        var methods = type.GetMethods(flags | all)
            .Where(static m => !m.IsSpecialName && !m.IsGenericMethodDefinition)
            .ToList();

        if (!forBinding)
        {
            return methods
                .Where(static m => IsSupportedSimpleMethod(m))
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

            if (overloads.Count > 1
                && overloads.Any(static m => HasComplexParameters(m)
                    || m.GetParameters().Any(static p => p.HasDefaultValue)))
            {
                continue;
            }

            if (overloads.Count == 1
                && HasComplexParameters(overloads[0])
                && overloads[0].GetParameters().Any(static p => p.HasDefaultValue))
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

    private static List<PropertyInfo> SupportedProperties(
        Type type,
        BindingFlags flags,
        bool forBinding = true,
        bool includeBase = true)
    {
        var all = BindingFlags.Public | (includeBase ? BindingFlags.FlattenHierarchy : BindingFlags.DeclaredOnly);
        var properties = type.GetProperties(flags | all)
            .Where(static p => p.GetIndexParameters().Length == 0 && p.GetMethod is not null)
            .Where(p => !forBinding
                || (!IsObsolete(p)
                    && !IsObsolete(p.GetMethod)
                    && (p.SetMethod is null || !IsObsolete(p.SetMethod))))
            .Where(static p => IsSupportedParameter(p.PropertyType));

        if (includeBase)
        {
            properties = properties
                .GroupBy(static p => p.Name, StringComparer.Ordinal)
                .Select(group => group
                    .OrderBy(p => InheritanceDistance(p.DeclaringType, type))
                    .First());
        }

        return properties
            .OrderBy(static p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static List<EventInfo> SupportedEvents(Type type, bool includeBase)
    {
        var all = BindingFlags.Public | BindingFlags.Instance
            | (includeBase ? BindingFlags.FlattenHierarchy : BindingFlags.DeclaredOnly);
        var events = type.GetEvents(all)
            .Where(static e => !IsObsolete(e))
            .Where(static e => e.EventHandlerType is not null && IsSupportedEventType(e.EventHandlerType))
            .ToList();

        if (includeBase)
        {
            events = events
                .GroupBy(static e => e.Name, StringComparer.Ordinal)
                .Select(group => group
                    .OrderBy(e => InheritanceDistance(e.DeclaringType, type))
                    .First())
                .ToList();
        }

        return events
            .OrderBy(static e => e.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsSupportedEventType(Type delegateType)
    {
        var invoke = delegateType.GetMethod("Invoke");
        if (invoke is null)
        {
            return false;
        }

        if (!Is(invoke.ReturnType, typeof(void)) && !IsSupportedParameter(invoke.ReturnType))
        {
            return false;
        }

        return invoke.GetParameters().All(static p =>
            !p.ParameterType.IsByRef
            && !p.ParameterType.IsArray
            && IsSupportedParameter(p.ParameterType));
    }

    private static string EventHandlerName(EventInfo eventInfo) =>
        "Event_" + eventInfo.Name;

    private static void EmitEventHandlers(
        StringBuilder builder,
        Type type,
        List<EventInfo> events)
    {
        foreach (var eventInfo in events)
        {
            var handlerType = eventInfo.EventHandlerType!;
            var invoke = handlerType.GetMethod("Invoke")!;
            var parameters = invoke.GetParameters();
            var argumentNames = Enumerable.Range(0, parameters.Length)
                .Select(static i => "a" + i)
                .ToArray();
            var call = $"function.Call({string.Join(", ", argumentNames)})";
            var lambda = Is(invoke.ReturnType, typeof(void))
                ? $"({string.Join(", ", argumentNames)}) => {{ {call}; }}"
                : $"({string.Join(", ", argumentNames)}) => {ConvertResult(invoke.ReturnType, call)}";
            var handlerName = EventHandlerName(eventInfo);

            builder.AppendLine();
            builder.AppendLine($"    private static int {handlerName}(global::System.IntPtr L)");
            builder.AppendLine("    {");
            builder.AppendLine($"        var self = ({TypeRef(type)})global::LuaLoom.LuaBinding.ReadObject(L, 1)!;");
            builder.AppendLine("        var op = global::LuaLoom.LuaBinding.ReadString(L, 2);");
            builder.AppendLine("        if (op == \"+\")");
            builder.AppendLine("        {");
            builder.AppendLine("            var function = global::LuaLoom.LuaBinding.CreateFunction(L, 3);");
            builder.AppendLine($"            var handler = new {TypeRef(handlerType)}({lambda});");
            builder.AppendLine(
                $"            global::LuaLoom.LuaEventBridge.Add(self, \"{eventInfo.Name}\", global::LuaLoom.LuaBinding.GetFunctionPointer(L, 3), handler, function);");
            builder.AppendLine($"            self.{eventInfo.Name} += handler;");
            builder.AppendLine("            return 0;");
            builder.AppendLine("        }");
            builder.AppendLine("        if (op == \"-\")");
            builder.AppendLine("        {");
            builder.AppendLine(
                $"            var handler = global::LuaLoom.LuaEventBridge.Remove(self, \"{eventInfo.Name}\", global::LuaLoom.LuaBinding.GetFunctionPointer(L, 3));");
            builder.AppendLine($"            if (handler is {TypeRef(handlerType)} typed)");
            builder.AppendLine("            {");
            builder.AppendLine($"                self.{eventInfo.Name} -= typed;");
            builder.AppendLine("            }");
            builder.AppendLine("            return 0;");
            builder.AppendLine("        }");
            builder.AppendLine(
                $"        global::LuaLoom.LuaBinding.Throw(L, \"Event operator must be '+' or '-'.\");");
            builder.AppendLine("        return 0;");
            builder.AppendLine("    }");
        }
    }

    private static List<FieldInfo> SupportedFields(
        Type type,
        BindingFlags flags,
        bool includeBase = true)
    {
        var all = BindingFlags.Public | (includeBase ? BindingFlags.FlattenHierarchy : BindingFlags.DeclaredOnly);
        var fields = type.GetFields(flags | all)
            .Where(static f => !IsObsolete(f))
            .Where(static f => IsSupportedParameter(f.FieldType));

        if (includeBase)
        {
            fields = fields
                .GroupBy(static f => f.Name, StringComparer.Ordinal)
                .Select(group => group
                    .OrderBy(f => InheritanceDistance(f.DeclaringType, type))
                    .First());
        }

        return fields
            .OrderBy(static f => f.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static int InheritanceDistance(Type? declaringType, Type type)
    {
        var distance = 0;
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current == declaringType)
            {
                return distance;
            }

            distance++;
        }

        return int.MaxValue;
    }

    private static bool IsSupportedMethod(MethodInfo method)
    {
        foreach (var parameter in method.GetParameters())
        {
            if (parameter.ParameterType.IsByRef)
            {
                if (!IsSupportedParameter(parameter.ParameterType.GetElementType()!))
                {
                    return false;
                }

                continue;
            }

            if (parameter.ParameterType.IsArray)
            {
                if (!IsSupportedParameter(parameter.ParameterType))
                {
                    return false;
                }

                continue;
            }

            if (!IsSupportedParameter(parameter.ParameterType))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSupportedSimpleMethod(MethodInfo method) =>
        method.GetParameters().All(static p =>
            !p.ParameterType.IsByRef
            && !p.ParameterType.IsArray
            && IsSupportedParameter(p.ParameterType));

    private static bool HasComplexParameters(MethodInfo method) =>
        method.GetParameters().Any(static p =>
            p.ParameterType.IsByRef || p.ParameterType.IsArray);

    private static bool IsParamArray(ParameterInfo parameter) =>
        parameter.ParameterType.IsArray
        && parameter.GetCustomAttributesData()
            .Any(static attribute => attribute.AttributeType.FullName
                == "System.ParamArrayAttribute");

    private static bool IsObsolete(MemberInfo? member) =>
        member is not null
        && member.GetCustomAttributesData()
            .Any(static attribute => attribute.AttributeType.FullName == typeof(ObsoleteAttribute).FullName);

    private static bool IsSupportedParameter(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsGenericParameter || type.IsGenericType)
        {
            return false;
        }

        if (type.IsArray)
        {
            return type.GetArrayRank() == 1
                && IsSupportedArrayElement(type.GetElementType()!);
        }

        return type.IsPrimitive
            || type.IsEnum
            || Is(type, typeof(string))
            || type.IsClass
            || type.IsValueType;
    }

    private static bool IsSupportedArrayElement(Type type) =>
        type.IsEnum
        || Is(type, typeof(bool))
        || Is(type, typeof(byte))
        || Is(type, typeof(sbyte))
        || Is(type, typeof(short))
        || Is(type, typeof(ushort))
        || Is(type, typeof(int))
        || Is(type, typeof(uint))
        || Is(type, typeof(long))
        || Is(type, typeof(ulong))
        || Is(type, typeof(float))
        || Is(type, typeof(double))
        || Is(type, typeof(string))
        || Is(type, typeof(object))
        || Is(type, typeof(LuaTable))
        || Is(type, typeof(LuaFunction));

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

    private static string ReadExpression(Type type, int index) =>
        ReadExpression(type, index.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string ReadExpression(Type type, string index)
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

        if (type.IsArray && type.GetArrayRank() == 1)
        {
            return $"global::LuaLoom.LuaBinding.ReadArray<{TypeRef(type.GetElementType()!)}>(L, {index})";
        }

        if (type.IsEnum)
        {
            return $"({TypeRef(type)})global::LuaLoom.LuaBinding.ReadLong(L, {index})";
        }

        if (Is(type, typeof(object)))
        {
            return $"global::LuaLoom.LuaBinding.ReadObject(L, {index})";
        }

        return $"global::LuaLoom.LuaBinding.ReadObjectAs<{TypeRef(type)}>(L, {index})";
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

        if (type.IsArray && type.GetArrayRank() == 1)
        {
            return $"global::LuaLoom.LuaBinding.PushArray<{TypeRef(type.GetElementType()!)}>(L, {expression});";
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

    private static bool IsExtensionMethod(MethodInfo method)
    {
        if (!method.IsStatic || method.GetParameters().Length == 0)
        {
            return false;
        }

        return method.GetCustomAttributesData()
            .Any(static attribute => attribute.AttributeType.FullName
                == typeof(System.Runtime.CompilerServices.ExtensionAttribute).FullName);
    }

    private static void EmitExtensionRegistration(
        StringBuilder builder,
        IReadOnlyList<MethodInfo> methods)
    {
        builder.AppendLine();
        builder.AppendLine("    public static void RegisterExtensions(global::LuaLoom.LuaEnv env)");
        builder.AppendLine("    {");

        foreach (var group in methods.GroupBy(static m => m.Name, StringComparer.Ordinal))
        {
            var method = group.First();
            var target = method.GetParameters()[0].ParameterType;
            builder.AppendLine(
                $"        env.RegisterExtensionMethod(typeof({TypeRef(target)}), \"{method.Name}\", {HandlerBase(method.Name, isStatic: true)});");
        }

        builder.AppendLine("    }");
    }

    private static bool IsExtensionClass(Type type)
    {
        if (!type.IsAbstract || !type.IsSealed)
        {
            return false;
        }

        return type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Any(method => method.GetCustomAttributesData()
                .Any(attribute => attribute.AttributeType.FullName
                    == typeof(System.Runtime.CompilerServices.ExtensionAttribute).FullName));
    }
}
