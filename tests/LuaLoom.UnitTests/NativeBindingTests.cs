using System.Text;
using LuaLoom.Native;
using Xunit;

namespace LuaLoom.UnitTests;

public sealed class NativeBindingTests
{
    [Fact]
    public void CreateState_StartsWithEmptyStack()
    {
        var state = LuaApi.luaL_newstate();

        try
        {
            Assert.NotEqual(IntPtr.Zero, state);
            Assert.Equal(0, LuaApi.lua_gettop(state));
        }
        finally
        {
            LuaApi.lua_close(state);
        }
    }

    [Fact]
    public void Guard_RestoresStackAfterExit()
    {
        var state = NewState();

        try
        {
            LuaApi.lua_pushinteger(state, 1);

            using (LuaStackGuard.Enter(state))
            {
                LuaApi.lua_pushinteger(state, 2);
                LuaApi.lua_pushinteger(state, 3);
                Assert.Equal(3, LuaApi.lua_gettop(state));
            }

            Assert.Equal(1, LuaApi.lua_gettop(state));
        }
        finally
        {
            LuaApi.lua_close(state);
        }
    }

    [Fact]
    public void Guard_RestoresStackWhenExceptionThrown()
    {
        var state = NewState();

        try
        {
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using (LuaStackGuard.Enter(state))
                {
                    LuaApi.lua_pushinteger(state, 1);
                    throw new InvalidOperationException("boom");
                }
            }));

            Assert.Equal(0, LuaApi.lua_gettop(state));
        }
        finally
        {
            LuaApi.lua_close(state);
        }
    }

    [Fact]
    public void NestedGuards_RestoreIndependently()
    {
        var state = NewState();

        try
        {
            using (LuaStackGuard.Enter(state))
            {
                LuaApi.lua_pushinteger(state, 1);

                using (LuaStackGuard.Enter(state))
                {
                    LuaApi.lua_pushinteger(state, 2);
                }

                Assert.Equal(1, LuaApi.lua_gettop(state));
                LuaApi.lua_pushinteger(state, 3);
                Assert.Equal(2, LuaApi.lua_gettop(state));
            }

            Assert.Equal(0, LuaApi.lua_gettop(state));
        }
        finally
        {
            LuaApi.lua_close(state);
        }
    }

    [Fact]
    public void Guard_WithZeroState_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            using var guard = LuaStackGuard.Enter(IntPtr.Zero);
        });
    }

    [Fact]
    public void LoadBuffer_WithSyntaxError_ReturnsErrSyntax()
    {
        var state = NewState();

        try
        {
            var chunk = Encoding.UTF8.GetBytes("local = 1");
            var status = LuaApi.luaL_loadbufferx(state, chunk, (nuint)chunk.Length, "syntax", mode: null);

            Assert.Equal(LuaApi.LuaErrSyntax, status);

            var exception = LuaErrors.Create(state, LuaStatus.ErrSyntax, "syntax");
            Assert.Equal(LuaStatus.ErrSyntax, exception.Status);
            Assert.Equal("syntax", exception.ChunkName);
            Assert.Equal(1, exception.Line);
            Assert.Equal(0, LuaApi.lua_gettop(state));
        }
        finally
        {
            LuaApi.lua_close(state);
        }
    }

    [Fact]
    public void PCall_WithRuntimeError_ThrowsLuaException()
    {
        var state = NewState();

        try
        {
            var chunk = Encoding.UTF8.GetBytes("error('boom')");
            Assert.Equal(0, LuaApi.luaL_loadbufferx(state, chunk, (nuint)chunk.Length, "boom", mode: null));

            var exception = Assert.Throws<LuaException>(() => LuaErrors.PCall(state, 0, 0, "boom"));

            Assert.Equal(LuaStatus.ErrRun, exception.Status);
            Assert.Contains("boom", exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, LuaApi.lua_gettop(state));
        }
        finally
        {
            LuaApi.lua_close(state);
        }
    }

    [Fact]
    public void Loader_MissingLibrary_ThrowsWithAttemptedPaths()
    {
        var exception = Assert.Throws<LuaLibraryNotFoundException>(
            () => NativeLibraryLoader.Instance.Load("lua99"));

        Assert.Equal("lua99", exception.LibraryName);
        Assert.NotEmpty(exception.AttemptedPaths);
        Assert.Contains("lua99", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            exception.AttemptedPaths,
            static path => path.Contains(Path.Combine("runtimes", System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, "native"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Loader_LoadsSameLibraryOnlyOnce()
    {
        var first = NativeLibraryLoader.Instance.Load(LuaApi.Library);
        var second = NativeLibraryLoader.Instance.Load(LuaApi.Library);

        Assert.NotEqual(IntPtr.Zero, first);
        Assert.Equal(first, second);
    }

    private static IntPtr NewState()
    {
        var state = LuaApi.luaL_newstate();
        Assert.NotEqual(IntPtr.Zero, state);
        return state;
    }
}
