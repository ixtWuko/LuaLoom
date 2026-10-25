using LuaLoom;
using LuaLoom.Native;
using LuaLoom.UnitTests.Sample;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class GeneratedEventBindingTests
{
    private const string EmitterType = "CS.LuaLoom.UnitTests.Sample.Emitter";

    [Fact]
    public void GeneratedStyleEventBinding_SubscribesAndUnsubscribes()
    {
        using var env = new LuaEnv();
        env.RegisterType(new LuaTypeBinding
        {
            Type = typeof(Emitter),
            FullName = "LuaLoom.UnitTests.Sample.Emitter",
            InstanceIndex = InstanceIndex,
        });

        var results = env.DoString($"""
            local emitter = {EmitterType}()
            local seen = 0
            local function on_changed(value) seen = value end
            emitter:Changed('+', on_changed)
            emitter:Raise(5)
            local first = seen
            emitter:Changed('-', on_changed)
            emitter:Raise(7)
            return first, seen
            """);

        Assert.Equal(5L, results[0]);
        Assert.Equal(5L, results[1]);
    }

    private static readonly LuaCSFunction InstanceIndexHandler = InstanceIndex;
    private static readonly LuaCSFunction EventChangedHandler = EventChanged;

    private static int InstanceIndex(IntPtr state)
    {
        if (LuaBinding.ReadString(state, 2) == "Changed")
        {
            LuaBinding.PushFunction(state, EventChangedHandler);
            return 1;
        }

        return 0;
    }

    private static int EventChanged(IntPtr state)
    {
        var self = (Emitter)LuaBinding.ReadObject(state, 1)!;
        var op = LuaBinding.ReadString(state, 2);
        if (op == "+")
        {
            var pointer = LuaBinding.GetFunctionPointer(state, 3);
            if (LuaEventBridge.Take(self, "Changed", pointer) is Action<int> previous)
            {
                self.Changed -= previous;
            }

            var function = LuaBinding.CreateFunction(state, 3);
            var handler = new Action<int>(value => function.Call(value));
            LuaEventBridge.Add(self, "Changed", pointer, handler, function);
            self.Changed += handler;
            return 0;
        }

        if (op == "-")
        {
            var handler = LuaEventBridge.Remove(self, "Changed", LuaBinding.GetFunctionPointer(state, 3));
            if (handler is Action<int> typed)
            {
                self.Changed -= typed;
            }

            return 0;
        }

        LuaBinding.Throw(state, "Event operator must be '+' or '-'.");
        return 0;
    }
}
