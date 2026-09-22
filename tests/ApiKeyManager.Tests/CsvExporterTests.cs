using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ApiKeyManager.Tests;

/// <summary>明文 CSV 导出：注入防护 + 基本格式（新增覆盖）。</summary>
public sealed class CsvExporterTests
{
    [Theory]
    [InlineData("=cmd|'/c calc'!A0")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    [InlineData("\tpayload")]
    [InlineData("\rpayload")]
    public void Escape_NeutralisesFormulaPrefixes(string malicious)
    {
        string escaped = CsvExporter.Escape(malicious);

        // 结果是带引号的 CSV 字段，且真正的首字符已被前导单引号挡住
        Assert.StartsWith("\"'", escaped);
        Assert.DoesNotContain("\"=", escaped);
        Assert.DoesNotContain("\"@", escaped);
    }

    [Fact]
    public void Escape_LeavesOrdinaryValuesAlone()
    {
        Assert.Equal("\"OpenAI\"", CsvExporter.Escape("OpenAI"));
        Assert.Equal("\"\"", CsvExporter.Escape(null));
        Assert.Equal("\"\"", CsvExporter.Escape(""));
    }

    [Fact]
    public void Escape_DoublesEmbeddedQuotes()
    {
        Assert.Equal("\"say \"\"hi\"\"\"", CsvExporter.Escape("say \"hi\""));
    }

    [Fact]
    public void Escape_KeepsCommasFromBreakingColumns()
    {
        string csv = CsvExporter.Build(new[]
        {
            new ApiEntry { Name = "a,b", Provider = "p" }
        });

        // 头部 9 列；数据行也必须解析回 9 个字段
        Assert.Equal(9, SplitCsv(csv.Split('\n')[0].TrimEnd('\r')).Count);
        Assert.Equal(9, SplitCsv(csv.Split('\n')[1].TrimEnd('\r')).Count);
        Assert.Contains("\"a,b\"", csv);
    }

    [Fact]
    public void Build_WritesHeaderAndAllEntries()
    {
        var entries = new List<ApiEntry>
        {
            new() { Name = "乙", ApiKey = "k2" },
            new() { Name = "甲", ApiKey = "k1" },
        };

        string csv = CsvExporter.Build(entries);
        string[] lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(CsvExporter.Header, lines[0]);
        Assert.Equal(3, lines.Length);
        // 按名称排序
        Assert.Contains("甲", lines[1]);
        Assert.Contains("乙", lines[2]);
    }

    /// <summary>极简 CSV 解析，仅用于断言列数（支持双引号转义）。</summary>
    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else current.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        return fields;
    }
}
