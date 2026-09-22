using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace ApiKeyManager.Wpf.Views;

/// <summary>密码对话框的三种用途。</summary>
public enum PasswordDialogMode
{
    /// <summary>只问一次（解锁 / 输入备份密码）。</summary>
    Ask,

    /// <summary>创建新密码：两次输入 + 最少 N 位。</summary>
    Create,

    /// <summary>修改密码：当前密码 + 新密码 + 确认（一个窗口三个字段）。</summary>
    Change,
}

public partial class PasswordDialog : Window
{
    private readonly PasswordDialogMode _mode;
    private readonly Func<string, bool>? _verifyCurrent;
    private readonly int _minLength;

    public string? Result { get; private set; }

    private PasswordDialog(
        PasswordDialogMode mode,
        string title,
        string prompt,
        string? prefill,
        int minLength,
        Func<string, bool>? verifyCurrent)
    {
        InitializeComponent();

        _mode = mode;
        _minLength = minLength;
        _verifyCurrent = verifyCurrent;

        TxtTitle.Text = title;
        TxtPrompt.Text = prompt;

        switch (mode)
        {
            case PasswordDialogMode.Ask:
                TxtSubtitle.Text = "密码仅用于本次解密，不会写入磁盘";
                LblPwd1.Text = "密码";
                break;

            case PasswordDialogMode.Create:
                TxtSubtitle.Text = $"至少 {minLength} 位";
                LblPwd1.Text = "设置主密码";
                LblPwd2.Text = "再次输入以确认";
                Block2.Visibility = Visibility.Visible;
                break;

            case PasswordDialogMode.Change:
                TxtSubtitle.Text = $"新密码至少 {minLength} 位";
                LblPwd1.Text = "当前主密码";
                LblPwd2.Text = "新主密码";
                Block2.Visibility = Visibility.Visible;
                Block3.Visibility = Visibility.Visible;
                break;
        }

        if (!string.IsNullOrEmpty(prefill))
            Pwd1.Password = prefill;

        // 无障碍：自绘模板下读屏必须靠 AutomationProperties 才能识别字段含义
        AutomationProperties.SetName(Pwd1, LblPwd1.Text);
        AutomationProperties.SetHelpText(Pwd1, "机密输入，内容不会显示");
        if (mode != PasswordDialogMode.Ask)
        {
            AutomationProperties.SetName(Pwd2, mode == PasswordDialogMode.Change ? "新主密码" : "再次输入以确认");
            AutomationProperties.SetHelpText(Pwd2, "机密输入，内容不会显示");
        }
        if (mode == PasswordDialogMode.Change)
        {
            AutomationProperties.SetName(Pwd3, "再次输入新密码以确认");
            AutomationProperties.SetHelpText(Pwd3, "机密输入，内容不会显示");
        }

        BtnOk.Click += (_, _) => OnOk();
        BtnCancel.Click += (_, _) => { Result = null; DialogResult = false; };
        Loaded += (_, _) =>
        {
            Pwd1.Focus();
            Pwd1.SelectAll();
        };
    }

    private void OnOk()
    {
        string p1 = Pwd1.Password;
        string p2 = Pwd2.Password;
        string p3 = Pwd3.Password;

        switch (_mode)
        {
            case PasswordDialogMode.Ask:
                if (p1.Length == 0) { Fail("密码不能为空。", Pwd1); return; }
                Result = p1;
                break;

            case PasswordDialogMode.Create:
                if (p1.Length < _minLength) { Fail($"密码至少 {_minLength} 位。", Pwd1); return; }
                if (!string.Equals(p1, p2, StringComparison.Ordinal)) { Fail("两次输入的密码不一致。", Pwd2); return; }
                Result = p1;
                break;

            case PasswordDialogMode.Change:
                if (_verifyCurrent != null && !_verifyCurrent(p1))
                {
                    Fail("当前主密码不正确。", Pwd1);
                    return;
                }
                if (p2.Length < _minLength) { Fail($"新密码至少 {_minLength} 位。", Pwd2); return; }
                if (string.Equals(p2, p1, StringComparison.Ordinal))
                {
                    Fail("新密码不能与当前密码相同。", Pwd2);
                    return;
                }
                if (!string.Equals(p2, p3, StringComparison.Ordinal))
                {
                    Fail("两次输入的新密码不一致。", Pwd3);
                    return;
                }
                Result = p2;
                break;
        }

        DialogResult = true;
    }

    private void Fail(string message, Control focus)
    {
        TxtError.Text = message;
        TxtError.Visibility = Visibility.Visible;
        focus.Focus();
    }

    // ---------------- 静态工厂 ----------------

    private static Window? OwnerWindow => Application.Current?.Windows.Count > 0
        ? Application.Current.MainWindow
        : null;

    /// <summary>
    /// 供 --dialogcheck 自检使用：构造指定模式的实例但不显示，
    /// 以便离屏排版后验证按钮是否被裁。
    /// </summary>
    internal static PasswordDialog CreateForInspection(
        PasswordDialogMode mode, string title = "自检", string prompt = "自检提示", int minLength = 8) =>
        new(mode, title, prompt, null, minLength, null);

    /// <summary>询问一次密码。取消返回 null。</summary>
    public static string? Ask(string title, string prompt, string? prefill = null)
    {
        var dlg = new PasswordDialog(PasswordDialogMode.Ask, title, prompt, prefill, 0, null)
        {
            Owner = OwnerWindow
        };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }

    /// <summary>设置新主密码（两次输入 + 长度校验）。取消返回 null。</summary>
    public static string? Create(string title, string prompt, int minLength = 8)
    {
        var dlg = new PasswordDialog(PasswordDialogMode.Create, title, prompt, null, minLength, null)
        {
            Owner = OwnerWindow
        };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }

    /// <summary>修改主密码（验证当前密码 + 新密码 + 确认）。取消返回 null。</summary>
    public static string? Change(string currentPassword, int minLength = 8)
    {
        var dlg = new PasswordDialog(
            PasswordDialogMode.Change,
            "修改主密码",
            "修改后全部记录会用新密码重新加密。\n忘记新密码将无法恢复数据。",
            null,
            minLength,
            p => string.Equals(p, currentPassword, StringComparison.Ordinal))
        {
            Owner = OwnerWindow
        };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }
}
