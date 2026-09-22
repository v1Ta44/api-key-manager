using System;
using ApiKeyManager;
using ApiKeyManager.Wpf.Views;

namespace ApiKeyManager.Wpf.Services;

/// <summary>导入方式。</summary>
public enum ImportChoice
{
    /// <summary>替换当前库（覆盖前会自动备份）。</summary>
    Replace,

    /// <summary>按 Id 合并（跳过重复）。</summary>
    Merge,

    /// <summary>放弃导入。</summary>
    Cancel,
}

/// <summary>
/// 平台交互抽象。
///
/// 存在的意义：把"弹窗 / 文件对话框 / 关窗"从 ViewModel 里隔离出去，
/// 这样 MainViewModel 不依赖任何 WPF 窗口类型，可以被单测替换成假实现。
/// WinForms 版把这些逻辑全塞在 MainForm 里，导致 CRUD 流程完全无法测试。
/// </summary>
public interface IDialogService
{
    /// <summary>询问主密码（密码框，不回显）。取消返回 null。</summary>
    string? AskPassword(string title, string prompt, string? prefill = null);

    /// <summary>设置新主密码（两次输入 + 强度校验）。取消返回 null。</summary>
    string? AskNewPassword(string title, string prompt);

    /// <summary>修改主密码（校验旧密码）。取消返回 null，成功返回新密码。</summary>
    string? ChangePassword(string currentPassword);

    /// <summary>编辑记录。返回 true 表示用户点了保存（直接写回 entry）。</summary>
    bool EditEntry(ApiEntry entry, bool isNew);

    /// <summary>选择导入方式。</summary>
    ImportChoice AskImportMode(int incomingCount, int currentCount);

    // ---- 通用消息框 ----

    void ShowInfo(string message, string title);

    void ShowWarning(string message, string title);

    void ShowError(string message, string title);

    /// <summary>是否确认（是/否）。</summary>
    bool Confirm(string message, string title);

    // ---- 文件对话框 ----

    /// <summary>返回 null 表示用户取消。</summary>
    string? PickOpenFile(string title, string filter);

    /// <summary>返回 null 表示用户取消。</summary>
    string? PickSaveFile(string title, string filter, string defaultExt, string suggestedName);

    // ---- 应用级 ----

    /// <summary>在 UI 线程上排队执行（用于锁定后立刻重新弹出解锁框）。</summary>
    void Dispatch(Action action);

    /// <summary>请求关闭应用（用户取消了解锁）。</summary>
    void RequestShutdown();
}
