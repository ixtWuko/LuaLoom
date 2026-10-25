using LuaLoom;
using Xunit;

namespace LuaLoom.UnitTests;

/// <summary>字符串跨 P/Invoke 的编码契约：两侧都必须是 UTF-8。</summary>
public sealed class EncodingTests
{
    [Theory]
    [InlineData("ascii only")]
    [InlineData("中文值")]
    [InlineData("日本語テスト")]
    [InlineData("emoji 😀 混排")]
    [InlineData("a,b;c=d")]
    public void Utf8String_RoundTripsThroughLua(string value)
    {
        using var env = new LuaEnv();
        env.DoString("function echo(value) return value end");
        using var echo = env.Global.Get<LuaFunction>("echo");

        var results = echo.Call(value);

        Assert.Single(results);
        Assert.Equal(value, results[0]);
    }

    [Theory]
    [InlineData("名称", "张三")]
    [InlineData("ключ", "значение")]
    [InlineData("キー", "値")]
    public void Utf8Key_MatchesKeyCreatedInLua(string key, string value)
    {
        using var env = new LuaEnv();
        env.DoString($"config = {{ ['{key}'] = '{value}' }}");
        using var config = env.Global.Get<LuaTable>("config");

        Assert.Equal(value, config.Get<string>(key));
    }

    [Fact]
    public void Utf8String_IsWrittenAsUtf8Bytes()
    {
        using var env = new LuaEnv();
        env.Global.Set("名字", "张三");

        // Lua 标识符只能是 ASCII，用 _G 取键；utf8.len 能进一步证明按 UTF-8 编码
        var length = env.DoString("return utf8.len(_G['名字'])")[0];

        Assert.Equal(2L, length);
    }
}
