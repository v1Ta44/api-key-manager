using System;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ApiKeyManager.Wpf.Services;

/// <summary>
/// 自动锁定监视器 —— 取代 WinForms 版的 <c>IMessageFilter</c> 全局活动检测。
///
/// 背景：WinForms 版用 <c>Application.AddMessageFilter</c> 拦截 WM_KEYDOWN /
/// WM_MOUSEMOVE 等来记录"用户还在操作"。WPF 没有直接等价物，
/// <c>HwndSource.AddHook</c> 只能拿到当前窗口的消息。
///
/// 这里用 <see cref="ComponentDispatcher.ThreadPreprocessMessage"/>：
/// 它是 WPF 内置的线程级消息钩子，覆盖本进程所有窗口，无需额外 P/Invoke。
/// 消息常量与原实现保持一致。
/// </summary>
public sealed class AutoLockWatcher : IDisposable
{
    // 与原 WinForms 版 ActivityFilter 完全相同的消息集合
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MOUSEWHEEL = 0x020A;

    private readonly DispatcherTimer _timer;
    private readonly Func<int> _getIdleLimitMinutes;
    private readonly Action _onLock;
    private readonly Action<TimeSpan>? _onTick;

    private DateTime _lastActivityUtc = DateTime.UtcNow;
    private bool _hooked;
    private bool _disposed;

    /// <param name="getIdleLimitMinutes">返回当前的闲置上限（分钟），返回 &lt;= 0 表示关闭自动锁定。</param>
    /// <param name="onLock">到期时调用（执行锁定）。</param>
    /// <param name="onTick">每秒调用一次，参数是剩余时间；用于刷新倒计时胶囊。</param>
    public AutoLockWatcher(
        Func<int> getIdleLimitMinutes,
        Action onLock,
        Action<TimeSpan>? onTick = null)
    {
        _getIdleLimitMinutes = getIdleLimitMinutes;
        _onLock = onLock;
        _onTick = onTick;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += OnTimerTick;
    }

    /// <summary>开始监视。幂等。</summary>
    public void Start()
    {
        if (_disposed) return;

        if (!_hooked)
        {
            ComponentDispatcher.ThreadPreprocessMessage += OnPreprocessMessage;
            _hooked = true;
        }

        _lastActivityUtc = DateTime.UtcNow;
        _timer.Start();
    }

    /// <summary>重置闲置计时（例如刚解锁、或执行了某个操作）。</summary>
    public void Ping() => _lastActivityUtc = DateTime.UtcNow;

    /// <summary>暂停计时（有模态对话框时不该自动锁定）。</summary>
    public void Pause() => _timer.Stop();

    /// <summary>恢复计时并重置。</summary>
    public void Resume()
    {
        Ping();
        if (!_disposed) _timer.Start();
    }

    private void OnPreprocessMessage(ref MSG msg, ref bool handled)
    {
        switch (msg.message)
        {
            case WM_KEYDOWN:
            case WM_KEYUP:
            case WM_MOUSEMOVE:
            case WM_LBUTTONDOWN:
            case WM_RBUTTONDOWN:
            case WM_MBUTTONDOWN:
            case WM_MOUSEWHEEL:
                _lastActivityUtc = DateTime.UtcNow;
                break;
        }
        // 不设置 handled：只观察，不拦截
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        int minutes = _getIdleLimitMinutes();
        if (minutes <= 0)
        {
            _onTick?.Invoke(TimeSpan.Zero);
            return;
        }

        var limit = TimeSpan.FromMinutes(minutes);
        var idle = DateTime.UtcNow - _lastActivityUtc;

        if (idle >= limit)
        {
            // 先停表，避免锁定过程中重复触发
            _timer.Stop();
            Ping();
            _onLock();
            return;
        }

        _onTick?.Invoke(limit - idle);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
        _timer.Tick -= OnTimerTick;

        if (_hooked)
        {
            ComponentDispatcher.ThreadPreprocessMessage -= OnPreprocessMessage;
            _hooked = false;
        }
    }
}
