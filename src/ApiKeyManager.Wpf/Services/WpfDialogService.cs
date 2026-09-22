using System;
using System.Windows;
using ApiKeyManager.Wpf.Views;
using Microsoft.Win32;

namespace ApiKeyManager.Wpf.Services;

/// <summary>
/// <see cref="IDialogService"/> 的 WPF 实现。
///
/// 这是 ViewModel 与平台 UI 之间唯一的接缝：
/// 所有 MessageBox / 文件对话框 / 窗口切换都集中在这里，
/// 因此 MainViewModel 保持平台无关、可单测。
/// </summary>
public sealed class WpfDialogService : IDialogService
{
    private readonly Window _owner;

    public WpfDialogService(Window owner)
    {
        _owner = owner;
    }

    private Window Owner => _owner.IsLoaded ? _owner : _owner;

    // ---------------- 密码 ----------------

    public string? AskPassword(string title, string prompt, string? prefill = null) =>
        PasswordDialog.Ask(title, prompt, prefill);

    public string? AskNewPassword(string title, string prompt) =>
        PasswordDialog.Create(title, prompt);

    public string? ChangePassword(string currentPassword) =>
        PasswordDialog.Change(currentPassword);

    // ---------------- 记录编辑 ----------------

    public bool EditEntry(ApiKeyManager.ApiEntry entry, bool isNew) =>
        EntryEditDialog.Edit(entry, isNew);

    // ---------------- 导入方式 ----------------

    public ImportChoice AskImportMode(int incomingCount, int currentCount)
    {
        var dlg = new ImportModeDialog(incomingCount, currentCount) { Owner = _owner };
        dlg.ShowDialog();

        return dlg.Choice;
    }

    // ---------------- 消息框 ----------------

    public void ShowInfo(string message, string title) =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(string message, string title) =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowError(string message, string title) =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message, string title) =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;

    // ---------------- 文件对话框 ----------------

    public string? PickOpenFile(string title, string filter)
    {
        var dlg = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
        };
        return dlg.ShowDialog(_owner) == true ? dlg.FileName : null;
    }

    public string? PickSaveFile(string title, string filter, string defaultExt, string suggestedName)
    {
        var dlg = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = defaultExt,
            FileName = suggestedName,
            OverwritePrompt = true,
        };
        return dlg.ShowDialog(_owner) == true ? dlg.FileName : null;
    }

    // ---------------- 应用级 ----------------

    public void Dispatch(Action action) =>
        _owner.Dispatcher.BeginInvoke(action, System.Windows.Threading.DispatcherPriority.Background);

    public void RequestShutdown() => Application.Current?.Shutdown();
}
