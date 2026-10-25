using System.Runtime.CompilerServices;
using LuaLoom;
using LuaLoom.Internal;
using LuaLoom.Native;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class MarshalTests : IDisposable
{
    private readonly IntPtr _state;
    private readonly LuaMarshal _marshal;

    public MarshalTests()
    {
        _state = LuaApi.luaL_newstate();
        Assert.NotEqual(IntPtr.Zero, _state);
        LuaApi.luaL_openlibs(_state);
        _marshal = new LuaMarshal(_state);
    }

    public void Dispose()
    {
        _marshal.Dispose();
        LuaApi.lua_close(_state);
    }

    private enum SmallEnum
    {
        A = 1,
        B = 2,
    }

    private struct Vec2
    {
        public float X;
        public float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    [Fact]
    public void Scalars_RoundTrip()
    {
        using var guard = LuaStackGuard.Enter(_state);

        _marshal.Push(42);
        Assert.Equal(42, _marshal.Read<int>(-1));

        _marshal.Push(true);
        Assert.True(_marshal.Read<bool>(-1));

        _marshal.Push(1.5);
        Assert.Equal(1.5, _marshal.Read<double>(-1));

        _marshal.Push("hello");
        Assert.Equal("hello", _marshal.Read<string>(-1));

        _marshal.Push<string?>(null);
        Assert.Null(_marshal.ReadObject(-1));
    }

    [Fact]
    public void LongAndULong_RoundTrip()
    {
        using var guard = LuaStackGuard.Enter(_state);

        _marshal.Push(long.MinValue);
        Assert.Equal(long.MinValue, _marshal.Read<long>(-1));

        _marshal.Push(long.MaxValue);
        Assert.Equal(long.MaxValue, _marshal.Read<long>(-1));

        _marshal.Push(ulong.MaxValue);
        Assert.Equal(ulong.MaxValue, _marshal.Read<ulong>(-1));
    }

    [Fact]
    public void StructAndEnum_RoundTrip_WithoutManagedAllocation()
    {
        // 预热，避免把 JIT 的首次分配算进来
        for (var i = 0; i < 100; i++)
        {
            LuaBinding.PushStruct(_state, new Vec2(i, i));
            _ = LuaBinding.ReadStruct<Vec2>(_state, -1);
            LuaApi.lua_settop(_state, LuaApi.lua_gettop(_state) - 1);
        }

        const int iterations = 1_000_000;
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < iterations; i++)
        {
            LuaBinding.PushStruct(_state, new Vec2(i, i + 1));
            var value = LuaBinding.ReadStruct<Vec2>(_state, -1);
            LuaApi.lua_settop(_state, LuaApi.lua_gettop(_state) - 1);

            if (value.X != i)
            {
                throw new InvalidOperationException("struct value mismatch");
            }

            _marshal.Push(i % 2 == 0 ? SmallEnum.A : SmallEnum.B);
            _ = _marshal.Read<SmallEnum>(-1);
            LuaApi.lua_settop(_state, LuaApi.lua_gettop(_state) - 1);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void SameObject_IsPushedAsSameUserdata()
    {
        using var guard = LuaStackGuard.Enter(_state);
        var value = new object();

        _marshal.PushObject(value);
        _marshal.PushObject(value);

        Assert.Equal(1, LuaApi.lua_rawequal(_state, -1, -2));
    }

    [Fact]
    public void BoolReader_UsesLuaTruthiness()
    {
        using var guard = LuaStackGuard.Enter(_state);
        _marshal.Push(1);
        Assert.True(_marshal.Read<bool>(-1));

        LuaApi.lua_settop(_state, LuaApi.lua_gettop(_state) - 1);
        LuaApi.lua_pushnil(_state);
        Assert.False(_marshal.Read<bool>(-1));

        LuaApi.lua_settop(_state, LuaApi.lua_gettop(_state) - 1);
        _marshal.Push(string.Empty);
        Assert.True(_marshal.Read<bool>(-1));
    }

    [Fact]
    public void IntegerReader_ReportsOverflowAsConversionError()
    {
        using var guard = LuaStackGuard.Enter(_state);
        _marshal.Push(4000000000L);

        Assert.Throws<LuaConversionException>(() => _marshal.Read<int>(-1));
        Assert.Throws<LuaConversionException>(() => LuaBinding.ReadInt(_state, -1));
    }

    [Fact]
    public void FloatReader_RejectsOutOfRangeValue()
    {
        using var guard = LuaStackGuard.Enter(_state);
        _marshal.Push(1e300);

        Assert.Throws<LuaConversionException>(() => _marshal.Read<float>(-1));
        Assert.Throws<LuaConversionException>(() => LuaBinding.ReadFloat(_state, -1));
    }

    [Fact]
    public void StringReader_ConvertsNumbersAndAcceptsNil()
    {
        using var guard = LuaStackGuard.Enter(_state);
        LuaApi.lua_pushnil(_state);
        Assert.Null(_marshal.Read<string>(-1));

        LuaApi.lua_settop(_state, LuaApi.lua_gettop(_state) - 1);
        _marshal.Push(42);
        Assert.Equal("42", _marshal.Read<string>(-1));
        Assert.Equal("42", LuaBinding.ReadString(_state, -1));

        LuaApi.lua_settop(_state, LuaApi.lua_gettop(_state) - 1);
        _marshal.Push(true);
        Assert.Throws<LuaConversionException>(() => _marshal.Read<string>(-1));
        Assert.Throws<LuaConversionException>(() => LuaBinding.ReadString(_state, -1));
    }

    [Fact]
    public void TypeMismatch_ThrowsConversionException()
    {
        using var guard = LuaStackGuard.Enter(_state);
        LuaApi.lua_pushstring(_state, "not a number");

        var exception = Assert.Throws<LuaConversionException>(() => _marshal.Read<int>(-1));

        Assert.Equal("Int32", exception.Expected);
        Assert.Equal("string", exception.Actual);
        Assert.Equal(-1, exception.Index);
    }

    [Fact]
    public void ReferenceIds_AreReusedAfterRelease()
    {
        var table = _marshal.CreateTable();
        var id = table.Id;

        _marshal.EnqueueRelease(id);
        Assert.Equal(1, _marshal.DrainDeferredReleases());

        var reused = _marshal.CreateTable();
        Assert.Equal(id, reused.Id);
    }

    [Fact]
    public void TableGetAndSet_RoundTrip()
    {
        using var table = _marshal.CreateTable();

        table.Set("hp", 100);
        table.Set("name", "player");

        Assert.Equal(100, table.Get<int>("hp"));
        Assert.Equal("player", table.Get<string>("name"));
        Assert.Equal(0, table.Get<int>("missing"));
    }

    [Fact]
    public void TableGetInPath_ReadsNestedValue()
    {
        using var table = _marshal.CreateTable();
        table.Set("a", _marshal.CreateTable());
        var nested = table.Get<LuaTable>("a");
        Assert.NotNull(nested);
        nested.Set("b", 3);
        nested.Dispose();

        Assert.Equal(3, table.GetInPath<int>("a.b"));
    }

    [Fact]
    public void TableGet_WrongType_ThrowsWithKey()
    {
        using var table = _marshal.CreateTable();
        table.Set("hp", "not a number");

        var exception = Assert.Throws<LuaConversionException>(() => table.Get<int>("hp"));

        Assert.Equal("hp", exception.Key);
    }

    [Fact]
    public void UndisposedHandle_IsReleasedAfterGcAndDrain()
    {
        var weak = CreateLeakedTable(out var id);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weak.IsAlive);
        Assert.True(_marshal.DrainDeferredReleases() >= 1);

        using var reused = _marshal.CreateTable();
        Assert.Equal(id, reused.Id);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference CreateLeakedTable(out int id)
    {
        var table = _marshal.CreateTable();
        id = table.Id;
        return new WeakReference(table);
    }

    [Fact]
    public void ReadObject_ReturnsTableHandle()
    {
        using var guard = LuaStackGuard.Enter(_state);
        using var table = _marshal.CreateTable();
        _marshal.PushReference(table.Id);

        var value = _marshal.ReadObject(-1);

        var read = Assert.IsType<LuaTable>(value);
        read.Set("x", 7);
        Assert.Equal(7, table.Get<int>("x"));
        read.Dispose();
    }
}
