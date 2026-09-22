using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ApiKeyManager;

/// <summary>明文 CSV 导出（含注入防护）。</summary>
public static class CsvExporter
{
    public const string Header = "名称,提供商,API Key,Base URL,默认模型,标签,备注,创建时间,更新时间";

    /// <summary>
    /// 公式注入防护：Excel / LibreOffice 会把 = + - @ 以及制表符 / 回车开头的单元格
    /// 当作公式执行（可导致 DDE 命令执行或数据外泄）。这里统一加前导单引号，
    /// 使其在电子表格中按纯文本处理。
    /// </summary>
    internal static string Escape(string? value)
    {
        string s = value ?? "";

        if (s.Length > 0 && (s[0] == '=' || s[0] == '+' || s[0] == '-' || s[0] == '@'
                             || s[0] == '\t' || s[0] == '\r'))
        {
            s = "'" + s;
        }

        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    public static string Build(IEnumerable<ApiEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header);
        foreach (var e in entries.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            sb.AppendLine(string.Join(",", new[]
            {
                e.Name, e.Provider, e.ApiKey, e.BaseUrl, e.Model, e.Tags, e.Notes,
                e.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                e.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            }.Select(Escape)));
        }
        return sb.ToString();
    }
}
