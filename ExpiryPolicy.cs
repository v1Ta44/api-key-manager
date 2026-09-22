using System;
using System.Collections.Generic;
using System.Linq;

namespace ApiKeyManager;

/// <summary>密钥到期状态。</summary>
public enum ExpiryState
{
    /// <summary>未设置到期日。</summary>
    None,

    /// <summary>仍在有效期内，无需提醒。</summary>
    Valid,

    /// <summary>即将到期（默认 14 天内）。</summary>
    ExpiringSoon,

    /// <summary>已过期。</summary>
    Expired,
}

/// <summary>到期判定结果。</summary>
public readonly record struct ExpiryInfo(ExpiryState State, int? DaysLeft)
{
    public bool NeedsAttention => State is ExpiryState.ExpiringSoon or ExpiryState.Expired;

    /// <summary>给状态栏 / 对话框用的短描述。</summary>
    public string Describe() => State switch
    {
        ExpiryState.Expired => DaysLeft is { } d && d < 0
            ? $"已过期 {-d} 天"
            : "已过期",
        ExpiryState.ExpiringSoon => DaysLeft is 0 ? "今天到期" : $"{DaysLeft} 天后到期",
        ExpiryState.Valid => "有效",
        _ => "未设置到期日",
    };
}

/// <summary>
/// 密钥到期判定。抽成纯函数便于单测——日期边界是最容易写错的地方。
/// 所有比较都按"本地日期"而非精确时刻：用户填的是日期，不是时间戳，
/// 当天填今天到期不应因为时分秒而立刻显示已过期。
/// </summary>
public static class ExpiryPolicy
{
    /// <summary>距到期多少天开始提醒。</summary>
    public const int SoonWindowDays = 14;

    /// <summary>
    /// 判定单条记录在 <paramref name="now"/>（本地时间）的到期状态。
    /// </summary>
    public static ExpiryInfo Evaluate(ApiEntry entry, DateTime now) =>
        Evaluate(entry.ExpiresUtc, now);

    public static ExpiryInfo Evaluate(DateTime? expires, DateTime now)
    {
        if (expires is not { } exp) return new ExpiryInfo(ExpiryState.None, null);

        // 按日期粒度比较，忽略时分秒
        int days = (int)Math.Floor((exp.Date - now.Date).TotalDays);

        if (days < 0) return new ExpiryInfo(ExpiryState.Expired, days);
        if (days <= SoonWindowDays) return new ExpiryInfo(ExpiryState.ExpiringSoon, days);
        return new ExpiryInfo(ExpiryState.Valid, days);
    }

    /// <summary>
    /// 汇总需要关注的记录（已过期 + 即将到期），已过期的排在前面。
    /// </summary>
    public static List<(ApiEntry Entry, ExpiryInfo Info)> FindAlerts(
        IEnumerable<ApiEntry> entries, DateTime now)
    {
        return entries
            .Select(e => (Entry: e, Info: Evaluate(e, now)))
            .Where(x => x.Info.NeedsAttention)
            .OrderBy(x => x.Info.State == ExpiryState.Expired ? 0 : 1)
            .ThenBy(x => x.Info.DaysLeft ?? int.MaxValue)
            .ThenBy(x => x.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
