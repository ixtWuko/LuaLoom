namespace LuaLoom.Native;

/// <summary>Lua C API 的状态码，取值与 lua.h 一致。</summary>
public enum LuaStatus
{
    Ok = 0,
    Yield = 1,
    ErrRun = 2,
    ErrSyntax = 3,
    ErrMem = 4,
    ErrErr = 5,
}
