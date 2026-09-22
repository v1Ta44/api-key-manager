using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace ApiKeyManager;

public sealed class MainForm : Form
{
    private readonly string? _autoPassword;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly ClipboardGuard _clipboard;

    private DataGridView _grid = null!;
    private FieldBox _search = null!;
    private Label _statusLabel = null!;
    private PillLabel _lockPill = null!;
    private ThemedButton _btnToggleKeys = null!;
    private ThemedButton _btnTheme = null!;
    private NumericUpDown _autoLock = null!;

    private EntryRepository _repo = null!;
    private AppSettings _settings = new();
    private string _dataDir = "";
    private string _vaultPath = "";
    private string? _password;
    private bool _unlocked;
    private bool _showKeys;
    private DateTime _lastActivity = DateTime.UtcNow;

    public MainForm(string? autoPassword = null)
    {
        _autoPassword = autoPassword;

        Icon = AppIcon.Get;
        Text = "API Key 管理器";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 720);
        MinimumSize = new Size(980, 560);

        (_dataDir, _vaultPath) = DataPaths.Resolve();
        _repo = new EntryRepository(_vaultPath);
        _settings = SettingsStore.Load(_dataDir);
        Theme.Current = string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase)
            ? AppTheme.Dark : AppTheme.Light;
        _showKeys = _settings.ShowKeys;

        _clipboard = new ClipboardGuard(
            readText: () => Clipboard.ContainsText() ? Clipboard.GetText() : null,
            clear: () => Clipboard.Clear());

        RebuildUi();

        _timer.Tick += OnTimer;
        _timer.Start();
        Application.AddMessageFilter(new ActivityFilter(() => _lastActivity = DateTime.UtcNow));
        Shown += (_, _) => BeginUnlock();
    }

    // ---------------- 界面搭建 ----------------

    private void RebuildUi()
    {
        SuspendLayout();
        while (Controls.Count > 0)
        {
            var c = Controls[0];
            Controls.Remove(c);
            c.Dispose();
        }

        var p = Theme.P;
        BackColor = p.Back;
        Padding = new Padding(16, 14, 16, 8);
        BuildLayout();
        ResumeLayout(true);
        ApplyStateTexts();
        RefreshGrid();
    }

    private void BuildLayout()
    {
        var p = Theme.P;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        // ===== 顶部卡片：标题 + 工具栏 =====
        var topCard = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12), AutoSize = true };
        var topTable = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 2 };
        topTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        topTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 标题区
        var titleBlock = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
        var badge = new GlyphBadge { Glyph = Theme.IcoLock, Size = new Size(46, 46), Margin = new Padding(0, 2, 12, 0) };
        var titleStack = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 0, 0, 0)
        };
        var lblTitle = new Label
        {
            Text = "API Key 管理器",
            Font = new Font("Segoe UI Semibold", 15f),
            ForeColor = p.Text,
            BackColor = p.Surface,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2)
        };
        var lblSub = new Label
        {
            Text = "本地加密 · 离线存储 · AES-256-GCM",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = p.TextDim,
            BackColor = p.Surface,
            AutoSize = true,
            Margin = new Padding(2, 0, 0, 0)
        };
        titleStack.Controls.Add(lblTitle);
        titleStack.Controls.Add(lblSub);
        titleBlock.Controls.Add(badge);
        titleBlock.Controls.Add(titleStack);
        topTable.Controls.Add(titleBlock, 0, 0);

        // 右上：主题 / 修改主密码 / 锁定
        var headRight = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft,
            Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = new Padding(0, 4, 0, 0)
        };
        _btnTheme = Btn("", "", BtnKind.Ghost, 86, (_, _) => ToggleTheme());
        headRight.Controls.Add(_btnTheme);
        headRight.Controls.Add(Btn("修改主密码", Theme.IcoSettings, BtnKind.Ghost, 132, (_, _) => ChangePassword()));
        headRight.Controls.Add(Btn("锁定", Theme.IcoLock, BtnKind.Ghost, 84, (_, _) => Lock()));
        topTable.Controls.Add(headRight, 1, 0);

        // 动作区
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 0)
        };
        actions.Controls.Add(Btn("新增", Theme.IcoAdd, BtnKind.Primary, 88, (_, _) => AddEntry()));
        actions.Controls.Add(Btn("编辑", Theme.IcoEdit, BtnKind.Subtle, 88, (_, _) => EditEntry()));
        actions.Controls.Add(Btn("删除", Theme.IcoDelete, BtnKind.Subtle, 88, (_, _) => DeleteEntry()));
        actions.Controls.Add(Sep());
        actions.Controls.Add(Btn("复制 Key", Theme.IcoCopy, BtnKind.Subtle, 112, (_, _) => CopySelected(e => e.ApiKey, "API Key")));
        actions.Controls.Add(Btn("复制 URL", Theme.IcoLink, BtnKind.Subtle, 112, (_, _) => CopySelected(e => e.BaseUrl, "Base URL")));
        _btnToggleKeys = Btn("", Theme.IcoView, BtnKind.Subtle, 118, (_, _) => ToggleKeys());
        actions.Controls.Add(_btnToggleKeys);
        actions.Controls.Add(Sep());
        actions.Controls.Add(Btn("导入", Theme.IcoImport, BtnKind.Subtle, 84, (_, _) => ImportBackup()));
        actions.Controls.Add(Btn("导出备份", Theme.IcoExport, BtnKind.Subtle, 112, (_, _) => ExportBackup()));
        actions.Controls.Add(Btn("导出 CSV", Theme.IcoFile, BtnKind.Subtle, 112, (_, _) => ExportCsv()));
        actions.Controls.Add(Sep());

        var searchIcon = new Label
        {
            Text = Theme.IcoSearch,
            Font = Theme.IconFont,
            ForeColor = p.TextDim,
            BackColor = p.Surface,
            AutoSize = true,
            Margin = new Padding(6, 10, 2, 0)
        };
        actions.Controls.Add(searchIcon);
        _search = new FieldBox { Width = 210, Height = 38, Margin = new Padding(0, 3, 0, 0) };
        _search.Label = "搜索";
        _search.Placeholder = "搜索名称 / 提供商 / 标签 / 备注";
        _search.Input.TextChanged += (_, _) => RefreshGrid();
        actions.Controls.Add(_search);

        var lockLabel = new Label
        {
            Text = "自动锁定（分钟，0=关）",
            AutoSize = true,
            ForeColor = p.TextDim,
            BackColor = p.Surface,
            Margin = new Padding(12, 11, 4, 0)
        };
        actions.Controls.Add(lockLabel);
        _autoLock = new NumericUpDown
        {
            Minimum = 0, Maximum = 240, Width = 58, Height = 30, Margin = new Padding(0, 6, 0, 0),
            BackColor = p.SurfaceAlt, ForeColor = p.Text, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10.5f),
            AccessibleName = "自动锁定分钟数",
            AccessibleDescription = "闲置多少分钟后自动锁定，0 表示关闭"
        };
        _autoLock.ValueChanged += (_, _) =>
        {
            _settings.AutoLockMinutes = (int)_autoLock.Value;
            SettingsStore.Save(_dataDir, _settings);
        };
        actions.Controls.Add(_autoLock);

        topTable.Controls.Add(actions, 0, 1);
        topTable.SetColumnSpan(actions, 2);
        topCard.Controls.Add(topTable);
        root.Controls.Add(topCard, 0, 0);

        // ===== 列表卡片 =====
        var gridCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(10) };
        _grid = new DataGridView { Dock = DockStyle.Fill };
        Theme.StyleGrid(_grid);
        _grid.AccessibleName = "API Key 列表";
        _grid.AccessibleDescription =
            "回车编辑、Delete 删除、Ctrl+C 复制 Key、双击编辑；列头可点击排序";
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersHeight = 44;
        _grid.RowTemplate.Height = 38;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;

        AddCol("名称", 150, 18);
        AddCol("提供商", 110, 12);

        var keyCol = AddCol("API Key", 200, 22);
        keyCol.DefaultCellStyle.Font = new Font("Consolas", 10.5f);

        AddCol("Base URL", 180, 18);
        AddCol("模型", 100, 10);
        AddCol("标签", 90, 10);
        AddCol("到期日", 110, 0);
        AddCol("更新时间", 130, 0);

        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditEntry(); };
        _grid.KeyDown += GridKeyDown;
        _grid.RowPostPaint += GridRowPostPaint;

        typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(_grid, true);

        gridCard.Controls.Add(_grid);
        root.Controls.Add(gridCard, 0, 1);

        // ===== 状态栏 =====
        var bottom = new Panel { Dock = DockStyle.Fill, Height = 34, BackColor = p.Back, Margin = new Padding(0, 8, 0, 0) };
        _lockPill = new PillLabel
        {
            Dock = DockStyle.Right, Width = 190, Margin = new Padding(8, 4, 0, 4),
            Font = new Font("Segoe UI", 9.5f)
        };
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = p.TextDim,
            BackColor = p.Back,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9.5f),
            AutoEllipsis = true
        };
        bottom.Controls.Add(_statusLabel);
        bottom.Controls.Add(_lockPill);
        root.Controls.Add(bottom, 0, 2);
    }

    private ThemedButton Btn(string text, string glyph, BtnKind kind, int width, EventHandler onClick)
    {
        var b = new ThemedButton
        {
            Text = text, Glyph = glyph, Kind = kind, Width = width, Margin = new Padding(0, 2, 8, 2)
        };
        b.Click += onClick;
        return b;
    }

    private static Control Sep() =>
        new Label
        {
            Text = "│",
            AutoSize = true,
            ForeColor = Theme.P.Border,
            BackColor = Theme.P.Surface,
            Margin = new Padding(2, 10, 8, 4)
        };

    private DataGridViewTextBoxColumn AddCol(string header, int width, float fillWeight)
    {
        var col = new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            Name = header,
            SortMode = DataGridViewColumnSortMode.Automatic,
            ReadOnly = true
        };
        if (fillWeight > 0)
        {
            col.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            col.FillWeight = fillWeight;
            col.MinimumWidth = 70;
        }
        else
        {
            col.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            col.Width = width;
        }
        _grid.Columns.Add(col);
        return col;
    }

    private void GridRowPostPaint(object? sender, DataGridViewRowPostPaintEventArgs e)
    {
        using var pen = new Pen(Theme.P.GridLine);
        e.Graphics.DrawLine(pen, e.RowBounds.Left, e.RowBounds.Bottom - 1,
            e.RowBounds.Right, e.RowBounds.Bottom - 1);
    }

    private void ApplyStateTexts()
    {
        _btnToggleKeys.Text = _showKeys ? "隐藏密钥" : "显示密钥";
        _btnTheme.Text = Theme.Current == AppTheme.Light ? "深色" : "浅色";
        _btnTheme.Glyph = Theme.Current == AppTheme.Light ? Theme.IcoMoon : Theme.IcoSun;
        _autoLock.Value = Math.Clamp(_settings.AutoLockMinutes, (int)_autoLock.Minimum, (int)_autoLock.Maximum);
    }

    private void ToggleTheme()
    {
        Theme.Current = Theme.Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
        _settings.Theme = Theme.Current.ToString();
        SettingsStore.Save(_dataDir, _settings);
        RebuildUi();
    }

    // ---------------- 解锁 / 锁定 ----------------

    private void BeginUnlock()
    {
        if (File.Exists(_vaultPath))
        {
            if (_autoPassword != null)
            {
                try
                {
                    Adopt(VaultStore.Load(_vaultPath, _autoPassword), _autoPassword);
                    return;
                }
                catch (VaultException) { /* 回落到手动输入 */ }
            }

            Text = "API Key 管理器（已锁定）";
            while (true)
            {
                string? pwd = PromptForm.AskPassword(this, "解锁", $"请输入主密码\n库文件：{_vaultPath}", _password);
                if (pwd == null)
                {
                    Close();
                    return;
                }
                try
                {
                    Adopt(VaultStore.Load(_vaultPath, pwd), pwd);
                    return;
                }
                catch (VaultException ex)
                {
                    MessageBox.Show(this, ex.Message, "无法解锁", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // 首次运行：创建主密码与库文件
        string? newPwd = _autoPassword ?? PromptForm.AskNewPassword(this, "创建主密码",
            "首次使用，请设置主密码\n主密码用于加密本机上所有 API Key，丢失后数据无法恢复");
        if (newPwd == null)
        {
            Close();
            return;
        }

        try
        {
            VaultStore.Save(_vaultPath, new VaultData(), newPwd);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法创建库文件：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        Adopt(new VaultData(), newPwd);
        if (_autoPassword == null)
        {
            MessageBox.Show(this,
                $"库文件已创建：\n{_vaultPath}\n\n请牢记主密码；忘记主密码将无法恢复数据。",
                "创建成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void Adopt(VaultData data, string password)
    {
        _repo.Load(data);
        _password = password;
        _unlocked = true;
        _lastActivity = DateTime.UtcNow;
        Text = "API Key 管理器";
        RefreshGrid();
        MaybeUpgradeIterations();

        // 到期汇总延后一拍：等窗口真正显示出来再弹，
        // 否则对话框会先于主窗口出现（看起来像卡住）
        BeginInvoke(new Action(ReportExpiryAlerts));

        if (Program.PreviewDialogs)
        {
            Program.PreviewDialogs = false;
            BeginInvoke(new Action(() =>
            {
                var sample = new ApiEntry
                {
                    Name = "OpenAI", Provider = "OpenAI",
                    ApiKey = "sk-proj-demo0001abcdefghijkl",
                    BaseUrl = "https://api.openai.com/v1",
                    Model = "gpt-4o", Tags = "AI,官方", Notes = "公司账号，月度限额 $500"
                };
                EntryEditForm.Edit(this, sample, true);
            }));
        }
    }

    private void Lock()
    {
        if (!_unlocked) return;
        _password = null;
        _unlocked = false;
        _repo.Clear();
        _clipboard.Tick(force: true);
        RefreshGrid();
        _statusLabel.Text = "已锁定。";
        BeginUnlock();
    }

    private bool SaveVault()
    {
        if (_password == null) return false;
        try
        {
            _repo.Save(_password);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    /// <summary>
    /// 静默升级口令派生强度：库里的迭代次数明显偏低时，用当前推荐值原地重加密。
    ///
    /// 只在这里调用，即"刚用口令成功解密之后"——此时口令已验证，
    /// 重加密不会因为口令错误而写坏库文件。
    ///
    /// 失败不打断用户：库文件仍是旧的（但可正常解密的）那份，
    /// 下次解锁还会再试一次。
    /// </summary>
    private void MaybeUpgradeIterations()
    {
        if (_password == null) return;

        VaultMetadata? meta = _repo.Inspect();
        if (meta is not { } info || !info.NeedsUpgrade) return;

        try
        {
            // 覆盖前先留一份旧库，万一新文件写入异常还能人工找回
            _repo.BackupBeforeOverwrite();
            _repo.Save(_password, IterationPolicy.Current);

            // 写完立刻回读校验：确认新文件能用同一口令解开、内容一致
            var check = VaultStore.Load(_vaultPath, _password);
            if (check.Entries.Count != _repo.Count)
                throw new VaultException("回读校验的记录数与预期不一致。");

            _statusLabel.Text = $"已将库文件加密强度升级到 {IterationPolicy.Current:N0} 次迭代。";
        }
        catch (Exception ex)
        {
            // 降级为静默提示：升级失败不影响当前会话继续使用
            _statusLabel.Text = $"加密强度升级未完成（{ex.Message}），下次解锁会重试。";
        }
    }

    // ---------------- 列表 ----------------

    private ApiEntry? SelectedEntry() => _grid.CurrentRow?.Tag as ApiEntry;

    private void RefreshGrid()
    {
        if (_grid == null) return;
        string q = (_search.Text2 ?? "").Trim();
        _grid.Rows.Clear();

        var view = EntrySearch.Filter(_repo.Entries, q);

        int shown = 0;
        int alerts = 0;
        DateTime now = DateTime.Now;

        foreach (var e in view)
        {
            var exp = ExpiryPolicy.Evaluate(e, now);
            if (exp.NeedsAttention) alerts++;

            int i = _grid.Rows.Add(
                e.Name,
                e.Provider,
                _showKeys ? e.ApiKey : KeyMask.Format(e.ApiKey),
                e.BaseUrl,
                e.Model,
                e.Tags,
                e.ExpiresUtc is { } d ? d.ToString("yyyy-MM-dd") : "—",
                e.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            _grid.Rows[i].Tag = e;

            // 到期状态的行内提示：整行不染色（会与选中态/斑马纹打架），
            // 只把「到期日」单元格标红/标橙，并补上 tooltip
            if (exp.NeedsAttention)
            {
                var cell = _grid.Rows[i].Cells["到期日"];
                cell.Style.ForeColor = exp.State == ExpiryState.Expired ? Theme.P.Danger : Theme.P.Accent;
                cell.Style.Font = Theme.HeaderFont;
                cell.ToolTipText = $"{e.Name}：{exp.Describe()}";
            }
            shown++;
        }

        _statusLabel.Text = _unlocked
            ? $"记录：{shown} / {_repo.Count} 条"
              + (alerts > 0 ? $"　·　⚠ {alerts} 条需关注到期" : "")
              + $"　·　库文件：{_vaultPath}"
            : "已锁定。";
    }

    /// <summary>
    /// 启动解锁后汇总一次到期提醒。
    /// 用非模态的状态栏 + 一次模态汇总框：既不打扰又能确保用户看到。
    /// </summary>
    private void ReportExpiryAlerts()
    {
        var alerts = ExpiryPolicy.FindAlerts(_repo.Entries, DateTime.Now);
        if (alerts.Count == 0) return;

        int expired = alerts.Count(a => a.Info.State == ExpiryState.Expired);
        int soon = alerts.Count - expired;

        var sb = new StringBuilder();
        if (expired > 0) sb.Append($"{expired} 条已过期");
        if (soon > 0)
        {
            if (sb.Length > 0) sb.Append("，");
            sb.Append($"{soon} 条将在 {ExpiryPolicy.SoonWindowDays} 天内到期");
        }

        var lines = alerts.Take(8).Select(a => $"· {a.Entry.Name} —— {a.Info.Describe()}");
        string detail = string.Join("\n", lines);
        if (alerts.Count > 8) detail += $"\n… 另有 {alerts.Count - 8} 条";

        MessageBox.Show(this,
            $"{sb}\n\n{detail}\n\n请在列表中处理（「到期日」列已标色）。",
            "密钥到期提醒", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // ---------------- 记录操作 ----------------

    private void AddEntry()
    {
        if (!_unlocked) return;
        var entry = new ApiEntry();
        if (EntryEditForm.Edit(this, entry, true) != DialogResult.OK) return;
        _repo.Add(entry);
        if (SaveVault())
        {
            RefreshGrid();
            _statusLabel.Text = $"已添加「{entry.Name}」。";
        }
    }

    private void EditEntry()
    {
        if (!_unlocked) return;
        var entry = SelectedEntry();
        if (entry == null)
        {
            _statusLabel.Text = "请先在列表中选择一条记录。";
            return;
        }
        if (EntryEditForm.Edit(this, entry, false) != DialogResult.OK) return;
        entry.UpdatedUtc = DateTime.UtcNow;
        if (SaveVault())
        {
            RefreshGrid();
            _statusLabel.Text = $"已保存「{entry.Name}」。";
        }
    }

    private void DeleteEntry()
    {
        if (!_unlocked) return;
        var entry = SelectedEntry();
        if (entry == null)
        {
            _statusLabel.Text = "请先在列表中选择一条记录。";
            return;
        }
        var ans = MessageBox.Show(this, $"确定删除「{entry.Name}」吗？\n此操作不可撤销。", "删除确认",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (ans != DialogResult.Yes) return;
        _repo.Remove(entry);
        if (SaveVault())
        {
            RefreshGrid();
            _statusLabel.Text = $"已删除「{entry.Name}」。";
        }
    }

    private void CopySelected(Func<ApiEntry, string> pick, string label)
    {
        if (!_unlocked) return;
        var entry = SelectedEntry();
        if (entry == null)
        {
            _statusLabel.Text = "请先在列表中选择一条记录。";
            return;
        }
        string value = pick(entry) ?? "";
        if (value.Length == 0)
        {
            _statusLabel.Text = $"该记录的 {label} 为空。";
            return;
        }
        try { Clipboard.SetText(value); }
        catch (Exception ex) { _statusLabel.Text = "复制失败：" + ex.Message; return; }

        _clipboard.Track(value, _settings.ClipboardClearSeconds);
        _statusLabel.Text = _settings.ClipboardClearSeconds > 0
            ? $"已复制 {label}，{_settings.ClipboardClearSeconds} 秒后自动清除剪贴板。"
            : $"已复制 {label}。";
    }

    private void ToggleKeys()
    {
        _showKeys = !_showKeys;
        _settings.ShowKeys = _showKeys;
        SettingsStore.Save(_dataDir, _settings);
        _btnToggleKeys.Text = _showKeys ? "隐藏密钥" : "显示密钥";
        RefreshGrid();
    }

    // ---------------- 导入 / 导出 ----------------

    private void ImportBackup()
    {
        if (!_unlocked) return;
        using var dlg = new OpenFileDialog
        {
            Title = "选择备份文件",
            Filter = "库备份 (*.akv;*.akvbak)|*.akv;*.akvbak|所有文件 (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string? pwd = PromptForm.AskPassword(this, "导入备份", "请输入该备份文件的密码", _password);
        if (pwd == null) return;

        VaultData data;
        try { data = VaultStore.Load(dlg.FileName, pwd); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导入失败：" + ex.Message, "导入备份", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var choice = MessageBox.Show(this,
            $"备份中包含 {data.Entries.Count} 条记录。\n\n是　= 替换当前库（先自动备份当前库再清空）\n否　= 合并到当前库（跳过重复 ID）\n取消 = 放弃导入",
            "导入方式", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (choice == DialogResult.Cancel) return;

        string? safety = null;
        if (choice == DialogResult.Yes)
        {
            // 替换是破坏性操作：先留一份当前库的副本，导入出错还能找回
            safety = _repo.BackupBeforeOverwrite();
            _repo.ReplaceAll(data.Entries);
        }
        else
        {
            _repo.MergeById(data.Entries);
        }

        if (SaveVault())
        {
            RefreshGrid();
            string note = choice == DialogResult.Yes && safety != null ? $"（原库已备份到 {safety}）" : "";
            _statusLabel.Text = $"已导入 {data.Entries.Count} 条记录。{note}";
        }
    }

    private void ExportBackup()
    {
        if (!_unlocked) return;
        using var dlg = new SaveFileDialog
        {
            Title = "导出加密备份",
            Filter = "库备份 (*.akvbak)|*.akvbak",
            DefaultExt = "akvbak",
            FileName = $"api-keys-backup-{DateTime.Now:yyyyMMdd}.akvbak"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string? pwd = PromptForm.AskPassword(this, "导出备份", "请设置备份密码（已预填当前主密码，可修改）", _password);
        if (pwd == null) return;

        try
        {
            // 备份里带上界面偏好，跨机迁移时主题 / 自动锁定时长等设置不会丢
            var bundle = _repo.Snapshot();
            bundle.Settings = _settings;
            VaultStore.Save(dlg.FileName, bundle, pwd);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "导出备份", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        MessageBox.Show(this,
            $"已导出加密备份：\n{dlg.FileName}\n\n导入时需要备份密码，请与备份文件分开妥善保管。",
            "导出备份", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExportCsv()
    {
        if (!_unlocked) return;
        var confirm = MessageBox.Show(this,
            "警告：CSV 是明文文件，任何人拿到即可看到全部 API Key。\n请仅在可信环境中使用，用完立即删除。\n\n确定继续导出吗？",
            "导出明文 CSV", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        using var dlg = new SaveFileDialog
        {
            Title = "导出 CSV（明文）",
            Filter = "CSV 文件 (*.csv)|*.csv",
            DefaultExt = "csv",
            FileName = $"api-keys-{DateTime.Now:yyyyMMdd}.csv"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        File.WriteAllText(dlg.FileName, CsvExporter.Build(_repo.Entries), new UTF8Encoding(true));

        _statusLabel.Text = $"已导出明文 CSV：{dlg.FileName}（请尽快删除）";
        MessageBox.Show(this,
            $"已导出明文 CSV：\n{dlg.FileName}\n\n该文件不加密，请尽快迁移或删除。",
            "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void ChangePassword()
    {
        if (!_unlocked || _password == null) return;
        string? newPwd = PasswordChangeForm.Run(this, _password);
        if (newPwd == null) return;
        _password = newPwd;
        if (SaveVault())
            MessageBox.Show(this, "主密码已修改，请牢记新密码。", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ---------------- 快捷键 / 定时 ----------------

    private void GridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            EditEntry();
        }
        else if (e.KeyCode == Keys.Delete)
        {
            DeleteEntry();
        }
        else if (e.Control && e.KeyCode == Keys.C)
        {
            e.SuppressKeyPress = true;
            CopySelected(x => x.ApiKey, "API Key");
        }
    }

    private void OnTimer(object? sender, EventArgs e)
    {
        _clipboard.Tick(force: false);

        if (!_unlocked) return;
        if (OwnedForms.Any(f => f.Visible)) return; // 有模态对话框时不自动锁定

        int minutes = _settings.AutoLockMinutes;
        if (minutes <= 0)
        {
            _lockPill.Text = "自动锁定：已关闭";
            return;
        }

        double left = minutes - (DateTime.UtcNow - _lastActivity).TotalMinutes;
        _lockPill.Text = "自动锁定 " + TimeSpan.FromMinutes(Math.Max(0, left)).ToString(@"mm\:ss");
        if (left <= 0) Lock();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _clipboard.Tick(force: true);
        base.OnFormClosing(e);
    }

    // 记录全局输入活动，用于自动锁定
    private sealed class ActivityFilter : IMessageFilter
    {
        private readonly Action _ping;
        public ActivityFilter(Action ping) => _ping = ping;

        public bool PreFilterMessage(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x0100: // WM_KEYDOWN
                case 0x0101: // WM_KEYUP
                case 0x0200: // WM_MOUSEMOVE
                case 0x0201: // WM_LBUTTONDOWN
                case 0x0204: // WM_RBUTTONDOWN
                case 0x0207: // WM_MBUTTONDOWN
                case 0x020A: // WM_MOUSEWHEEL
                    _ping();
                    break;
            }
            return false;
        }
    }
}
