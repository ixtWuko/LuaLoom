using LuaLoom.Native;

namespace LuaLoom.Internal;

/// <summary>注册表引用的分配与释放。luaL_ref 自身会复用空闲 id。</summary>
internal sealed class LuaReferenceTable
{
    private readonly IntPtr _state;

    public LuaReferenceTable(IntPtr state)
    {
        _state = state;
    }

    public int Add(int index) => LuaApi.luaL_ref(_state, LuaApi.LuaRegistryIndex);

    public void Release(int id)
    {
        if (id != 0)
        {
            LuaApi.luaL_unref(_state, LuaApi.LuaRegistryIndex, id);
        }
    }
}
