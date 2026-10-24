namespace LuaLoom;

/// <summary>自定义类型映射的扩展点，注册后优先于内置规则。</summary>
public interface ITypeMarshaler
{
    bool CanMarshal(Type type);

    void Push(IntPtr state, object value);

    object? Read(IntPtr state, int index, Type type);
}
