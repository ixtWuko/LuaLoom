using System.Runtime.CompilerServices;

namespace LuaLoom;

/// <summary>生成事件绑定使用的订阅表：对象、事件名、Lua 函数指针到委托的映射。</summary>
public static class LuaEventBridge
{
    private static readonly ConditionalWeakTable<object, Subscriptions> Table = new();

    public static void Add(
        object instance,
        string eventName,
        IntPtr pointer,
        Delegate handler,
        LuaFunction function)
    {
        var subscriptions = Table.GetOrCreateValue(instance);
        lock (subscriptions)
        {
            subscriptions.Add(eventName, pointer, handler, function);
        }
    }

    public static Delegate? Remove(object instance, string eventName, IntPtr pointer)
    {
        if (!Table.TryGetValue(instance, out var subscriptions))
        {
            return null;
        }

        lock (subscriptions)
        {
            return subscriptions.Remove(eventName, pointer);
        }
    }

    /// <summary>取出并移除已有订阅；重复订阅时由调用方先把它从事件上摘掉。</summary>
    public static Delegate? Take(object instance, string eventName, IntPtr pointer)
    {
        if (!Table.TryGetValue(instance, out var subscriptions))
        {
            return null;
        }

        lock (subscriptions)
        {
            return subscriptions.Take(eventName, pointer);
        }
    }

    private sealed class Subscriptions
    {
        private readonly Dictionary<string, Dictionary<IntPtr, (Delegate Handler, LuaFunction Function)>> _events = new();

        public void Add(string name, IntPtr pointer, Delegate handler, LuaFunction function)
        {
            if (!_events.TryGetValue(name, out var handlers))
            {
                handlers = new Dictionary<IntPtr, (Delegate, LuaFunction)>();
                _events[name] = handlers;
            }

            if (handlers.Remove(pointer, out var previous))
            {
                previous.Function.Dispose();
            }

            handlers[pointer] = (handler, function);
        }

        public Delegate? Remove(string name, IntPtr pointer)
        {
            if (!_events.TryGetValue(name, out var handlers)
                || !handlers.Remove(pointer, out var entry))
            {
                return null;
            }

            entry.Function.Dispose();
            return entry.Handler;
        }

        public Delegate? Take(string name, IntPtr pointer)
        {
            return Remove(name, pointer);
        }
    }
}
