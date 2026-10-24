namespace LuaLoom;

public sealed partial class LuaEnv
{
    private readonly Dictionary<Type, LuaTypeBinding> _typeBindings = new();
    private readonly Dictionary<string, LuaTypeBinding> _typeBindingsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, ILuaDelegateInvoker> _delegateBindings = new();
    private readonly Dictionary<Type, Func<LuaTable, object>> _interfaceBindings = new();

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

    internal bool TryGetDelegate(Type type, out ILuaDelegateInvoker invoker) =>
        _delegateBindings.TryGetValue(type, out invoker!);

    internal bool TryGetInterfaceFactory(Type type, out Func<LuaTable, object> factory) =>
        _interfaceBindings.TryGetValue(type, out factory!);

    internal bool TryGetTypeBinding(Type type, out LuaTypeBinding binding) =>
        _typeBindings.TryGetValue(type, out binding!);

    internal bool TryGetTypeBinding(string fullName, out LuaTypeBinding binding) =>
        _typeBindingsByName.TryGetValue(fullName, out binding!);
}
