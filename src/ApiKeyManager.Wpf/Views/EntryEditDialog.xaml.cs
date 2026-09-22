using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ApiKeyManager;

namespace ApiKeyManager.Wpf.Views;

public partial class EntryEditDialog : Window
{
    private readonly ApiEntry _entry;
    private readonly bool _isNew;

    private EntryEditDialog(ApiEntry entry, bool isNew)
    {
        InitializeComponent();

        _entry = entry;
        _isNew = isNew;

        Title = isNew ? "新增 API Key" : "编辑 API Key";
        TxtTitle.Text = Title;

        // ---- 回填 ----
        TxtName.Text = entry.Name;
        TxtProvider.Text = entry.Provider;
        PwdKey.Password = entry.ApiKey;
        TxtKeyPlain.Text = entry.ApiKey;
        TxtBaseUrl.Text = entry.BaseUrl;
        TxtModel.Text = entry.Model;
        TxtTags.Text = entry.Tags;
        TxtNotes.Text = entry.Notes;

        // 到期日：未设置则默认"不提醒"，避免给老记录凭空造出到期日
        if (entry.ExpiresUtc is { } saved)
        {
            PickExpiry.SelectedDate = saved.Date;
            ChkNoExpiry.IsChecked = false;
        }
        else
        {
            PickExpiry.SelectedDate = DateTime.Today.AddMonths(3);
            ChkNoExpiry.IsChecked = true;
        }

        // ---- 无障碍：自绘模板下必须显式声明字段名 ----
        SetName(TxtName, "名称");
        SetName(TxtProvider, "提供商");
        SetName(PwdKey, "API Key");
        SetName(TxtKeyPlain, "API Key");
        SetName(TxtBaseUrl, "Base URL");
        SetName(TxtModel, "默认模型");
        SetName(TxtTags, "标签");
        SetName(TxtNotes, "备注");
        SetName(PickExpiry, "到期日");
        SetName(ChkNoExpiry, "不设置到期提醒");
        SetName(ChkShowKey, "显示密钥明文");
        AutomationProperties.SetHelpText(PwdKey, "机密输入，内容不会显示");

        AutomationProperties.SetName(PickExpiry, "到期日");
        AutomationProperties.SetHelpText(PickExpiry, "格式 年-月-日");

        // ---- 交互 ----
        ChkShowKey.Checked += (_, _) => ToggleKeyVisible(true);
        ChkShowKey.Unchecked += (_, _) => ToggleKeyVisible(false);

        ChkNoExpiry.Checked += (_, _) => SyncExpiryEnabled();
        ChkNoExpiry.Unchecked += (_, _) => SyncExpiryEnabled();
        PickExpiry.SelectedDateChanged += (_, _) => SyncExpiryEnabled();

        BtnSave.Click += (_, _) => OnSave();
        BtnCancel.Click += (_, _) => DialogResult = false;

        SyncExpiryEnabled();

        Loaded += (_, _) =>
        {
            TxtName.Focus();
            TxtName.SelectAll();
        };
    }

    private static void SetName(DependencyObject o, string name) =>
        AutomationProperties.SetName(o, name);

    private void ToggleKeyVisible(bool show)
    {
        if (show)
        {
            TxtKeyPlain.Text = PwdKey.Password;
            PwdKey.Visibility = Visibility.Collapsed;
            TxtKeyPlain.Visibility = Visibility.Visible;
        }
        else
        {
            PwdKey.Password = TxtKeyPlain.Text;
            TxtKeyPlain.Visibility = Visibility.Collapsed;
            PwdKey.Visibility = Visibility.Visible;
        }
    }

    /// <summary>当前 Key 文本（两个控件中可见的那个）。</summary>
    private string CurrentKey => ChkShowKey.IsChecked == true ? TxtKeyPlain.Text : PwdKey.Password;

    private void SyncExpiryEnabled()
    {
        bool off = ChkNoExpiry.IsChecked == true;
        PickExpiry.IsEnabled = !off;

        if (off)
        {
            TxtExpiryHint.Text = "不提醒到期";
            TxtExpiryHint.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
            return;
        }

        var when = PickExpiry.SelectedDate ?? DateTime.Today;
        var info = ExpiryPolicy.Evaluate(when, DateTime.Now);
        TxtExpiryHint.Text = info.Describe();

        string brush = info.State == ExpiryState.Expired
            ? "ExpiryExpired"
            : info.State == ExpiryState.ExpiringSoon ? "ExpirySoon" : "TextDim";
        TxtExpiryHint.SetResourceReference(TextBlock.ForegroundProperty, brush);
    }

    private void OnSave()
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            ShowError("名称不能为空。");
            TxtName.Focus();
            return;
        }

        _entry.Name = TxtName.Text.Trim();
        _entry.Provider = TxtProvider.Text.Trim();
        _entry.ApiKey = CurrentKey.Trim();
        _entry.BaseUrl = TxtBaseUrl.Text.Trim();
        _entry.Model = TxtModel.Text.Trim();
        _entry.Tags = TxtTags.Text.Trim();
        _entry.Notes = TxtNotes.Text;

        // 勾了「不设置」就存 null，而不是一个未来日期
        _entry.ExpiresUtc = ChkNoExpiry.IsChecked == true
            ? null
            : (PickExpiry.SelectedDate ?? DateTime.Today).Date;

        DialogResult = true;
    }

    private void ShowError(string message)
    {
        TxtError.Text = message;
        TxtError.Visibility = Visibility.Visible;
    }

    /// <summary>打开编辑对话框。返回 true 表示已保存（entry 已被就地修改）。</summary>
    public static bool Edit(ApiEntry entry, bool isNew)
    {
        var dlg = new EntryEditDialog(entry, isNew)
        {
            Owner = Application.Current?.MainWindow
        };
        return dlg.ShowDialog() == true;
    }
}
