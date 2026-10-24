namespace LuaLoom;

/// <summary>把 Lua 函数包装成指定 delegate 类型的工厂，由生成代码实现。</summary>
public interface ILuaDelegateInvoker
{
    Delegate Create(LuaFunction function);
}
