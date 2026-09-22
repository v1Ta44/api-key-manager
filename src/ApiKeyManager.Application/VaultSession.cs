using System.Text;
using ApiKeyManager;

namespace ApiKeyManager.Application;

public enum ResultCode { Success, Validation, Locked, SessionChanged, StorageFailure, InvalidVault, BackupFailure }
public sealed record OperationResult(ResultCode Code, string Message = "")
{
    public bool Succeeded => Code == ResultCode.Success;
    public static OperationResult Ok(string message = "") => new(ResultCode.Success, message);
}

public sealed record EntrySummary(string Id, string Name, string Provider, string KeyHint, bool HasKey,
    string BaseUrl, string Model, string Tags, string Notes, DateTime? Expires, DateTime CreatedUtc, DateTime UpdatedUtc);

/// <summary>导入预览只暴露计数，解密后的候选由会话持有，不能从 UI 就地修改。</summary>
public sealed record ImportPreview(int Incoming, int Added, int Duplicate, int Current, bool HasSettings);

/// <summary>
/// 拥有凭据和已保存数据。所有写入串行执行，候选快照成功写入后才发布。
/// Lock 可同步使当前会话失效，已开始的磁盘写入收尾但不能把数据回填到锁定界面。
/// </summary>
public sealed class VaultSession
{
    private readonly IVaultStorage _storage;
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly object _sync = new();
    private VaultData? _data;
    private string? _password;
    private long _generation;
    private VaultData? _pendingImport;
    private long _importGeneration;
    public bool Exists => _storage.Exists;
    public bool IsUnlocked { get { lock (_sync) return _data != null; } }
    public long Generation { get { lock (_sync) return _generation; } }
    public VaultSession(IVaultStorage storage) => _storage = storage;

    public IReadOnlyList<EntrySummary> GetEntries()
    {
        lock (_sync) return _data?.Entries.Select(e => new EntrySummary(e.Id, e.Name, e.Provider, KeyMask.Format(e.ApiKey),
            e.ApiKey.Length > 0, e.BaseUrl, e.Model, e.Tags, e.Notes, e.ExpiresUtc, e.CreatedUtc, e.UpdatedUtc)).ToArray() ?? [];
    }

    public ApiEntry? GetDraft(string id)
    {
        lock (_sync) return _data?.Entries.FirstOrDefault(e => e.Id == id)?.Clone();
    }

    public string? GetSecret(string id)
    {
        lock (_sync) return _data?.Entries.FirstOrDefault(e => e.Id == id)?.ApiKey;
    }

    public void Lock()
    {
        lock (_sync) { _generation++; _data = null; _password = null; _pendingImport = null; }
    }

    public async Task<OperationResult> UnlockAsync(string password, bool create = false)
    {
        if (string.IsNullOrEmpty(password) || (create && password.Length < 8))
            return new(ResultCode.Validation, create ? "主密码至少需要 8 位。" : "请输入主密码。");
        var version = Generation;
        await _writes.WaitAsync().ConfigureAwait(false);
        try
        {
            var message = "";
            var data = await Task.Run(() =>
            {
                if (Generation != version) return null;
                if (create)
                {
                    if (_storage.Exists) throw new InvalidOperationException("库已存在，请解锁。");
                    _storage.Save(new VaultData(), password);
                }
                var loaded = _storage.Load(password);
                Validate(loaded);
                if (_storage.Inspect() is { NeedsUpgrade: true })
                {
                    try
                    {
                        _storage.Backup(); // 失败不继续升级。
                        if (Generation != version) return null;
                        _storage.Save(loaded, password);
                        var verified = _storage.Load(password);
                        if (!SameEntries(loaded, verified)) throw new IOException("升级回读不一致。");
                        message = "已备份并升级加密强度。";
                    }
                    catch { message = "已解锁；加密强度升级未完成，下次解锁重试。"; }
                }
                return loaded;
            }).ConfigureAwait(false);
            lock (_sync)
            {
                if (_generation != version || data == null) return Changed();
                _data = Copy(data); _password = password; _generation++; _pendingImport = null;
                return OperationResult.Ok(message);
            }
        }
        catch (VaultException) { return new(ResultCode.InvalidVault, "主密码错误，或库文件已损坏。请检查后重试。"); }
        catch (InvalidOperationException ex) { return new(ResultCode.Validation, ex.Message); }
        catch { return StorageError(); }
        finally { _writes.Release(); }
    }

    public Task<OperationResult> SaveEntryAsync(ApiEntry draft, long generation)
    {
        if (string.IsNullOrWhiteSpace(draft.Name)) return Task.FromResult(new OperationResult(ResultCode.Validation, "名称不能为空。"));
        var detached = draft.Clone();
        return CommitAsync(generation, data =>
        {
            var old = data.Entries.FindIndex(e => e.Id == detached.Id);
            detached.Name = detached.Name.Trim();
            detached.UpdatedUtc = DateTime.UtcNow;
            if (old >= 0) { detached.CreatedUtc = data.Entries[old].CreatedUtc; data.Entries[old] = detached; }
            else { detached.CreatedUtc = detached.UpdatedUtc; data.Entries.Add(detached); }
        });
    }

    public Task<OperationResult> DeleteAsync(string id, long generation) =>
        CommitAsync(generation, data => data.Entries.RemoveAll(e => e.Id == id));

    private async Task<OperationResult> CommitAsync(long generation, Action<VaultData> change, bool requireBackup = false, string? newPassword = null, string? oldPassword = null)
    {
        await _writes.WaitAsync().ConfigureAwait(false);
        try
        {
            VaultData candidate;
            string password;
            lock (_sync)
            {
                if (_data == null || _password == null) return Locked();
                if (_generation != generation) return Changed();
                if (oldPassword != null && oldPassword != _password) return new(ResultCode.Validation, "当前主密码不正确。");
                candidate = Copy(_data); password = _password;
            }
            change(candidate);
            if (requireBackup)
            {
                try { await Task.Run(_storage.Backup).ConfigureAwait(false); }
                catch { return new(ResultCode.BackupFailure, "无法创建恢复备份，已停止覆盖。请检查目录权限和可用空间。"); }
            }
            if (Generation != generation) return Changed();
            await Task.Run(() => _storage.Save(candidate, newPassword ?? password)).ConfigureAwait(false);
            lock (_sync)
            {
                if (_generation != generation || _data == null) return Changed();
                _data = candidate;
                if (newPassword != null) _password = newPassword;
                _pendingImport = null;
            }
            return OperationResult.Ok();
        }
        catch { return StorageError(); }
        finally { _writes.Release(); }
    }

    public Task<OperationResult> ChangePasswordAsync(string current, string next, long generation)
    {
        if (next.Length < 8) return Task.FromResult(new OperationResult(ResultCode.Validation, "新主密码至少需要 8 位。"));
        if (next == current) return Task.FromResult(new OperationResult(ResultCode.Validation, "新密码不能与当前密码相同。"));
        return CommitAsync(generation, _ => { }, newPassword: next, oldPassword: current);
    }

    public async Task<(OperationResult Result, ImportPreview? Preview)> PrepareImportAsync(string path, string password)
    {
        CancelImport();
        var generation = Generation;
        if (!IsUnlocked) return (Locked(), null);
        try
        {
            var incoming = await Task.Run(() => VaultStore.Load(path, password)).ConfigureAwait(false);
            Validate(incoming);
            // 拒绝有重复 Id 的歧义文件，不悄悄让重复项覆盖。
            if (incoming.Entries.Any(e => string.IsNullOrWhiteSpace(e.Id)) || incoming.Entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != incoming.Entries.Count)
                return (new(ResultCode.Validation, "备份包含缺失或重复的记录 Id，无法安全导入。"), null);
            lock (_sync)
            {
                if (_generation != generation || _data == null) return (Changed(), null);
                _pendingImport = Copy(incoming); _importGeneration = generation;
                var ids = _data.Entries.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
                var added = incoming.Entries.Count(e => !ids.Contains(e.Id));
                return (OperationResult.Ok(), new(incoming.Entries.Count, added, incoming.Entries.Count - added, _data.Entries.Count, incoming.Settings != null));
            }
        }
        catch (VaultException) { return (new(ResultCode.InvalidVault, "备份密码错误，或备份文件已损坏。"), null); }
        catch { return (StorageError(), null); }
    }

    public void CancelImport() { lock (_sync) _pendingImport = null; }

    public async Task<(OperationResult Result, AppSettings? Preferences)> CommitImportAsync(bool replace, bool restorePreferences)
    {
        VaultData incoming;
        long generation;
        lock (_sync)
        {
            if (_pendingImport == null) return (new(ResultCode.Validation, "请重新选择并预览备份。"), null);
            incoming = Copy(_pendingImport); generation = _importGeneration;
        }
        var result = await CommitAsync(generation, data =>
        {
            if (replace) data.Entries = incoming.Entries;
            else
            {
                var ids = data.Entries.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
                data.Entries.AddRange(incoming.Entries.Where(e => ids.Add(e.Id)));
            }
        }, requireBackup: replace).ConfigureAwait(false);
        var settings = result.Succeeded && restorePreferences ? incoming.Settings : null;
        if (settings != null) settings.ShowKeys = false;
        return (result, settings);
    }

    public async Task<OperationResult> ExportAsync(string path, string? backupPassword, AppSettings settings, bool csv = false)
    {
        if (_storage is FileVaultStorage file)
        {
            var target = Path.GetFullPath(path);
            if (target.Equals(file.PathName, StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith(file.PathName + ".", StringComparison.OrdinalIgnoreCase) ||
                target.Equals(Path.Combine(Path.GetDirectoryName(file.PathName)!, "settings.json"), StringComparison.OrdinalIgnoreCase))
                return new(ResultCode.Validation, "导出文件不能覆盖当前库、锁文件或设置文件。请选择其他文件名。");
        }
        VaultData data;
        long generation;
        lock (_sync)
        {
            if (_data == null) return Locked();
            data = Copy(_data); generation = _generation;
        }
        if (!csv && string.IsNullOrEmpty(backupPassword)) return new(ResultCode.Validation, "请输入备份密码。");
        data.Settings = new() { Theme = settings.Theme, AutoLockMinutes = settings.AutoLockMinutes, ClipboardClearSeconds = settings.ClipboardClearSeconds, ShowKeys = false };
        try
        {
            if (Generation != generation) return Changed();
            await Task.Run(() =>
            {
                if (csv)
                {
                    var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { File.WriteAllText(temp, CsvExporter.Build(data.Entries), new UTF8Encoding(true)); File.Move(temp, path, true); }
                    finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
                }
                else new FileVaultStorage(path).Save(data, backupPassword!);
            }).ConfigureAwait(false);
            return Generation == generation ? OperationResult.Ok() : Changed();
        }
        catch { return StorageError(); }
    }

    private static void Validate(VaultData data)
    {
        if (data.Entries == null || data.Entries.Any(e => e == null || string.IsNullOrWhiteSpace(e.Id)) ||
            data.Entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != data.Entries.Count)
            throw new VaultException("记录结构无效。");
        foreach (var e in data.Entries)
        {
            e.Name ??= ""; e.Provider ??= ""; e.ApiKey ??= ""; e.BaseUrl ??= "";
            e.Model ??= ""; e.Tags ??= ""; e.Notes ??= "";
        }
    }

    private static VaultData Copy(VaultData data) => new()
    {
        Entries = data.Entries.Select(e => e.Clone()).ToList(),
        Settings = data.Settings == null ? null : new() { Theme = data.Settings.Theme, AutoLockMinutes = data.Settings.AutoLockMinutes, ClipboardClearSeconds = data.Settings.ClipboardClearSeconds, ShowKeys = data.Settings.ShowKeys }
    };
    private static bool SameEntries(VaultData a, VaultData b) => System.Text.Json.JsonSerializer.Serialize(a.Entries) == System.Text.Json.JsonSerializer.Serialize(b.Entries);
    private static OperationResult Changed() => new(ResultCode.SessionChanged, "会话已改变，请解锁后重新操作。已开始的写入可能已经完成。");
    private static OperationResult Locked() => new(ResultCode.Locked, "空间已锁定，请先解锁。");
    private static OperationResult StorageError() => new(ResultCode.StorageFailure, "文件操作失败，请检查目录权限、文件占用和可用空间后重试。");
}
