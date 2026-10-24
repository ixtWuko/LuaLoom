using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using LuaLoom.Internal;
using LuaLoom.Native;
using NativeMarshal = System.Runtime.InteropServices.Marshal;

namespace LuaLoom;

/// <summary>Lua 调 C# 的桥：CS 命名空间树、构造与成员访问、反射兜底。</summary>
internal static class Interop
{
    private const string Bootstrap = """
        local cs = {}
        local mt = {}
        local type_lookup = lua_loom_type

        mt.__index = function(self, key)
            local fqn = rawget(self, '__fqn')
            local child = fqn and (fqn .. '.' .. key) or key
            local class = type_lookup(child)
            if class ~= nil then
                rawset(self, key, class)
                return class
            end
            local ns = { __fqn = child }
            setmetatable(ns, mt)
            rawset(self, key, ns)
            return ns
        end

        mt.__newindex = function(self, key)
            error('No such type: ' .. tostring(rawget(self, '__fqn')) .. '.' .. tostring(key), 2)
        end

        CS = setmetatable({}, mt)
        """;

    private static readonly ConcurrentDictionary<IntPtr, LuaEnv> Environments = new();
    private static readonly ConcurrentDictionary<string, Type?> TypeCache = new();
    private static readonly ConcurrentDictionary<(Type Type, string Name), (bool Found, MethodInfo? Method)> ExtensionCache = new();

    private static readonly (string LuaName, string MethodName)[] Operators =
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

    private static readonly LuaCSFunction OperatorCallback = OnOperator;
    private static readonly LuaCSFunction EventCallback = OnEvent;
    private static readonly IntPtr EventPointer = NativeMarshal.GetFunctionPointerForDelegate(EventCallback);

    private static readonly LuaCSFunction GenericFactoryCallback = OnGenericFactory;
    private static readonly IntPtr GenericFactoryPointer = NativeMarshal.GetFunctionPointerForDelegate(GenericFactoryCallback);
    private static readonly LuaCSFunction GenericInvokeCallback = OnGenericInvoke;
    private static readonly IntPtr GenericInvokePointer = NativeMarshal.GetFunctionPointerForDelegate(GenericInvokeCallback);

    private static readonly ConditionalWeakTable<object, EventSubscriptions> Events = new();

    private static readonly LuaCSFunction TypeCallback = OnType;
    private static readonly LuaCSFunction ConstructCallback = OnConstruct;
    private static readonly LuaCSFunction StaticIndexCallback = OnStaticIndex;
    private static readonly LuaCSFunction StaticSetCallback = OnStaticSet;
    private static readonly LuaCSFunction InstanceIndexCallback = OnInstanceIndex;
    private static readonly LuaCSFunction InstanceNewIndexCallback = OnInstanceNewIndex;
    private static readonly LuaCSFunction StaticMethodCallback = OnStaticMethod;
    private static readonly LuaCSFunction InstanceMethodCallback = OnInstanceMethod;

    private static readonly IntPtr TypePointer = NativeMarshal.GetFunctionPointerForDelegate(TypeCallback);
    private static readonly IntPtr ConstructPointer = NativeMarshal.GetFunctionPointerForDelegate(ConstructCallback);
    private static readonly IntPtr StaticIndexPointer = NativeMarshal.GetFunctionPointerForDelegate(StaticIndexCallback);
    private static readonly IntPtr StaticSetPointer = NativeMarshal.GetFunctionPointerForDelegate(StaticSetCallback);
    private static readonly IntPtr StaticMethodPointer = NativeMarshal.GetFunctionPointerForDelegate(StaticMethodCallback);
    private static readonly IntPtr InstanceMethodPointer = NativeMarshal.GetFunctionPointerForDelegate(InstanceMethodCallback);

    static Interop()
    {
        LuaMarshal.BridgeReader = ReadBridge;
    }

    public static void Install(LuaEnv environment)
    {
        Environments[environment.Handle] = environment;
        environment.Marshal.SetObjectHandlers(InstanceIndexCallback, InstanceNewIndexCallback);

        foreach (var (luaName, methodName) in Operators)
        {
            environment.Marshal.SetObjectMetamethod(luaName, OperatorCallback, methodName);
        }

        var state = environment.Handle;
        LuaApi.lua_pushcclosure(state, TypePointer, 0);
        LuaApi.lua_setglobal(state, "lua_loom_type");
        LuaApi.lua_pushcclosure(state, GenericFactoryPointer, 0);
        LuaApi.lua_setglobal(state, "lua_loom_generic");

        environment.DoString(Bootstrap, "=lua_loom_cs");
    }

    public static void Uninstall(IntPtr state) => Environments.TryRemove(state, out _);

    private static object? ReadBridge(IntPtr state, Type type, int index)
    {
        var environment = GetEnvironment(state);

        if (typeof(Delegate).IsAssignableFrom(type))
        {
            if (!environment.TryGetDelegate(type, out var invoker))
            {
                throw new LuaConversionException(
                    $"{type.Name} (add it to csharpCallLua and re-run lua-loom-gen)",
                    DescribeLuaType(state, index),
                    index);
            }

            var function = environment.Marshal.CreateFunction(index);
            return invoker.Create(function);
        }

        if (type.IsInterface)
        {
            var table = environment.Marshal.CreateTableFrom(index);
            if (environment.TryGetInterfaceFactory(type, out var factory))
            {
                return factory(table);
            }

            if (!environment.Options.EnableReflectionFallback)
            {
                throw new LuaConversionException(
                    $"{type.Name} (register the interface binding or enable EnableReflectionFallback)",
                    DescribeLuaType(state, index),
                    index);
            }

            var proxy = DispatchProxy.Create(type, typeof(LuaInterfaceProxy));
            ((LuaInterfaceProxy)proxy).Initialize(table);
            return proxy;
        }

        return null;
    }

    private static int OnType(IntPtr state) => Guarded(state, static s =>
    {
        var fullName = ReadString(s, 1);
        var environment = GetEnvironment(s);
        var hasBinding = environment.TryGetTypeBinding(fullName, out var binding);

        var type = hasBinding ? binding.Type : ResolveType(fullName);
        if (type is null)
        {
            LuaApi.lua_pushnil(s);
            return 1;
        }

        if (!hasBinding && !environment.Options.EnableReflectionFallback)
        {
            throw new InvalidOperationException(
                $"Type '{fullName}' is not registered. Add it to luaCallCSharp or enable EnableReflectionFallback.");
        }

        CreateClassTable(s, fullName);
        return 1;
    });

    private static int OnConstruct(IntPtr state) => Guarded(state, static s =>
    {
        var environment = GetEnvironment(s);
        var fullName = GetUpvalueString(s, 1);
        var hasBinding = environment.TryGetTypeBinding(fullName, out var binding);
        if (hasBinding && binding.Call is not null)
        {
            var top = LuaApi.lua_gettop(s);
            var results = binding.Call(s);
            if (results > 0 || LuaApi.lua_gettop(s) > top)
            {
                return results;
            }
        }

        var type = hasBinding ? binding.Type : ResolveType(fullName);
        if (type is null)
        {
            throw new InvalidOperationException("Type not found.");
        }

        var values = ReadArguments(environment, s, 2, LuaApi.lua_gettop(s));
        if (!TrySelectConstructor(type, values, out var constructor, out var args))
        {
            throw new MissingMethodException(
                $"No matching constructor for '{type.Name}' with {values.Length} argument(s).");
        }

        var instance = constructor.Invoke(args);
        environment.Marshal.PushObject(instance);
        return 1 + PushByRefResults(environment, constructor.GetParameters(), args);
    });

    private static int OnStaticIndex(IntPtr state) => Guarded(state, static s =>
    {
        var environment = GetEnvironment(s);
        var fullName = GetUpvalueString(s, 1);

        if (environment.TryGetTypeBinding(fullName, out var binding) && binding.StaticIndex is not null)
        {
            var top = LuaApi.lua_gettop(s);
            var results = binding.StaticIndex(s);
            if (results > 0 || LuaApi.lua_gettop(s) > top)
            {
                return results;
            }
        }

        var type = ResolveType(fullName) ?? throw new InvalidOperationException("Type not found.");
        var name = ReadString(s, 2);

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
        var property = type.GetProperty(name, flags);
        if (property is not null)
        {
            environment.Marshal.PushObject(property.GetValue(null));
            return 1;
        }

        var field = type.GetField(name, flags);
        if (field is not null)
        {
            environment.Marshal.PushObject(field.GetValue(null));
            return 1;
        }

        var method = type.GetMethods(flags).FirstOrDefault(m => m.Name == name);
        if (method is not null)
        {
            LuaApi.lua_pushstring(s, fullName);
            LuaApi.lua_pushstring(s, name);
            LuaApi.lua_pushcclosure(s, StaticMethodPointer, 2);
            return 1;
        }

        LuaApi.lua_pushnil(s);
        return 1;
    });

    private static int OnStaticSet(IntPtr state) => Guarded(state, static s =>
    {
        var environment = GetEnvironment(s);
        var fullName = GetUpvalueString(s, 1);

        if (environment.TryGetTypeBinding(fullName, out var binding) && binding.StaticNewIndex is not null)
        {
            var top = LuaApi.lua_gettop(s);
            var results = binding.StaticNewIndex(s);
            if (results == 0)
            {
                return 0;
            }

            LuaApi.lua_settop(s, top);
        }

        var type = ResolveType(fullName) ?? throw new InvalidOperationException("Type not found.");
        var name = ReadString(s, 2);
        var value = environment.Marshal.ReadObject(3);

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
        var property = type.GetProperty(name, flags);
        if (property is not null)
        {
            property.SetValue(null, ConvertValue(value, property.PropertyType));
            return 0;
        }

        var field = type.GetField(name, flags)
            ?? throw new MissingMemberException(type.FullName, name);
        field.SetValue(null, ConvertValue(value, field.FieldType));
        return 0;
    });

    private static int OnInstanceIndex(IntPtr state) => Guarded(state, static s =>
    {
        var environment = GetEnvironment(s);
        var instance = environment.Marshal.GetObjectFromUserdata(1)
            ?? throw new InvalidOperationException("Lua value is not a C# object.");

        if (LuaApi.lua_type(s, 2) != LuaApi.LuaTString)
        {
            return ReadIndexer(environment, s, instance);
        }

        if (environment.TryGetTypeBinding(instance.GetType(), out var binding) && binding.InstanceIndex is not null)
        {
            var top = LuaApi.lua_gettop(s);
            var results = binding.InstanceIndex(s);
            if (results > 0 || LuaApi.lua_gettop(s) > top)
            {
                return results;
            }
        }

        var name = ReadString(s, 2);
        var type = instance.GetType();

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        var property = type.GetProperty(name, flags);
        if (property is not null)
        {
            environment.Marshal.PushObject(property.GetValue(instance));
            return 1;
        }

        var field = type.GetField(name, flags);
        if (field is not null)
        {
            environment.Marshal.PushObject(field.GetValue(instance));
            return 1;
        }

        var method = type.GetMethods(flags).FirstOrDefault(m => m.Name == name);
        if (method is not null)
        {
            LuaApi.lua_pushstring(s, name);
            LuaApi.lua_pushcclosure(s, InstanceMethodPointer, 1);
            return 1;
        }

        var eventInfo = type.GetEvent(name, flags);
        if (eventInfo is not null)
        {
            LuaApi.lua_pushstring(s, name);
            LuaApi.lua_pushcclosure(s, EventPointer, 1);
            return 1;
        }

        var extension = FindExtensionMethod(type, name);
        if (extension is not null)
        {
            LuaApi.lua_pushstring(s, extension.DeclaringType!.FullName!);
            LuaApi.lua_pushstring(s, name);
            LuaApi.lua_pushcclosure(s, StaticMethodPointer, 2);
            return 1;
        }

        LuaApi.lua_pushnil(s);
        return 1;
    });

    private static int OnInstanceNewIndex(IntPtr state) => Guarded(state, static s =>
    {
        var environment = GetEnvironment(s);
        var instance = environment.Marshal.GetObjectFromUserdata(1)
            ?? throw new InvalidOperationException("Lua value is not a C# object.");

        if (LuaApi.lua_type(s, 2) != LuaApi.LuaTString)
        {
            return WriteIndexer(environment, s, instance);
        }

        if (environment.TryGetTypeBinding(instance.GetType(), out var binding) && binding.InstanceNewIndex is not null)
        {
            var top = LuaApi.lua_gettop(s);
            var results = binding.InstanceNewIndex(s);
            if (results == 0)
            {
                return 0;
            }

            LuaApi.lua_settop(s, top);
        }

        var name = ReadString(s, 2);
        var value = environment.Marshal.ReadObject(3);
        var type = instance.GetType();

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        var property = type.GetProperty(name, flags);
        if (property is not null)
        {
            property.SetValue(instance, ConvertValue(value, property.PropertyType));
            return 0;
        }

        var field = type.GetField(name, flags)
            ?? throw new MissingMemberException(type.FullName, name);
        field.SetValue(instance, ConvertValue(value, field.FieldType));
        return 0;
    });

    private static int OnStaticMethod(IntPtr state) => Guarded(state, static s =>
    {
        var environment = GetEnvironment(s);
        var type = ResolveType(GetUpvalueString(s, 1)) ?? throw new InvalidOperationException("Type not found.");
        var name = GetUpvalueString(s, 2);
        var values = ReadArguments(environment, s, 1, LuaApi.lua_gettop(s));

        if (!TrySelectMethod(type, name, values, isStatic: true, out var method, out var args))
        {
            throw new MissingMethodException($"No matching static method '{type.Name}.{name}'.");
        }

        var result = Invoke(method, null, args);
        return PushReturnAndByRefResults(environment, method, result, args);
    });

    private static int OnInstanceMethod(IntPtr state) => Guarded(state, static s =>
    {
        var environment = GetEnvironment(s);
        var instance = environment.Marshal.GetObjectFromUserdata(1)
            ?? throw new InvalidOperationException("Lua value is not a C# object.");
        var name = GetUpvalueString(s, 1);
        var values = ReadArguments(environment, s, 2, LuaApi.lua_gettop(s));

        if (!TrySelectMethod(instance.GetType(), name, values, isStatic: false, out var method, out var args))
        {
            throw new MissingMethodException($"No matching method '{instance.GetType().Name}.{name}'.");
        }

        var result = Invoke(method, instance, args);
        return PushReturnAndByRefResults(environment, method, result, args);
    });

    private static int OnOperator(IntPtr state) => Guarded(state, static s =>
    {
        var environment = GetEnvironment(s);
        var methodName = GetUpvalueString(s, 1);
        var argumentCount = LuaApi.lua_gettop(s);
        var left = environment.Marshal.ReadObject(1);
        var right = argumentCount >= 2 ? environment.Marshal.ReadObject(2) : null;

        if (left is null)
        {
            if (methodName == "op_Equality")
            {
                LuaApi.lua_pushboolean(s, right is null ? 1 : 0);
                return 1;
            }

            LuaApi.lua_pushnil(s);
            return 1;
        }

        var type = left.GetType();
        var isUnary = methodName == "op_UnaryNegation";
        var parameterCount = isUnary ? 1 : argumentCount;
        var method = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == parameterCount);

        if (method is null)
        {
            if (methodName == "op_Equality")
            {
                LuaApi.lua_pushboolean(s, ReferenceEquals(left, right) ? 1 : 0);
                return 1;
            }

            throw new MissingMethodException($"C# type '{type.FullName}' does not define '{methodName}'.");
        }

        var parameters = method.GetParameters();
        var arguments = new object?[parameterCount];
        arguments[0] = ConvertValue(left, parameters[0].ParameterType);
        if (parameterCount >= 2)
        {
            arguments[1] = ConvertValue(right, parameters[1].ParameterType);
        }

        var result = Invoke(method, null, arguments);
        if (methodName == "op_Equality")
        {
            LuaApi.lua_pushboolean(s, result is true ? 1 : 0);
            return 1;
        }

        environment.Marshal.PushObject(result);
        return 1;
    });

    private static int ReadIndexer(LuaEnv environment, IntPtr state, object instance)
    {
        var indexer = IndexerOf(instance.GetType());
        if (indexer is null)
        {
            LuaApi.lua_pushnil(state);
            return 1;
        }

        var key = environment.Marshal.ReadObject(2);
        var value = indexer.GetValue(
            instance,
            [ConvertValue(key, indexer.GetIndexParameters()[0].ParameterType)]);
        environment.Marshal.PushObject(value);
        return 1;
    }

    private static int WriteIndexer(LuaEnv environment, IntPtr state, object instance)
    {
        var indexer = IndexerOf(instance.GetType());
        if (indexer is null)
        {
            throw new MissingMemberException(instance.GetType().FullName, "Item");
        }

        var key = environment.Marshal.ReadObject(2);
        var value = environment.Marshal.ReadObject(3);
        indexer.SetValue(
            instance,
            ConvertValue(value, indexer.PropertyType),
            [ConvertValue(key, indexer.GetIndexParameters()[0].ParameterType)]);
        return 0;
    }

    private static PropertyInfo? IndexerOf(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(static p => p.GetIndexParameters().Length == 1);

    private static MethodInfo? FindExtensionMethod(Type type, string name)
    {
        var key = (type, name);
        if (ExtensionCache.TryGetValue(key, out var cached))
        {
            return cached.Found ? cached.Method : null;
        }

        var method = SearchExtensionMethod(type, name);
        ExtensionCache[key] = (method is not null, method);
        return method;
    }

    private static MethodInfo? SearchExtensionMethod(Type type, string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(static t => t is not null).Cast<Type>().ToArray();
            }

            foreach (var candidate in types
                .Where(static t => t.IsAbstract && t.IsSealed)
                .OrderBy(static t => t.FullName, StringComparer.Ordinal))
            {
                foreach (var method in candidate
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .OrderBy(static m => m.Name, StringComparer.Ordinal)
                    .ThenBy(static m => m.GetParameters().Length))
                {
                    if (method.Name != name
                        || method.IsGenericMethodDefinition
                        || !method.IsDefined(typeof(ExtensionAttribute), inherit: false))
                    {
                        continue;
                    }

                    var parameters = method.GetParameters();
                    if (parameters.Length == 0
                        || !parameters[0].ParameterType.IsAssignableFrom(type))
                    {
                        continue;
                    }

                    return method;
                }
            }
        }

        return null;
    }

    private static int OnEvent(IntPtr state) => Guarded(state, static s =>
    {
        var environment = GetEnvironment(s);
        var name = GetUpvalueString(s, 1);
        var instance = environment.Marshal.GetObjectFromUserdata(1)
            ?? throw new InvalidOperationException("Lua value is not a C# object.");
        var operation = ReadString(s, 2);
        var eventInfo = instance.GetType().GetEvent(name, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMemberException(instance.GetType().FullName, name);

        if (LuaApi.lua_type(s, 3) != LuaApi.LuaTFunction)
        {
            throw new ArgumentException($"Event '{name}' expects a function as the second argument.");
        }

        var pointer = LuaApi.lua_topointer(s, 3);
        var subscriptions = Events.GetOrCreateValue(instance);

        lock (subscriptions)
        {
            var handlers = subscriptions.For(name);

            if (operation == "+")
            {
                var function = environment.Marshal.CreateFunction(3);
                try
                {
                    var handler = LuaEventDelegateFactory.Create(eventInfo.EventHandlerType!, function);
                    handlers[pointer] = (handler, function);
                    eventInfo.AddEventHandler(instance, handler);
                }
                catch
                {
                    function.Dispose();
                    throw;
                }

                return 0;
            }

            if (operation == "-")
            {
                if (handlers.Remove(pointer, out var entry))
                {
                    eventInfo.RemoveEventHandler(instance, entry.Handler);
                    entry.Function.Dispose();
                }

                return 0;
            }

            throw new ArgumentException("Event operator must be '+' or '-'.");
        }
    });

    private static int OnGenericFactory(IntPtr state) => Guarded(state, static s =>
    {
        var typeName = ReadTypeName(s, 1);
        var methodName = ReadString(s, 2);
        var count = LuaApi.lua_gettop(s) - 2;
        if (count <= 0)
        {
            throw new ArgumentException("lua_loom_generic requires at least one type argument.");
        }

        var names = new string[count];
        for (var i = 0; i < count; i++)
        {
            names[i] = ReadTypeName(s, 3 + i);
        }

        LuaApi.lua_pushstring(s, typeName);
        LuaApi.lua_pushstring(s, methodName);
        LuaApi.lua_pushstring(s, string.Join('|', names));
        LuaApi.lua_pushcclosure(s, GenericInvokePointer, 3);
        return 1;
    });

    private static int OnGenericInvoke(IntPtr state) => Guarded(state, static s =>
    {
        var environment = GetEnvironment(s);
        var type = ResolveType(GetUpvalueString(s, 1))
            ?? throw new InvalidOperationException("Type not found.");
        var methodName = GetUpvalueString(s, 2);
        var typeNames = GetUpvalueString(s, 3).Split('|');

        var genericArguments = new Type[typeNames.Length];
        for (var i = 0; i < typeNames.Length; i++)
        {
            genericArguments[i] = ResolveType(typeNames[i])
                ?? throw new InvalidOperationException($"Generic argument '{typeNames[i]}' not found.");
        }

        var definition = type
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == methodName
                && m.IsGenericMethodDefinition
                && m.GetGenericArguments().Length == genericArguments.Length)
            ?? throw new MissingMethodException(
                $"No generic method '{type.Name}.{methodName}' with {genericArguments.Length} type argument(s).");

        var method = definition.MakeGenericMethod(genericArguments);

        if (method.IsStatic)
        {
            var values = ReadArguments(environment, s, 1, LuaApi.lua_gettop(s));
            if (!TryBindArguments(values, method.GetParameters(), out var arguments, out _))
            {
                throw new ArgumentException($"No matching arguments for generic method '{type.Name}.{methodName}'.");
            }

            var result = Invoke(method, null, arguments);
            return PushReturnAndByRefResults(environment, method, result, arguments);
        }
        else
        {
            var target = environment.Marshal.GetObjectFromUserdata(1)
                ?? throw new InvalidOperationException("Lua value is not a C# object.");
            var values = ReadArguments(environment, s, 2, LuaApi.lua_gettop(s));
            if (!TryBindArguments(values, method.GetParameters(), out var arguments, out _))
            {
                throw new ArgumentException($"No matching arguments for generic method '{type.Name}.{methodName}'.");
            }

            var result = Invoke(method, target, arguments);
            return PushReturnAndByRefResults(environment, method, result, arguments);
        }
    });

    private static string ReadTypeName(IntPtr state, int index)
    {
        if (LuaApi.lua_type(state, index) == LuaApi.LuaTString)
        {
            return ReadString(state, index);
        }

        LuaApi.lua_getfield(state, index, "__fqn");
        var name = ReadString(state, -1);
        LuaApi.lua_settop(state, LuaApi.lua_gettop(state) - 1);

        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Expected a C# type table or type name.");
        }

        return name;
    }

    private sealed class EventSubscriptions
    {
        private readonly Dictionary<string, Dictionary<IntPtr, (Delegate Handler, LuaFunction Function)>> _byEvent = new();

        public Dictionary<IntPtr, (Delegate Handler, LuaFunction Function)> For(string name)
        {
            if (!_byEvent.TryGetValue(name, out var handlers))
            {
                handlers = new Dictionary<IntPtr, (Delegate, LuaFunction)>();
                _byEvent[name] = handlers;
            }

            return handlers;
        }
    }

    private static int PushReturnAndByRefResults(
        LuaEnv environment,
        MethodInfo method,
        object? result,
        object?[] arguments)
    {
        var count = 0;
        if (method.ReturnType != typeof(void))
        {
            environment.Marshal.PushObject(result);
            count++;
        }

        return count + PushByRefResults(environment, method.GetParameters(), arguments);
    }

    private static int PushByRefResults(
        LuaEnv environment,
        ParameterInfo[] parameters,
        object?[] arguments)
    {
        var count = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (!parameter.ParameterType.IsByRef || (parameter.IsIn && !parameter.IsOut))
            {
                continue;
            }

            environment.Marshal.PushObject(arguments[i]);
            count++;
        }

        return count;
    }

    private static object? Invoke(MethodInfo method, object? target, object?[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    private static void CreateClassTable(IntPtr state, string fullName)
    {
        LuaApi.lua_createtable(state, 0, 0);
        LuaApi.lua_pushstring(state, fullName);
        LuaApi.lua_setfield(state, -2, "__fqn");

        LuaApi.lua_createtable(state, 0, 3);

        LuaApi.lua_pushstring(state, fullName);
        LuaApi.lua_pushcclosure(state, ConstructPointer, 1);
        LuaApi.lua_setfield(state, -2, "__call");

        LuaApi.lua_pushstring(state, fullName);
        LuaApi.lua_pushcclosure(state, StaticIndexPointer, 1);
        LuaApi.lua_setfield(state, -2, "__index");

        LuaApi.lua_pushstring(state, fullName);
        LuaApi.lua_pushcclosure(state, StaticSetPointer, 1);
        LuaApi.lua_setfield(state, -2, "__newindex");

        LuaApi.lua_setmetatable(state, -2);
    }

    private static object?[] ReadArguments(LuaEnv environment, IntPtr state, int first, int last)
    {
        if (last < first)
        {
            return [];
        }

        var arguments = new object?[last - first + 1];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = environment.Marshal.ReadObject(first + i);
        }

        return arguments;
    }

    private static bool TrySelectConstructor(
        Type type,
        object?[] values,
        out ConstructorInfo constructor,
        out object?[] arguments)
    {
        constructor = null!;
        arguments = [];
        var bestScore = int.MinValue;

        foreach (var candidate in type.GetConstructors())
        {
            if (TryBindArguments(values, candidate.GetParameters(), out var candidateArguments, out var score)
                && score > bestScore)
            {
                constructor = candidate;
                arguments = candidateArguments;
                bestScore = score;
            }
        }

        return bestScore != int.MinValue;
    }

    private static bool TrySelectMethod(
        Type type,
        string name,
        object?[] values,
        bool isStatic,
        out MethodInfo method,
        out object?[] arguments)
    {
        var flags = BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        method = null!;
        arguments = [];
        var bestScore = int.MinValue;

        foreach (var candidate in type.GetMethods(flags))
        {
            if (candidate.Name != name || candidate.IsGenericMethodDefinition)
            {
                continue;
            }

            if (TryBindArguments(values, candidate.GetParameters(), out var candidateArguments, out var score)
                && score > bestScore)
            {
                method = candidate;
                arguments = candidateArguments;
                bestScore = score;
            }
        }

        return bestScore != int.MinValue;
    }

    private static bool TryBindArguments(
        object?[] values,
        ParameterInfo[] parameters,
        out object?[] arguments,
        out int score)
    {
        arguments = new object?[parameters.Length];
        score = 0;
        var valueIndex = 0;

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var parameterType = parameter.ParameterType;

            if (parameterType.IsByRef)
            {
                var elementType = parameterType.GetElementType()!;
                if (parameter.IsOut)
                {
                    arguments[i] = elementType.IsValueType
                        ? Activator.CreateInstance(elementType)
                        : null;
                    continue;
                }

                if (valueIndex >= values.Length
                    || !TryConvert(values[valueIndex], elementType, out arguments[i]))
                {
                    return false;
                }

                score += ScoreValue(values[valueIndex], elementType);
                valueIndex++;
                continue;
            }

            if (IsParamArray(parameter))
            {
                var elementType = parameterType.GetElementType()!;
                var remaining = values.Length - valueIndex;
                var array = Array.CreateInstance(elementType, remaining);
                for (var j = 0; j < remaining; j++)
                {
                    if (!TryConvert(values[valueIndex + j], elementType, out var item))
                    {
                        return false;
                    }

                    score += ScoreValue(values[valueIndex + j], elementType);
                    try
                    {
                        array.SetValue(item, j);
                    }
                    catch (Exception exception) when (exception is InvalidCastException or ArgumentException)
                    {
                        return false;
                    }
                }

                arguments[i] = array;
                valueIndex = values.Length;
                continue;
            }

            if (valueIndex >= values.Length)
            {
                if (!parameter.HasDefaultValue)
                {
                    return false;
                }

                arguments[i] = parameter.DefaultValue;
                continue;
            }

            if (!TryConvert(values[valueIndex], parameterType, out arguments[i]))
            {
                return false;
            }

            score += ScoreValue(values[valueIndex], parameterType);
            valueIndex++;
        }

        return valueIndex == values.Length;
    }

    private static int ScoreValue(object? value, Type target)
    {
        var underlying = Nullable.GetUnderlyingType(target) ?? target;

        if (value is null)
        {
            if (underlying == typeof(object))
            {
                return 1;
            }

            return target.IsValueType && Nullable.GetUnderlyingType(target) is null ? 0 : 3;
        }

        var actual = value.GetType();
        if (actual == target || actual == underlying)
        {
            return 5;
        }

        if (underlying == typeof(object))
        {
            return 1;
        }

        if (target.IsAssignableFrom(actual))
        {
            return 4;
        }

        if (underlying.IsEnum)
        {
            return value is string ? 2 : 3;
        }

        if (underlying == typeof(bool))
        {
            return 1;
        }

        if (underlying == typeof(string))
        {
            return 2;
        }

        if (IsNumeric(underlying) && IsNumeric(actual))
        {
            if (actual == typeof(long))
            {
                if (underlying == typeof(long) || underlying == typeof(ulong))
                {
                    return 5;
                }

                return IsIntegral(underlying) ? 4 : 2;
            }

            if (actual == typeof(double))
            {
                if (underlying == typeof(double))
                {
                    return 5;
                }

                if (underlying == typeof(float))
                {
                    return 4;
                }

                return IsIntegral(underlying) ? 1 : 3;
            }

            return 2;
        }

        return 1;
    }

    private static bool IsNumeric(Type type) =>
        type == typeof(byte)
        || type == typeof(sbyte)
        || type == typeof(short)
        || type == typeof(ushort)
        || type == typeof(int)
        || type == typeof(uint)
        || type == typeof(long)
        || type == typeof(ulong)
        || type == typeof(float)
        || type == typeof(double)
        || type == typeof(decimal);

    private static bool IsIntegral(Type type) =>
        type == typeof(byte)
        || type == typeof(sbyte)
        || type == typeof(short)
        || type == typeof(ushort)
        || type == typeof(int)
        || type == typeof(uint)
        || type == typeof(long)
        || type == typeof(ulong);

    private static bool IsParamArray(ParameterInfo parameter) =>
        parameter.ParameterType.IsArray
        && parameter.GetCustomAttributes(typeof(ParamArrayAttribute), inherit: false).Length > 0;

    private static bool TryConvert(object? value, Type target, out object? converted)
    {
        try
        {
            converted = ConvertValue(value, target);
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidCastException
                or FormatException
                or OverflowException
                or ArgumentException
                or NotSupportedException)
        {
            converted = null;
            return false;
        }
    }

    internal static object? ConvertValue(object? value, Type target)
    {
        var underlying = Nullable.GetUnderlyingType(target) ?? target;

        if (value is null)
        {
            return target.IsValueType && Nullable.GetUnderlyingType(target) is null
                ? Activator.CreateInstance(target)
                : null;
        }

        if (underlying.IsInstanceOfType(value))
        {
            return value;
        }

        if (underlying.IsEnum)
        {
            return value is string text
                ? Enum.Parse(underlying, text, ignoreCase: true)
                : Enum.ToObject(underlying, Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }

        if (underlying == typeof(bool))
        {
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        if (underlying == typeof(string))
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
    }

    private static Type? ResolveType(string fullName) => TypeCache.GetOrAdd(fullName, static name =>
    {
        var type = Type.GetType(name);
        if (type is not null)
        {
            return type;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(name);
            if (type is not null)
            {
                return type;
            }
        }

        return null;
    });

    private static LuaEnv GetEnvironment(IntPtr state) =>
        Environments.TryGetValue(state, out var environment)
            ? environment
            : throw new InvalidOperationException("LuaLoom environment is not installed for this state.");

    private static string GetUpvalueString(IntPtr state, int index) =>
        ReadString(state, LuaApi.Profile.RegistryIndex - index);

    private static string ReadString(IntPtr state, int index)
    {
        var pointer = LuaApi.lua_tolstring(state, index, out var length);
        return pointer == IntPtr.Zero
            ? string.Empty
            : NativeMarshal.PtrToStringUTF8(pointer, checked((int)length)) ?? string.Empty;
    }

    private static string DescribeLuaType(IntPtr state, int index)
    {
        var name = LuaApi.lua_typename(state, LuaApi.lua_type(state, index));
        return NativeMarshal.PtrToStringUTF8(name) ?? "unknown";
    }

    private static int Guarded(IntPtr state, Func<IntPtr, int> body)
    {
        try
        {
            return body(state);
        }
        catch (Exception exception)
        {
            LuaApi.lua_pushstring(state, exception.Message);
            return LuaApi.lua_error(state);
        }
    }
}
