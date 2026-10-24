using System.Reflection;
using LuaLoom.Native;

namespace LuaLoom;

public sealed partial class LuaEnv
{
    private readonly Dictionary<Type, LuaTypeBinding> _typeBindings = new();
    private readonly Dictionary<string, LuaTypeBinding> _typeBindingsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, ILuaDelegateInvoker> _delegateBindings = new();
    private readonly Dictionary<Type, Func<LuaTable, object>> _interfaceBindings = new();
    private readonly List<Type> _extensionTypes = new();
    private readonly Dictionary<(Type Type, string Name), (bool Found, MethodInfo? Method)> _extensionCache = new();
    private readonly List<(Type Target, string Name, LuaCSFunction Handler)> _extensionMethods = new();
    private readonly HashSet<Type> _privateAccessible = new();

    public void RegisterType(LuaTypeBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        EnsureReady();
        _typeBindings[binding.Type] = binding;
        _typeBindingsByName[binding.FullName] = binding;
    }

    public void RegisterDelegate(Type delegateType, ILuaDelegateInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(delegateType);
        ArgumentNullException.ThrowIfNull(invoker);
        EnsureReady();
        _delegateBindings[delegateType] = invoker;
    }

    public void RegisterInterface(Type interfaceType, Func<LuaTable, object> factory)
    {
        ArgumentNullException.ThrowIfNull(interfaceType);
        ArgumentNullException.ThrowIfNull(factory);
        EnsureReady();
        _interfaceBindings[interfaceType] = factory;
    }

    /// <summary>注册一个静态扩展方法类；只有注册过的扩展方法才可见。</summary>
    public void RegisterExtensionType(Type extensionType)
    {
        ArgumentNullException.ThrowIfNull(extensionType);
        if (!extensionType.IsAbstract || !extensionType.IsSealed)
        {
            throw new ArgumentException(
                $"Extension type '{extensionType.FullName}' must be a static class.",
                nameof(extensionType));
        }

        EnsureReady();
        if (!_extensionTypes.Contains(extensionType))
        {
            _extensionTypes.Add(extensionType);
            _extensionCache.Clear();
        }
    }

    /// <summary>允许反射访问指定类型的非公共成员。</summary>
    public void MakePrivateAccessible(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        EnsureReady();
        _privateAccessible.Add(type);
    }

    internal bool IsPrivateAccessible(Type type) => _privateAccessible.Contains(type);

    /// <summary>注册生成器产出的扩展方法分派入口。</summary>
    public void RegisterExtensionMethod(Type targetType, string name, LuaCSFunction handler)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(handler);
        EnsureReady();
        _extensionMethods.Add((targetType, name, handler));
    }

    internal bool TryGetExtensionMethod(Type type, string name, out LuaCSFunction handler)
    {
        foreach (var entry in _extensionMethods)
        {
            if (entry.Name == name && entry.Target.IsAssignableFrom(type))
            {
                handler = entry.Handler;
                return true;
            }
        }

        handler = null!;
        return false;
    }

    internal MethodInfo? FindExtensionMethod(Type type, string name)
    {
        var key = (type, name);
        if (_extensionCache.TryGetValue(key, out var cached))
        {
            return cached.Found ? cached.Method : null;
        }

        var method = Interop.SearchExtensionMethod(_extensionTypes, type, name);
        _extensionCache[key] = (method is not null, method);
        return method;
    }

    internal bool TryGetDelegate(Type type, out ILuaDelegateInvoker invoker) =>
        _delegateBindings.TryGetValue(type, out invoker!);

    internal bool TryGetInterfaceFactory(Type type, out Func<LuaTable, object> factory) =>
        _interfaceBindings.TryGetValue(type, out factory!);

    internal bool TryGetTypeBinding(Type type, out LuaTypeBinding binding) =>
        _typeBindings.TryGetValue(type, out binding!);

    internal bool TryGetTypeBinding(string fullName, out LuaTypeBinding binding) =>
        _typeBindingsByName.TryGetValue(fullName, out binding!);
}
