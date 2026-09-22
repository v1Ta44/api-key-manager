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
    private int _failures;
    public bool CleanupFailed { get; private set; }

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
        _failures = 0;
        CleanupFailed = false;
        if (string.IsNullOrEmpty(value) || seconds <= 0)
        {
            _text = null;
            return;
        }
        _text = value;
        _clearAt = DateTime.UtcNow.AddSeconds(seconds);
    }

    /// <summary>到点或强制时清理自己的内容；被占用最多重试五次。</summary>
    public void Tick(bool force)
    {
        if (_text == null) return;
        if (!force && DateTime.UtcNow < _clearAt) return;

        try
        {
            var current = _readText();
            if (current == null) throw new InvalidOperationException("剪贴板暂不可用");
            if (current == _text)
            {
                _clear();
            }
        }
        catch
        {
            _clearAt = DateTime.UtcNow;
            if (++_failures < 5) return;
            CleanupFailed = true;
        }

        _text = null;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
