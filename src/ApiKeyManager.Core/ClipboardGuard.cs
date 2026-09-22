using System;

namespace ApiKeyManager;

/// <summary>
/// 剪贴板定时清理：复制密钥 N 秒后自动清除。
/// 抽成独立类型便于单测——清理逻辑不依赖 UI。
/// 只有剪贴板内容仍是当初复制的原文时才清除，避免误删用户后来复制的东西。
/// </summary>
public sealed class ClipboardGuard
{
    private readonly Func<string?> _readText;
    private readonly Action _clear;

    private string? _text;
    private DateTime _clearAt;

    public ClipboardGuard(Func<string?> readText, Action clear)
    {
        _readText = readText;
        _clear = clear;
    }

    /// <summary>当前是否有等待清理的剪贴板内容。</summary>
    public bool HasPending => _text != null;

    /// <summary>距自动清除的剩余时间（无待清理内容时为 null）。</summary>
    public TimeSpan? TimeLeft => _text == null ? null : Max(TimeSpan.Zero, _clearAt - DateTime.UtcNow);

    /// <summary>记录一次复制，<paramref name="seconds"/> &lt;= 0 表示不自动清除。</summary>
    public void Track(string value, int seconds)
    {
        if (string.IsNullOrEmpty(value) || seconds <= 0)
        {
            _text = null;
            return;
        }
        _text = value;
        _clearAt = DateTime.UtcNow.AddSeconds(seconds);
    }

    /// <summary>到点则清理；<paramref name="force"/> 为 true 时无条件立即清理（退出 / 锁定时用）。</summary>
    public void Tick(bool force)
    {
        if (_text == null) return;
        if (!force && DateTime.UtcNow < _clearAt) return;

        try
        {
            if (force || _readText() == _text)
            {
                _clear();
            }
        }
        catch
        {
            // 剪贴板被其他进程占用时忽略，不能因为清理失败影响主流程
        }

        _text = null;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
