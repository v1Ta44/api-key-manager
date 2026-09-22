using System;
using System.Collections.Generic;
using System.Linq;

namespace ApiKeyManager;

/// <summary>列表模糊搜索（名称 / 提供商 / 标签 / 备注 / URL / 模型）。</summary>
public static class EntrySearch
{
    /// <summary>
    /// 对一组记录做模糊过滤 + 按名称排序。
    /// 使用 OrdinalIgnoreCase：与区域无关、不触发文化相关的多次比较，比 CurrentCulture 快得多。
    /// </summary>
    public static List<ApiEntry> Filter(IEnumerable<ApiEntry> entries, string? query)
    {
        IEnumerable<ApiEntry> view = entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase);

        string q = (query ?? "").Trim();
        if (q.Length > 0)
        {
            view = view.Where(e => Matches(e, q));
        }

        return view.ToList();
    }

    public static bool Matches(ApiEntry e, string query) =>
        Contains(e.Name, query) || Contains(e.Provider, query) || Contains(e.Tags, query) ||
        Contains(e.Notes, query) || Contains(e.BaseUrl, query) || Contains(e.Model, query);

    private static bool Contains(string? haystack, string needle) =>
        (haystack ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}
