namespace LuaLoom.Native;

/// <summary>加载 Lua 原生库。默认实现按既定顺序查找 lua54.dll。</summary>
public interface ILibraryLoader
{
    /// <summary>加载库并返回模块句柄；失败抛 <see cref="LuaLibraryNotFoundException"/>。</summary>
    IntPtr Load(string libraryName);
}
