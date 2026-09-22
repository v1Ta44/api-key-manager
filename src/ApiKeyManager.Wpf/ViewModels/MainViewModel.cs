using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using ApiKeyManager;
using ApiKeyManager.Wpf.Services;
using ApiKeyManager.Wpf.Themes;

namespace ApiKeyManager.Wpf.ViewModels;

/// <summary>
/// 主界面 ViewModel —— 取代 WinForms 版 MainForm 的 834 行 code-behind。
///
/// 职责划分：
///   本类只做"状态 + 命令 + 编排"，不做任何像素/控件操作。
///   文件对话框、消息框等平台交互通过注入的 <see cref="IDialogService"/> 完成，
///   因此本类可以被单测（不需要 UI 线程）。
/// </summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IDialogService _dialogs;
    private readonly ClipboardService _clipboard;
    private readonly AutoLockWatcher _autoLock;

    private EntryRepository? _repo;
    private AppSettings _settings = new();
    private string _dataDir = "";
    private string _vaultPath = "";
    private string? _password;
    private bool _unlocked;
    private bool _showKeys;
    private string _searchText = "";
    private string _statusText = "就绪。";
    private int _autoLockMinutes;
    private EntryViewModel? _selected;

    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        _clipboard = new ClipboardService();

        (_dataDir, _vaultPath) = DataPaths.Resolve();
        _settings = SettingsStore.Load(_dataDir);
        _showKeys = _settings.ShowKeys;
        _autoLockMinutes = _settings.AutoLockMinutes;

        _autoLock = new AutoLockWatcher(
            getIdleLimitMinutes: () => _autoLockMinutes,
            onLock: Lock,
            onTick: UpdateAutoLockPill);

        Entries = new ObservableCollection<EntryViewModel>();
        Filtered = new ObservableCollection<EntryViewModel>();

        // ---- 命令 ----
        AddCommand = new RelayCommand(AddEntry, () => IsUnlocked);
        EditCommand = new RelayCommand(EditEntry, () => IsUnlocked && Selected != null);
        DeleteCommand = new RelayCommand(DeleteEntry, () => IsUnlocked && Selected != null);
        CopyKeyCommand = new RelayCommand(() => CopySelected(e => e.ApiKey, "API Key"), () => IsUnlocked && Selected != null);
        CopyUrlCommand = new RelayCommand(() => CopySelected(e => e.BaseUrl, "Base URL"), () => IsUnlocked && Selected != null);
        ToggleKeysCommand = new RelayCommand(ToggleKeys, () => IsUnlocked);
        ImportCommand = new RelayCommand(ImportBackup, () => IsUnlocked);
        ExportBackupCommand = new RelayCommand(ExportBackup, () => IsUnlocked);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => IsUnlocked);
        ChangePasswordCommand = new RelayCommand(ChangePassword, () => IsUnlocked);
        LockCommand = new RelayCommand(Lock, () => IsUnlocked);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        UnlockCommand = new RelayCommand(Unlock);
    }

    // ==================== 集合 ====================

    /// <summary>全部记录（未过滤）。</summary>
    public ObservableCollection<EntryViewModel> Entries { get; }

    /// <summary>经搜索过滤后的视图（直接绑到 ListBox）。</summary>
    public ObservableCollection<EntryViewModel> Filtered { get; }

    // ==================== 状态属性 ====================

    public bool IsUnlocked
    {
        get => _unlocked;
        private set
        {
            if (SetField(ref _unlocked, value))
            {
                OnPropertyChanged(nameof(IsLocked));
                OnPropertyChanged(nameof(VaultInfo));
            }
        }
    }

    public bool IsLocked => !_unlocked;

    public EntryViewModel? Selected
    {
        get => _selected;
        set => SetField(ref _selected, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value)) ApplyFilter();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public int AutoLockMinutes
    {
        get => _autoLockMinutes;
        set
        {
            if (!SetField(ref _autoLockMinutes, value)) return;

            _settings.AutoLockMinutes = value;
            SettingsStore.Save(_dataDir, _settings);
        }
    }

    public bool ShowKeys
    {
        get => _showKeys;
        private set
        {
            if (SetField(ref _showKeys, value)) OnPropertyChanged(nameof(ToggleKeysLabel));
        }
    }

    public string ToggleKeysLabel => _showKeys ? "隐藏密钥" : "显示密钥";

    public string ThemeLabel => ThemeManager.IsDark ? "浅色" : "深色";

    public string AutoLockPillText { get; private set; } = "自动锁定：已关闭";

    public string VaultInfo => _unlocked ? _vaultPath : "";

    public bool HasNoEntries => Filtered.Count == 0;

    public string EmptyHint => Entries.Count == 0
        ? "还没有任何记录，点「新增」开始。"
        : "没有匹配的记录，试试别的关键词。";

    /// <summary>状态栏摘要：记录数 + 到期警示。</summary>
    public string SummaryText
    {
        get
        {
            if (!_unlocked) return "已锁定。";

            var alerts = ExpiryPolicy.FindAlerts(Entries.Select(e => e.Model), DateTime.Now);
            string s = $"记录：{Filtered.Count} / {Entries.Count} 条";
            if (alerts.Count > 0)
            {
                int expired = alerts.Count(a => a.Info.State == ExpiryState.Expired);
                int soon = alerts.Count - expired;
                var bits = new List<string>();
                if (expired > 0) bits.Add($"{expired} 条已过期");
                if (soon > 0) bits.Add($"{soon} 条即将到期");
                s += "　·　⚠ " + string.Join("，", bits);
            }
            return s;
        }
    }

    // ==================== 命令 ====================

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand CopyKeyCommand { get; }
    public ICommand CopyUrlCommand { get; }
    public ICommand ToggleKeysCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ExportBackupCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ChangePasswordCommand { get; }
    public ICommand LockCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand UnlockCommand { get; }

    // ==================== 启动 / 解锁 ====================

    /// <summary>
    /// 启动流程。与 WinForms 版 BeginUnlock 等价：
    /// 库存在 → 要口令解锁；库不存在 → 创建主密码。
    /// </summary>
    public void Start(string? autoPassword = null)
    {
        _autoLock.Start();

        if (File.Exists(_vaultPath))
        {
            if (autoPassword != null)
            {
                try
                {
                    Adopt(VaultStore.Load(_vaultPath, autoPassword), autoPassword);
                    return;
                }
                catch (VaultException) { /* 回落到手动输入 */ }
            }

            // 主窗口先显示出来，解锁对话框由 View 在 Loaded 后弹出
            StatusText = "已锁定，请输入主密码。";
            return;
        }

        // 首次运行：创建主密码
        string? newPwd = autoPassword ?? _dialogs.AskNewPassword(
            "创建主密码",
            "首次使用，请设置主密码\n主密码用于加密本机上所有 API Key，丢失后数据无法恢复");

        if (newPwd == null)
        {
            StatusText = "未创建主密码，程序将退出。";
            _dialogs.RequestShutdown();
            return;
        }

        try
        {
            VaultStore.Save(_vaultPath, new VaultData(), newPwd);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("无法创建库文件：" + ex.Message, "错误");
            _dialogs.RequestShutdown();
            return;
        }

        Adopt(new VaultData(), newPwd);

        if (autoPassword == null)
        {
            _dialogs.ShowInfo(
                $"库文件已创建：\n{_vaultPath}\n\n请牢记主密码；忘记主密码将无法恢复数据。",
                "创建成功");
        }
    }

    /// <summary>供 View 在窗口显示后调用，弹出解锁对话框。</summary>
    public void Unlock()
    {
        if (File.Exists(_vaultPath))
        {
            while (true)
            {
                string? pwd = _dialogs.AskPassword(
                    "解锁", $"请输入主密码\n库文件：{_vaultPath}", _password);

                if (pwd == null)
                {
                    _dialogs.RequestShutdown();
                    return;
                }

                try
                {
                    Adopt(VaultStore.Load(_vaultPath, pwd), pwd);
                    return;
                }
                catch (VaultException ex)
                {
                    _dialogs.ShowWarning(ex.Message, "无法解锁");
                    _password = null;
                }
            }
        }

        Start();
    }

    private void Adopt(VaultData data, string password)
    {
        _repo = new EntryRepository(_vaultPath);
        _repo.Load(data);
        _password = password;
        IsUnlocked = true;

        RebuildEntries();
        _autoLock.Resume();

        StatusText = "已解锁。";
        MaybeUpgradeIterations();
        ReportExpiryAlerts();
    }

    private void Lock()
    {
        if (!_unlocked) return;

        _password = null;
        _repo = null;
        IsUnlocked = false;

        _clipboard.Tick(force: true);
        Entries.Clear();
        Filtered.Clear();
        Selected = null;

        StatusText = "已锁定。";
        AutoLockPillText = "";
        OnPropertyChanged(nameof(AutoLockPillText));
        OnPropertyChanged(nameof(SummaryText));

        // 立刻重新询问口令
        _dialogs.Dispatch(Unlock);
    }

    // ==================== 列表 ====================

    private void RebuildEntries()
    {
        Entries.Clear();
        if (_repo != null)
        {
            foreach (var e in _repo.Entries.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Entries.Add(new EntryViewModel(e) { });
            }
        }

        foreach (var vm in Entries) vm.SetShowKey(_showKeys);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var matched = EntrySearch.Filter(
            Entries.Select(vm => vm.Model), _searchText);

        var wanted = new HashSet<string>(matched.Select(m => m.Id), StringComparer.Ordinal);

        Filtered.Clear();
        foreach (var vm in Entries)
        {
            if (wanted.Contains(vm.Id)) Filtered.Add(vm);
        }

        // 选中项若已被过滤掉则清空
        if (Selected != null && !Filtered.Contains(Selected)) Selected = null;

        RaiseAll(nameof(HasNoEntries), nameof(EmptyHint), nameof(SummaryText));
    }

    // ==================== CRUD ====================

    private void AddEntry()
    {
        if (_repo == null) return;

        var entry = new ApiEntry();
        if (!_dialogs.EditEntry(entry, isNew: true)) return;

        _repo.Add(entry);
        if (!SaveVault()) return;

        RebuildEntries();
        SelectById(entry.Id);
        StatusText = $"已添加「{entry.Name}」。";
    }

    private void EditEntry()
    {
        if (_repo == null || Selected == null) return;

        var entry = Selected.Model;
        if (!_dialogs.EditEntry(entry, isNew: false)) return;

        entry.UpdatedUtc = DateTime.UtcNow;
        if (!SaveVault()) return;

        // 名称可能变了，需要重排；直接用模型数据就地刷新该行
        Selected.RefreshAll();
        if (NameChanged(Selected)) RebuildEntries();

        RaiseAll(nameof(SummaryText));
        StatusText = $"已保存「{entry.Name}」。";
    }

    private static bool NameChanged(EntryViewModel vm) => true; // 保守：名称变化无法廉价判定，重建即可

    private void DeleteEntry()
    {
        if (_repo == null || Selected == null) return;

        string name = Selected.Name;
        if (!_dialogs.Confirm(
                $"确定删除「{name}」吗？\n此操作不可撤销。",
                "删除确认")) return;

        _repo.Remove(Selected.Model);
        if (!SaveVault()) return;

        RebuildEntries();
        StatusText = $"已删除「{name}」。";
    }

    private void SelectById(string id)
    {
        var target = Filtered.FirstOrDefault(vm => vm.Id == id);
        if (target != null) Selected = target;
    }

    private bool SaveVault()
    {
        if (_repo == null || _password == null) return false;
        try
        {
            _repo.Save(_password);
            return true;
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("保存失败：" + ex.Message, "错误");
            return false;
        }
    }

    // ==================== 复制 ====================

    private void CopySelected(Func<ApiEntry, string> pick, string label)
    {
        if (Selected == null) return;

        string value = pick(Selected.Model) ?? "";
        if (value.Length == 0)
        {
            StatusText = $"该记录的 {label} 为空。";
            return;
        }

        if (!_clipboard.SetText(value, _settings.ClipboardClearSeconds))
        {
            StatusText = "复制失败：剪贴板被其它程序占用，请稍后重试。";
            return;
        }

        StatusText = _settings.ClipboardClearSeconds > 0
            ? $"已复制 {label}，{_settings.ClipboardClearSeconds} 秒后自动清除剪贴板。"
            : $"已复制 {label}。";
    }

    private void ToggleKeys()
    {
        ShowKeys = !ShowKeys;
        _settings.ShowKeys = ShowKeys;
        SettingsStore.Save(_dataDir, _settings);

        foreach (var vm in Entries) vm.SetShowKey(ShowKeys);
    }

    // ==================== 导入 / 导出 ====================

    private void ImportBackup()
    {
        if (_repo == null) return;

        string? file = _dialogs.PickOpenFile(
            "选择备份文件",
            "库备份 (*.akv;*.akvbak)|*.akv;*.akvbak|所有文件 (*.*)|*.*");
        if (file == null) return;

        string? pwd = _dialogs.AskPassword("导入备份", "请输入该备份文件的密码", _password);
        if (pwd == null) return;

        VaultData data;
        try
        {
            data = VaultStore.Load(file, pwd);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("导入失败：" + ex.Message, "导入备份");
            return;
        }

        var choice = _dialogs.AskImportMode(data.Entries.Count, _repo.Count);
        if (choice == ImportChoice.Cancel) return;

        string? safety = null;
        if (choice == ImportChoice.Replace)
        {
            safety = _repo.BackupBeforeOverwrite();
            _repo.ReplaceAll(data.Entries);
        }
        else
        {
            _repo.MergeById(data.Entries);
        }

        if (!SaveVault()) return;

        RebuildEntries();
        string note = choice == ImportChoice.Replace && safety != null
            ? $"（原库已备份到 {Path.GetFileName(safety)}）"
            : "";
        StatusText = $"已导入 {data.Entries.Count} 条记录。{note}";
    }

    private void ExportBackup()
    {
        if (_repo == null) return;

        string? file = _dialogs.PickSaveFile(
            "导出加密备份",
            "库备份 (*.akvbak)|*.akvbak",
            "akvbak",
            $"api-keys-backup-{DateTime.Now:yyyyMMdd}.akvbak");
        if (file == null) return;

        string? pwd = _dialogs.AskPassword("导出备份", "请设置备份密码（已预填当前主密码，可修改）", _password);
        if (pwd == null) return;

        try
        {
            var bundle = _repo.Snapshot();
            bundle.Settings = _settings;
            VaultStore.Save(file, bundle, pwd);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("导出失败：" + ex.Message, "导出备份");
            return;
        }

        _dialogs.ShowInfo(
            $"已导出加密备份：\n{file}\n\n导入时需要备份密码，请与备份文件分开妥善保管。",
            "导出备份");
        StatusText = $"已导出加密备份：{Path.GetFileName(file)}";
    }

    private void ExportCsv()
    {
        if (_repo == null) return;

        if (!_dialogs.Confirm(
                "警告：CSV 是明文文件，任何人拿到即可看到全部 API Key。\n请仅在可信环境中使用，用完立即删除。\n\n确定继续导出吗？",
                "导出明文 CSV")) return;

        string? file = _dialogs.PickSaveFile(
            "导出 CSV（明文）",
            "CSV 文件 (*.csv)|*.csv",
            "csv",
            $"api-keys-{DateTime.Now:yyyyMMdd}.csv");
        if (file == null) return;

        try
        {
            File.WriteAllText(file, CsvExporter.Build(_repo.Entries), new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("导出失败：" + ex.Message, "导出 CSV");
            return;
        }

        _dialogs.ShowWarning(
            $"已导出明文 CSV：\n{file}\n\n该文件不加密，请尽快迁移或删除。",
            "导出完成");
        StatusText = $"已导出明文 CSV：{Path.GetFileName(file)}（请尽快删除）";
    }

    // ==================== 主密码 / 主题 ====================

    private void ChangePassword()
    {
        if (_repo == null || _password == null) return;

        string? newPwd = _dialogs.ChangePassword(_password);
        if (newPwd == null) return;

        _password = newPwd;
        if (SaveVault())
        {
            _dialogs.ShowInfo("主密码已修改，请牢记新密码。", "成功");
            StatusText = "主密码已修改。";
        }
    }

    private void ToggleTheme()
    {
        ThemeManager.Toggle();
        _settings.Theme = ThemeManager.CurrentName;
        SettingsStore.Save(_dataDir, _settings);

        OnPropertyChanged(nameof(ThemeLabel));
    }

    // ==================== 迭代数升级 ====================

    /// <summary>
    /// 静默升级口令派生强度。只在"刚用口令成功解密之后"调用，
    /// 因此不会因口令错误而写坏库文件。
    /// </summary>
    private void MaybeUpgradeIterations()
    {
        if (_repo == null || _password == null) return;

        var meta = _repo.Inspect();
        if (meta is not { } info || !info.NeedsUpgrade) return;

        try
        {
            _repo.BackupBeforeOverwrite();
            _repo.Save(_password, IterationPolicy.Current);

            var check = VaultStore.Load(_vaultPath, _password);
            if (check.Entries.Count != _repo.Count)
                throw new VaultException("回读校验的记录数与预期不一致。");

            StatusText = $"已将库文件加密强度升级到 {IterationPolicy.Current:N0} 次迭代。";
        }
        catch (Exception ex)
        {
            StatusText = $"加密强度升级未完成（{ex.Message}），下次解锁会重试。";
        }
    }

    // ==================== 到期提醒 ====================

    private void ReportExpiryAlerts()
    {
        var alerts = ExpiryPolicy.FindAlerts(Entries.Select(e => e.Model), DateTime.Now);
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

        _dialogs.ShowWarning(
            $"{sb}\n\n{detail}\n\n请在列表中处理（「到期日」列已标色）。",
            "密钥到期提醒");
    }

    // ==================== 自动锁定 ====================

    private void UpdateAutoLockPill(TimeSpan left)
    {
        if (_autoLockMinutes <= 0)
        {
            AutoLockPillText = "自动锁定：已关闭";
        }
        else
        {
            AutoLockPillText = "自动锁定 " + left.ToString(@"mm\:ss");
        }
        OnPropertyChanged(nameof(AutoLockPillText));
    }

    /// <summary>供 View 每秒调用，驱动剪贴板自动清除。</summary>
    public void TickClipboard() => _clipboard.Tick(force: false);

    /// <summary>窗口关闭时清理。</summary>
    public void Shutdown()
    {
        _clipboard.Tick(force: true);
        _autoLock.Dispose();
    }

    public void Dispose() => Shutdown();
}
