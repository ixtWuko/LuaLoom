namespace LuaLoom.Native;

/// <summary>Lua 值的类型，取值与 lua.h 的 LUA_T* 一致。</summary>
internal enum LuaType
{
    None = -1,
    Nil = 0,
    Boolean = 1,
    LightUserData = 2,
    Number = 3,
    String = 4,
    Table = 5,
    Function = 6,
    UserData = 7,
    Thread = 8,
}
