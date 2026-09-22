using System;
using System.Drawing;
using System.Windows.Forms;

namespace ApiKeyManager;

// ---------------- 通用输入对话框 ----------------

public sealed class PromptForm : Form
{
    private readonly FieldBox _input;
    private readonly FieldBox? _confirm;
    private readonly Label _error;
    private readonly Func<string?, string?>? _validate;

    private PromptForm(string title, string prompt, bool isPassword, bool needConfirm, string? prefill, Func<string?, string?>? validate)
    {
        _validate = validate;
        var p = Theme.P;

        Text = title;
        Icon = AppIcon.Get;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = p.Back;
        Padding = new Padding(16);
        ClientSize = new Size(500, needConfirm ? 430 : 356);

        var card = new Card { Dock = DockStyle.Fill };

        var badge = new GlyphBadge { Glyph = Theme.IcoLock, Location = new Point(24, 22) };
        var lblTitle = new Label
        {
            Text = title,
            Font = new Font("Segoe UI Semibold", 14f),
            ForeColor = p.Text, BackColor = p.Surface,
            AutoSize = true, Location = new Point(86, 26)
        };
        var lblPrompt = new Label
        {
            Text = prompt,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = p.TextDim, BackColor = p.Surface,
            AutoSize = true, Location = new Point(24, 92)
        };

        _input = new FieldBox { Location = new Point(24, 146), Width = 420, Height = 38 };
        _input.Label = prompt;
        _input.IsSecret = isPassword;
        _input.Text2 = prefill ?? "";
        _input.Placeholder = isPassword ? "密码" : "";

        card.Controls.Add(badge);
        card.Controls.Add(lblTitle);
        card.Controls.Add(lblPrompt);
        card.Controls.Add(_input);

        int errY;
        if (needConfirm)
        {
            var lbl2 = new Label
            {
                Text = "再次输入以确认",
                ForeColor = p.TextDim, BackColor = p.Surface,
                AutoSize = true, Location = new Point(24, 198)
            };
            _confirm = new FieldBox { Location = new Point(24, 224), Width = 420, Height = 38 };
            _confirm.Label = "再次输入以确认";
            _confirm.IsSecret = isPassword;
            _confirm.Placeholder = "重复输入";
            card.Controls.Add(lbl2);
            card.Controls.Add(_confirm);
            errY = 282;
        }
        else
        {
            errY = 200;
        }

        _error = new Label
        {
            ForeColor = p.Danger, BackColor = p.Surface,
            AutoSize = true, Location = new Point(24, errY)
        };
        card.Controls.Add(_error);

        int cardH = ClientSize.Height - 32;
        int btnY = cardH - 58;
        int okX = 468 - 24 - 88;
        var ok = new ThemedButton
        {
            Text = "确定", Kind = BtnKind.Primary, Width = 88, Location = new Point(okX, btnY)
        };
        var cancel = new ThemedButton
        {
            Text = "取消", Kind = BtnKind.Subtle, Width = 88,
            DialogResult = DialogResult.Cancel, Location = new Point(okX - 98, btnY)
        };
        ok.Click += (_, _) => OnOk();
        card.Controls.Add(ok);
        card.Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(card);
    }

    private string Value => _input.Text2;

    private void OnOk()
    {
        string? err = _validate?.Invoke(_input.Text2);
        if (err == null && _confirm != null && _confirm.Text2 != _input.Text2)
            err = "两次输入的密码不一致。";
        if (err != null)
        {
            _error.Text = err;
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    public static string? AskPassword(IWin32Window owner, string title, string prompt, string? prefill = null)
    {
        using var f = new PromptForm(title, prompt, true, false, prefill,
            v => string.IsNullOrEmpty(v) ? "不能为空。" : null);
        return f.ShowDialog(owner) == DialogResult.OK ? f.Value : null;
    }

    public static string? AskNewPassword(IWin32Window owner, string title, string prompt)
    {
        using var f = new PromptForm(title, prompt, true, true, null,
            v => (v ?? "").Length < 8 ? "密码至少 8 个字符。" : null);
        return f.ShowDialog(owner) == DialogResult.OK ? f.Value : null;
    }
}

// ---------------- 修改主密码 ----------------

public sealed class PasswordChangeForm : Form
{
    private readonly string _current;
    private readonly FieldBox _old;
    private readonly FieldBox _new;
    private readonly FieldBox _confirm;
    private readonly Label _error;

    private PasswordChangeForm(string current)
    {
        _current = current;
        var p = Theme.P;

        Text = "修改主密码";
        Icon = AppIcon.Get;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = p.Back;
        Padding = new Padding(16);
        ClientSize = new Size(548, 420);

        var card = new Card { Dock = DockStyle.Fill };
        var badge = new GlyphBadge { Glyph = Theme.IcoSettings, Location = new Point(24, 22) };
        var lblTitle = new Label
        {
            Text = "修改主密码",
            Font = new Font("Segoe UI Semibold", 14f),
            ForeColor = p.Text, BackColor = p.Surface,
            AutoSize = true, Location = new Point(86, 26)
        };
        var lblSub = new Label
        {
            Text = "保存后整个库将用新主密码重新加密",
            ForeColor = p.TextDim, BackColor = p.Surface,
            AutoSize = true, Location = new Point(86, 54)
        };
        card.Controls.Add(badge);
        card.Controls.Add(lblTitle);
        card.Controls.Add(lblSub);

        _old = Row(card, 1, "当前主密码", isPassword: true);
        _new = Row(card, 2, "新主密码（≥8 位）", isPassword: true);
        _confirm = Row(card, 3, "确认新主密码", isPassword: true);

        _error = new Label
        {
            ForeColor = p.Danger, BackColor = p.Surface,
            AutoSize = true, Location = new Point(24, 290)
        };
        card.Controls.Add(_error);

        int okX = 516 - 24 - 88;
        var ok = new ThemedButton { Text = "确认修改", Kind = BtnKind.Primary, Width = 108, Location = new Point(okX - 20, 340) };
        var cancel = new ThemedButton { Text = "取消", Kind = BtnKind.Subtle, Width = 88, DialogResult = DialogResult.Cancel, Location = new Point(okX - 128, 340) };
        ok.Click += (_, _) => OnOk();
        card.Controls.Add(ok);
        card.Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(card);
    }

    private FieldBox Row(Card card, int index, string label, bool isPassword)
    {
        var p = Theme.P;
        int y = 96 + (index - 1) * 62;
        var lbl = new Label
        {
            Text = label,
            ForeColor = p.TextDim, BackColor = p.Surface,
            AutoSize = true, Location = new Point(24, y + 9)
        };
        var box = new FieldBox { Location = new Point(196, y), Width = 296, Height = 38 };
        box.Label = label;
        box.IsSecret = isPassword;
        card.Controls.Add(lbl);
        card.Controls.Add(box);
        return box;
    }

    private void OnOk()
    {
        if (_old.Text2 != _current) { _error.Text = "当前主密码不正确。"; return; }
        if (_new.Text2.Length < 8) { _error.Text = "新密码至少 8 个字符。"; return; }
        if (_new.Text2 != _confirm.Text2) { _error.Text = "两次输入的新密码不一致。"; return; }
        DialogResult = DialogResult.OK;
        Close();
    }

    public static string? Run(IWin32Window owner, string currentPassword)
    {
        using var f = new PasswordChangeForm(currentPassword);
        return f.ShowDialog(owner) == DialogResult.OK ? f._new.Text2 : null;
    }
}

// ---------------- 记录编辑 ----------------

public sealed class EntryEditForm : Form
{
    private readonly ApiEntry _entry;
    private readonly FieldBox _name;
    private readonly FieldBox _provider;
    private readonly FieldBox _apiKey;
    private readonly FieldBox _baseUrl;
    private readonly FieldBox _model;
    private readonly FieldBox _tags;
    private readonly FieldBox _notes;
    private readonly CheckBox _showKey;
    private readonly DateTimePicker _expiry;
    private readonly CheckBox _noExpiry;
    private readonly Label _expiryHint;
    private readonly Label _error;

    private EntryEditForm(ApiEntry entry, bool isNew)
    {
        _entry = entry;
        var p = Theme.P;

        Text = isNew ? "新增 API Key" : "编辑 API Key";
        Icon = AppIcon.Get;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = p.Back;
        Padding = new Padding(16);
        ClientSize = new Size(548, 780);

        var card = new Card { Dock = DockStyle.Fill };
        var badge = new GlyphBadge { Glyph = isNew ? Theme.IcoAdd : Theme.IcoEdit, Location = new Point(24, 22) };
        var lblTitle = new Label
        {
            Text = isNew ? "新增 API Key" : $"编辑「{entry.Name}」",
            Font = new Font("Segoe UI Semibold", 14f),
            ForeColor = p.Text, BackColor = p.Surface,
            AutoSize = true, Location = new Point(86, 26)
        };
        var lblSub = new Label
        {
            Text = "保存后立即加密写入库文件",
            ForeColor = p.TextDim, BackColor = p.Surface,
            AutoSize = true, Location = new Point(86, 54)
        };
        card.Controls.Add(badge);
        card.Controls.Add(lblTitle);
        card.Controls.Add(lblSub);

        _name = Row(card, 0, "名称 *");
        _provider = Row(card, 1, "提供商");
        _apiKey = Row(card, 2, "API Key", out _showKey);
        _baseUrl = Row(card, 3, "Base URL");
        _model = Row(card, 4, "默认模型");
        _tags = Row(card, 5, "标签");

        _name.Text2 = entry.Name;
        _provider.Text2 = entry.Provider;
        _apiKey.Text2 = entry.ApiKey;
        _baseUrl.Text2 = entry.BaseUrl;
        _model.Text2 = entry.Model;
        _tags.Text2 = entry.Tags;
        _apiKey.IsSecret = true;
        _showKey.CheckedChanged += (_, _) => _apiKey.IsSecret = !_showKey.Checked;
        _baseUrl.Placeholder = "https://...";
        _tags.Placeholder = "用逗号分隔，如：AI, 生产";

        // ---- 到期日 ----
        var lblExpiry = new Label
        {
            Text = "到期日",
            ForeColor = p.TextDim, BackColor = p.Surface,
            AutoSize = true, Location = new Point(24, 96 + 6 * 56 + 9)
        };
        _expiry = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd",
            Location = new Point(136, 96 + 6 * 56),
            Width = 160,
            Font = Theme.InputFont,
            AccessibleName = "到期日",
            AccessibleDescription = "密钥到期日期，格式 年-月-日"
        };
        _noExpiry = new CheckBox
        {
            Text = "不设置到期提醒",
            AutoSize = true,
            ForeColor = p.TextDim,
            BackColor = p.Surface,
            Location = new Point(306, 96 + 6 * 56 + 8),
            AccessibleName = "不设置到期提醒",
            AccessibleDescription = "勾选后该密钥不参与到期提醒"
        };
        _expiryHint = new Label
        {
            ForeColor = p.TextDim, BackColor = p.Surface,
            AutoSize = true, Location = new Point(136, 96 + 6 * 56 + 32)
        };
        card.Controls.Add(lblExpiry);
        card.Controls.Add(_expiry);
        card.Controls.Add(_noExpiry);
        card.Controls.Add(_expiryHint);

        // 已设置的到期日：回填。未设置则默认"不提醒"（避免给老记录凭空造出到期日）
        if (entry.ExpiresUtc is { } saved)
        {
            _expiry.Value = saved.Date;
            _noExpiry.Checked = false;
        }
        else
        {
            _expiry.Value = DateTime.Today.AddMonths(3);
            _noExpiry.Checked = true;
        }

        _noExpiry.CheckedChanged += (_, _) => SyncExpiryEnabled();
        _expiry.ValueChanged += (_, _) => SyncExpiryEnabled();
        SyncExpiryEnabled();

        // 备注
        int notesY = 96 + 7 * 56;
        var lblNotes = new Label
        {
            Text = "备注",
            ForeColor = p.TextDim, BackColor = p.Surface,
            AutoSize = true, Location = new Point(24, notesY + 12)
        };
        _notes = new FieldBox(true) { Location = new Point(136, notesY), Width = 356, Height = 100 };
        _notes.Label = "备注";
        _notes.Text2 = entry.Notes;
        card.Controls.Add(lblNotes);
        card.Controls.Add(_notes);

        int metaY = notesY + 114;
        var lblMeta = new Label
        {
            Text = isNew
                ? "创建时间：保存时生成"
                : $"创建：{entry.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}　更新：{entry.UpdatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}",
            ForeColor = p.TextDim, BackColor = p.Surface,
            AutoSize = true, Location = new Point(24, metaY)
        };
        card.Controls.Add(lblMeta);

        _error = new Label
        {
            ForeColor = p.Danger, BackColor = p.Surface,
            AutoSize = true, Location = new Point(24, metaY + 28)
        };
        card.Controls.Add(_error);

        int btnY = metaY + 78;
        int okX = 516 - 24 - 88;
        var ok = new ThemedButton { Text = "保存", Kind = BtnKind.Primary, Width = 88, Location = new Point(okX, btnY) };
        var cancel = new ThemedButton { Text = "取消", Kind = BtnKind.Subtle, Width = 88, DialogResult = DialogResult.Cancel, Location = new Point(okX - 98, btnY) };
        ok.Click += (_, _) => OnOk();
        card.Controls.Add(ok);
        card.Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(card);
    }

    /// <summary>到期控件联动：勾了「不设置」就禁用日期选择，并给出人性化提示。</summary>
    private void SyncExpiryEnabled()
    {
        _expiry.Enabled = !_noExpiry.Checked;

        if (_noExpiry.Checked)
        {
            _expiryHint.Text = "不提醒到期";
            _expiryHint.ForeColor = Theme.P.TextDim;
            return;
        }

        var info = ExpiryPolicy.Evaluate(_expiry.Value, DateTime.Now);
        _expiryHint.Text = info.Describe();
        _expiryHint.ForeColor = info.State == ExpiryState.Expired
            ? Theme.P.Danger
            : info.State == ExpiryState.ExpiringSoon ? Theme.P.Accent : Theme.P.TextDim;
    }

    private FieldBox Row(Card card, int index, string label) => Row(card, index, label, out _);

    private FieldBox Row(Card card, int index, string label, out CheckBox show)
    {
        var p = Theme.P;
        int y = 96 + index * 56;
        var lbl = new Label
        {
            Text = label,
            ForeColor = p.TextDim, BackColor = p.Surface,
            AutoSize = true, Location = new Point(24, y + 9)
        };
        var box = new FieldBox { Location = new Point(136, y), Width = 356, Height = 38 };
        box.Label = label;
        card.Controls.Add(lbl);
        card.Controls.Add(box);

        show = new CheckBox
        {
            Text = "显示",
            AutoSize = true,
            AccessibleName = $"{label}：显示明文",
            ForeColor = p.TextDim,
            BackColor = p.Surface,
            Location = new Point(440, y + 10),
            Visible = false
        };
        if (label == "API Key")
        {
            box.Width = 288;
            show.Visible = true;
        }
        card.Controls.Add(show);
        return box;
    }

    private void OnOk()
    {
        if (string.IsNullOrWhiteSpace(_name.Text2))
        {
            _error.Text = "名称不能为空。";
            _name.Focus();
            return;
        }

        _entry.Name = _name.Text2.Trim();
        _entry.Provider = _provider.Text2.Trim();
        _entry.ApiKey = _apiKey.Text2.Trim();
        _entry.BaseUrl = _baseUrl.Text2.Trim();
        _entry.Model = _model.Text2.Trim();
        _entry.Tags = _tags.Text2.Trim();
        _entry.Notes = _notes.Text2;

        // 勾了「不设置」就存 null，而不是存一个未来日期
        _entry.ExpiresUtc = _noExpiry.Checked ? null : _expiry.Value.Date;

        DialogResult = DialogResult.OK;
        Close();
    }

    public static DialogResult Edit(IWin32Window owner, ApiEntry entry, bool isNew)
    {
        using var f = new EntryEditForm(entry, isNew);
        return f.ShowDialog(owner);
    }
}
