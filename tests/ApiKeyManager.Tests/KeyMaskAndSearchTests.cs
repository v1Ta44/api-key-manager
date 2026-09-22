using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ApiKeyManager.Tests;

/// <summary>密钥掩码（原 SelfTest 第 7 项 + 边界）。</summary>
public sealed class KeyMaskTests
{
    [Fact]
    public void Format_HidesMiddleButKeepsTail()
    {
        string masked = KeyMask.Format("sk-1234567890abcdef");

        Assert.NotEqual("sk-1234567890abcdef", masked);
        Assert.EndsWith("cdef", masked);
        Assert.DoesNotContain("1234567890abcdef", masked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Format_ReturnsEmptyForBlankInput(string? input)
    {
        Assert.Equal("", KeyMask.Format(input));
    }

    [Fact]
    public void Format_ShortKeysDoNotLeakPlaintext()
    {
        // 8 字符以内的短密钥直接全掩码，不保留首尾
        string masked = KeyMask.Format("abc12345");
        Assert.DoesNotContain("abc12345", masked);
        Assert.All(masked, c => Assert.Equal('•', c));
    }

    [Fact]
    public void Format_TrimsSurroundingWhitespace()
    {
        Assert.Equal(KeyMask.Format("sk-1234567890abcdef"), KeyMask.Format("  sk-1234567890abcdef  "));
    }
}

/// <summary>列表搜索过滤（新增覆盖）。</summary>
public sealed class EntrySearchTests
{
    private static List<ApiEntry> Sample() => new()
    {
        new() { Name = "OpenAI", Provider = "OpenAI", Tags = "AI,官方", Notes = "公司账号",
                BaseUrl = "https://api.openai.com/v1", Model = "gpt-4o" },
        new() { Name = "DeepSeek", Provider = "DeepSeek", Tags = "AI", Notes = "个人开发用",
                BaseUrl = "https://api.deepseek.com", Model = "deepseek-chat" },
        new() { Name = "短信网关", Provider = "云短信", Tags = "短信,生产", Notes = "生产环境通知",
                BaseUrl = "https://sms.example.com/v2", Model = "" },
    };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Filter_ReturnsEverythingForBlankQuery(string? query)
    {
        Assert.Equal(3, EntrySearch.Filter(Sample(), query).Count);
    }

    [Theory]
    [InlineData("openai", "OpenAI")]
    [InlineData("OPENAI", "OpenAI")]      // 大小写不敏感
    [InlineData("deepseek", "DeepSeek")]
    [InlineData("官方", "OpenAI")]         // 命中标签
    [InlineData("个人开发", "DeepSeek")]   // 命中备注
    [InlineData("sms.example", "短信网关")] // 命中 URL
    [InlineData("chat", "DeepSeek")]       // 命中模型
    public void Filter_MatchesAcrossAllSearchableFields(string query, string expectedName)
    {
        var hits = EntrySearch.Filter(Sample(), query);

        Assert.Contains(hits, e => e.Name == expectedName);
    }

    [Fact]
    public void Filter_ReturnsEmptyWhenNothingMatches()
    {
        Assert.Empty(EntrySearch.Filter(Sample(), "zzz-not-present"));
    }

    [Fact]
    public void Filter_SortsByName()
    {
        var names = EntrySearch.Filter(Sample(), null).Select(e => e.Name).ToList();
        var expected = names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

        Assert.Equal(expected, names);
    }
}
