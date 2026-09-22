using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;

namespace ApiKeyManager.Wpf.Views;

/// <summary>主题化消息框的语义类型，决定徽标图标与强调色。</summary>
public enum MessageKind
{
    /// <summary>一般信息。</summary>
    Info,

    /// <summary>警告：需要注意但未失败。</summary>
    Warning,

    /// <summary>错误：操作失败。</summary>
    Error,

    /// <summary>到期提醒：用警告色 + 时钟图标。</summary>
    Expiry,
}

/// <summary>
/// 替代 <c>System.Windows.MessageBox</c> 的主题化对话框。
///
/// MessageBox 是 Win32 对话框，用系统配色，不跟随应用主题；
/// 在深色模式下会弹出一个刺眼的亮色窗口，与整个界面割裂。
/// </summary>
public partial class MessageDialog : Window
{
    private MessageDialog(MessageKind kind, string title, string message, bool confirm)
    {
        InitializeComponent();

        Title = title;
        TxtHeading.Text = title;
        TxtBody.Text = message;

        (BadgeIcon.Text, string brushKey) = kind switch
        {
            MessageKind.Warning => ("\uE7BA", "Warning"),
            MessageKind.Error => ("\uEA39", "Danger"),
            MessageKind.Expiry => ("\uE823", "Warning"),   // 时钟
            _ => ("\uE946", "Accent"),
        };

        if (TryFindResource(brushKey) is Brush b)
        {
            Badge.Background = b;
            BadgeIcon.Foreground = Brushes.White;
        }

        if (confirm)
        {
            BtnNo.Visibility = Visibility.Visible;
            BtnOk.Content = "继续";
        }

        // 无障碍：读屏软件需要能报出这段内容
        AutomationProperties.SetName(this, title);
        AutomationProperties.SetHelpText(TxtBody, message);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    // ---------------- 静态入口 ----------------

    public static void Show(Window owner, MessageKind kind, string title, string message)
    {
        var dlg = new MessageDialog(kind, title, message, confirm: false) { Owner = owner };
        dlg.ShowDialog();
    }

    public static bool Confirm(Window owner, string title, string message, MessageKind kind = MessageKind.Warning)
    {
        var dlg = new MessageDialog(kind, title, message, confirm: true) { Owner = owner };
        return dlg.ShowDialog() == true;
    }
}
