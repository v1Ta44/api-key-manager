using System;
using System.Windows;
using ApiKeyManager;

namespace ApiKeyManager.Wpf.Services;

/// <summary>
/// 剪贴板适配层。
///
/// 为什么需要单独一层：WPF 的 <see cref="Clipboard"/> 与 WinForms 有一处重要差异 ——
/// 剪贴板被其它进程占用时（很常见，比如刚复制完大文件、或其他程序开着剪贴板监听），
/// WPF 会抛 <see cref="System.Runtime.InteropServices.COMException"/>，
/// 而 WinForms 的同类 API 往往只是静默失败。
///
/// 直接调用会导致"点复制就崩"。这里统一包住异常，失败返回 false，
/// 由调用方降级为状态栏提示。
/// </summary>
public sealed class ClipboardService
{
    private readonly ClipboardGuard _guard;

    public ClipboardService()
    {
        _guard = new ClipboardGuard(ReadTextSafe, ClearSafe);
    }

    /// <summary>当前是否有等待自动清除的内容。</summary>
    public bool HasPending => _guard.HasPending;

    /// <summary>
    /// 写入剪贴板并登记自动清除。
    /// 返回是否成功（失败通常是被其它进程占用）。
    /// </summary>
    public bool SetText(string? value, int clearAfterSeconds)
    {
        if (string.IsNullOrEmpty(value)) return false;

        try
        {
            // 用 SetDataObject(copy: true) 而不是 SetText：
            // SetText 的内容在应用退出后会从剪贴板消失（WPF 默认延迟渲染），
            // copy:true 会立即把数据实体写进去，用户退出程序后仍能粘贴。
            var data = new DataObject(DataFormats.UnicodeText, value);
            Clipboard.SetDataObject(data, copy: true);

            _guard.Track(value, clearAfterSeconds);
            return true;
        }
        catch (Exception)
        {
            // COMException / ExternalException：剪贴板被占用，或当前无桌面会话
            return false;
        }
    }

    /// <summary>到点则清理（<paramref name="force"/> 为 true 时立即清理，用于锁定/退出）。</summary>
    public void Tick(bool force) => _guard.Tick(force);

    /// <summary>距自动清除的剩余时间。</summary>
    public TimeSpan? TimeLeft => _guard.TimeLeft;

    // ---- 注入给 ClipboardGuard 的安全实现 ----

    private static string? ReadTextSafe()
    {
        try
        {
            // WPF 在剪贴板为空/被占用时抛异常，而不是返回 null
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch
        {
            return null;
        }
    }

    private static void ClearSafe()
    {
        try
        {
            Clipboard.Clear();
        }
        catch
        {
            // 被占用时忽略：ClipboardGuard 已经把 _text 置空，不会无限重试
        }
    }
}
