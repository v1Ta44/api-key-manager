using System.Globalization;
using ApiKeyManager.Application;

namespace ApiKeyManager.Wpf.ViewModels;

public sealed record WorkspaceEntry(EntrySummary Value)
{
    public string Id => Value.Id;
    public string Name => Value.Name;
    public string Provider => Value.Provider;
    public string Monogram => string.IsNullOrEmpty(Provider) ? "K" : Provider[..1].ToUpperInvariant();
    public string Subtitle => string.Join(" · ", new[] { Provider, Value.Model }.Where(s => s.Length > 0));
    public string KeyHint => Value.HasKey ? Value.KeyHint : "未填写密钥";
    public string Url => Value.BaseUrl;
    public string Model => Value.Model;
    public string Tags => Value.Tags;
    public string Notes => Value.Notes;
    public string CopyLabel => "复制 " + Name + " 的密钥";
    public string ExpiryLabel => ExpiryPolicy.Evaluate(Value.Expires, DateTime.Today).Describe();
    public string ExpiryKind => ExpiryPolicy.Evaluate(Value.Expires, DateTime.Today).State switch
    { ExpiryState.Expired => "Expired", ExpiryState.ExpiringSoon => "Soon", _ => "All" };
    public string Dates => $"到期日：{Value.Expires?.ToString("yyyy-MM-dd") ?? "未设置"}\n创建：{Value.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}\n更新：{Value.UpdatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
}

/// <summary>页面状态及用例编排。密码只作为方法参数，列表只接收不含密钥的摘要。</summary>
public sealed class WorkspaceViewModel
{
    private readonly VaultSession _session;
    private readonly Func<AppSettings, bool> _saveSettings;
    private readonly Func<string, int, bool> _copy;
    private List<WorkspaceEntry> _all = [];
    private long _draftGeneration;
    private DateTime _revealUntil;
    public event Action? Changed;
    public event Action? SessionLocked;
    public event Action? SessionUnlocked;
    public WorkspaceViewModel(VaultSession session, AppSettings settings, Func<AppSettings, bool> saveSettings, Func<string, int, bool> copy)
    { _session = session; Settings = Normalize(settings); _saveSettings = saveSettings; _copy = copy; }
    public AppSettings Settings { get; private set; }
    public bool IsLocked => !_session.IsUnlocked;
    public bool IsNew => !_session.Exists;
    public bool Busy { get; private set; }
    public string Feedback { get; private set; } = "";
    public string Error { get; private set; } = "";
    public string Query { get; private set; } = "";
    public string Filter { get; private set; } = "All";
    public string Platform { get; private set; } = "全部平台";
    public string Tag { get; private set; } = "全部标签";
    public IReadOnlyList<string> Platforms => new[] { "全部平台" }.Concat(_all.Select(e => e.Provider).Where(s => s.Length > 0).Distinct().Order()).ToArray();
    public IReadOnlyList<string> Tags => new[] { "全部标签" }.Concat(_all.SelectMany(e => SplitTags(e.Tags)).Distinct().Order()).ToArray();
    private static string[] SplitTags(string text) => text.Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public IReadOnlyList<WorkspaceEntry> Entries { get; private set; } = [];
    public WorkspaceEntry? Selected { get; private set; }
    public ApiEntry? Draft { get; private set; }
    public bool NewDraft { get; private set; }
    public string Pane { get; private set; } = "";
    public string? RevealedSecret { get; private set; }
    public ImportPreview? ImportPreview { get; private set; }
    public bool HasRecords => _all.Count > 0;
    public string AllLabel => $"全部  {_all.Count}";
    public string SoonLabel => $"即将到期  {_all.Count(e => e.ExpiryKind == "Soon")}";
    public string ExpiredLabel => $"已过期  {_all.Count(e => e.ExpiryKind == "Expired")}";
    public void Notify(string message) { Feedback = message; Changed?.Invoke(); }
    public void SetError(string message) { Error = message; Changed?.Invoke(); }
    private void Signal() => Changed?.Invoke();
    private void Refresh(string? id = null)
    {
        id ??= Selected?.Id;
        _all = _session.GetEntries().Select(e => new WorkspaceEntry(e)).ToList();
        if (!Platforms.Contains(Platform)) Platform = "全部平台";
        if (!Tags.Contains(Tag)) Tag = "全部标签";
        ApplyFilter(id);
    }
    private void ApplyFilter(string? id)
    {
        Entries = _all.Where(e => (Filter == "All" || e.ExpiryKind == Filter) &&
            (Platform == "全部平台" || e.Provider == Platform) && (Tag == "全部标签" || SplitTags(e.Tags).Contains(Tag)) &&
            new[] { e.Name, e.Provider, e.Model, e.Tags, e.Notes, e.Url }.Any(s => s.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        Selected = Entries.FirstOrDefault(e => e.Id == id) ?? Entries.FirstOrDefault();
        RevealedSecret = null;
    }
    public void Search(string query, string filter) { Query = query; Filter = filter; ApplyFilter(Selected?.Id); Signal(); }
    public void FilterBy(string platform, string tag) { Platform = platform; Tag = tag; ApplyFilter(Selected?.Id); Signal(); }
    public void ResetFilters() { Platform = "全部平台"; Tag = "全部标签"; Search("", "All"); }
    public void Select(WorkspaceEntry? entry) { Selected = entry; RevealedSecret = null; Signal(); }
    public async Task Unlock(string password, string confirmation)
    {
        if (Busy) return;
        if (IsNew && password != confirmation) { SetError("两次输入的主密码不一致。"); return; }
        await Run(async () =>
        {
            var result = await _session.UnlockAsync(password, IsNew);
            if (result.Succeeded) { Refresh(); SessionUnlocked?.Invoke(); }
            return result;
        }, "空间已解锁。");
    }
    public void Lock()
    {
        _session.Lock(); Draft = null; Pane = ""; ImportPreview = null; RevealedSecret = null;
        _all.Clear(); Entries = []; Selected = null; Query = ""; Filter = "All"; Platform = "全部平台"; Tag = "全部标签"; Error = "";
        Feedback = "空间已锁定，未保存的草稿已清除。"; SessionLocked?.Invoke(); Signal();
    }
    public void BeginEdit(bool add)
    {
        if (Busy || IsLocked || Pane.Length > 0) return;
        Draft = add ? new ApiEntry() : Selected == null ? null : _session.GetDraft(Selected.Id);
        if (Draft == null) return;
        NewDraft = add; _draftGeneration = _session.Generation; Pane = "Edit"; Error = ""; RevealedSecret = null; Signal();
    }
    public async Task SaveDraft(ApiEntry input, string expiry)
    {
        if (Busy || IsLocked || Draft == null) return;
        DateTime? date = null;
        if (!string.IsNullOrWhiteSpace(expiry))
        {
            if (!DateTime.TryParseExact(expiry.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            { SetError("到期日请填写 yyyy-MM-dd，例如 2026-12-31，或留空。"); return; }
            date = parsed.Date;
        }
        input.Id = Draft.Id; input.CreatedUtc = Draft.CreatedUtc; input.ExpiresUtc = date;
        await Run(async () =>
        {
            var r = await _session.SaveEntryAsync(input, _draftGeneration);
            if (r.Succeeded)
            {
                Draft = null; Pane = ""; Refresh(input.Id);
                if (Selected?.Id != input.Id) { Query = ""; Filter = "All"; Platform = "全部平台"; Tag = "全部标签"; ApplyFilter(input.Id); }
            }
            return r;
        }, "记录已保存。");
    }
    public void OpenPane(string pane)
    {
        if (Busy || IsLocked || Pane.Length > 0) return;
        Pane = pane; Error = ""; RevealedSecret = null; Signal();
    }
    public void ClosePane()
    {
        if (Busy) return;
        Draft = null; Pane = ""; ImportPreview = null; Error = ""; _session.CancelImport(); Signal();
    }
    public async Task Delete()
    {
        if (Busy || IsLocked || Selected == null || Pane != "Delete") return;
        var id = Selected.Id; var generation = _session.Generation;
        await Run(async () => { var r = await _session.DeleteAsync(id, generation); if (r.Succeeded) { Pane = ""; Refresh(); } return r; }, "记录已删除。");
    }
    public bool Copy(WorkspaceEntry? entry, bool url = false)
    {
        if (IsLocked || entry == null) return false;
        var value = url ? entry.Url : _session.GetSecret(entry.Id);
        var success = !string.IsNullOrEmpty(value) && _copy(value, Settings.ClipboardClearSeconds);
        Notify(string.IsNullOrEmpty(value) ? "这条记录没有可复制的内容。" :
            success ? Settings.ClipboardClearSeconds > 0 ? $"已复制，{Settings.ClipboardClearSeconds} 秒后清理。" : "已复制；自动清理已关闭。" : "剪贴板暂时不可用，请重试。");
        return success;
    }
    public void Reveal()
    {
        if (IsLocked || Selected == null) return;
        RevealedSecret = RevealedSecret == null ? _session.GetSecret(Selected.Id) : null;
        _revealUntil = DateTime.UtcNow.AddSeconds(10); Signal();
    }
    public void Tick()
    {
        if (RevealedSecret != null && DateTime.UtcNow >= _revealUntil) { RevealedSecret = null; Signal(); }
    }
    public void HideSecret() { RevealedSecret = null; Signal(); }
    public void ToggleTheme()
    {
        var next = Normalize(Settings); next.Theme = next.Theme == "Dark" ? "Light" : "Dark";
        ApplySettings(next);
    }
    public void SavePreferences(string idle, string clipboard)
    {
        if (!int.TryParse(idle, out var minutes) || minutes < 0 || minutes > 1440 ||
            !int.TryParse(clipboard, out var seconds) || seconds < 0 || seconds > 3600)
        { SetError("自动锁定填写 0–1440 分钟，剪贴板清理填写 0–3600 秒。0 表示关闭。"); return; }
        var next = Normalize(Settings); next.AutoLockMinutes = minutes; next.ClipboardClearSeconds = seconds;
        if (ApplySettings(next)) { Pane = ""; Signal(); }
    }
    private bool ApplySettings(AppSettings settings)
    {
        if (!_saveSettings(settings)) { SetError("设置保存失败，请检查目录权限；原设置保留。"); return false; }
        Settings = settings; Error = ""; Notify("设置已保存。"); return true;
    }
    public async Task ChangePassword(string current, string next, string confirm)
    {
        if (Busy || IsLocked) return;
        if (next != confirm) { SetError("两次输入的新主密码不一致。"); return; }
        var generation = _session.Generation;
        await Run(async () => { var r = await _session.ChangePasswordAsync(current, next, generation); if (r.Succeeded) Pane = ""; return r; }, "主密码已修改，并已验证新库可解锁。");
    }
    public async Task PrepareImport(string path, string password)
    {
        if (Busy || IsLocked) return;
        await Run(async () =>
        {
            var (r, preview) = await _session.PrepareImportAsync(path, password);
            if (r.Succeeded) { ImportPreview = preview; Pane = "Preview"; }
            return r;
        }, "备份已读取，请核对导入方式。");
    }
    public async Task CommitImport(bool replace, bool preferences)
    {
        if (Busy || IsLocked) return;
        await Run(async () =>
        {
            var (r, settings) = await _session.CommitImportAsync(replace, preferences);
            if (r.Succeeded)
            {
                Pane = ""; ImportPreview = null; Refresh();
                if (settings != null && !ApplySettings(Normalize(settings)))
                    return OperationResult.Ok("记录已导入；偏好保存失败，原设置保留。");
            }
            return r;
        }, "导入完成。");
    }
    public async Task Export(string path, string password, string confirm, bool csv)
    {
        if (Busy || IsLocked) return;
        if (!csv && password != confirm) { SetError("两次输入的备份密码不一致。"); return; }
        await Run(async () => { var r = await _session.ExportAsync(path, password, Settings, csv); if (r.Succeeded) Pane = ""; return r; }, csv ? "CSV 已导出，包含明文密钥。" : "加密备份已导出。");
    }
    private async Task Run(Func<Task<OperationResult>> action, string success)
    {
        Busy = true; Error = ""; Signal();
        try
        {
            var result = await action();
            if (result.Succeeded) { Error = ""; Feedback = result.Message.Length > 0 ? result.Message : success; }
            else if (result.Code == ResultCode.SessionChanged) Feedback = result.Message;
            else Error = result.Message;
        }
        catch { Error = "操作未完成，请检查文件和目录后重试。"; }
        finally { Busy = false; Signal(); }
    }
    private static AppSettings Normalize(AppSettings s) => new()
    { Theme = s.Theme == "Dark" ? "Dark" : "Light", AutoLockMinutes = s.AutoLockMinutes is >= 0 and <= 1440 ? s.AutoLockMinutes : 5, ClipboardClearSeconds = s.ClipboardClearSeconds is >= 0 and <= 3600 ? s.ClipboardClearSeconds : 30, ShowKeys = false };
}
