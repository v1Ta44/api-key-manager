using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ApiKeyManager;

/// <summary>
/// 记录集合 + 落盘逻辑。把 MainForm 里"数据侧"的职责抽出来，
/// 使增删改查与导入导出不依赖任何 WinForms 控件。
/// </summary>
public sealed class EntryRepository
{
    private readonly List<ApiEntry> _entries = new();

    public string VaultPath { get; }

    public EntryRepository(string vaultPath)
    {
        VaultPath = vaultPath;
    }

    public IReadOnlyList<ApiEntry> Entries => _entries;

    public int Count => _entries.Count;

    public void Load(VaultData data)
    {
        _entries.Clear();
        _entries.AddRange(data.Entries);
    }

    public VaultData Snapshot() =>
        new() { Entries = _entries.Select(e => e.Clone()).ToList() };

    public ApiEntry Add(ApiEntry entry)
    {
        entry.CreatedUtc = entry.UpdatedUtc = DateTime.UtcNow;
        _entries.Add(entry);
        return entry;
    }

    public bool Remove(ApiEntry entry) => _entries.Remove(entry);

    /// <summary>用指定口令把当前记录写回库文件。</summary>
    public void Save(string password) => VaultStore.Save(VaultPath, Snapshot(), password);

    /// <summary>用指定口令 + 指定迭代次数写回库文件（用于强度升级）。</summary>
    public void Save(string password, int iterations) =>
        VaultStore.Save(VaultPath, Snapshot(), password, iterations);

    /// <summary>
    /// 探测当前库文件的迭代次数；文件不存在或不是有效库时返回 null。
    /// </summary>
    public VaultMetadata? Inspect() => VaultStore.Inspect(VaultPath);

    public void Clear() => _entries.Clear();

    /// <summary>替换整个库（导入"替换"模式）。</summary>
    public void ReplaceAll(IEnumerable<ApiEntry> items)
    {
        _entries.Clear();
        _entries.AddRange(items);
    }

    /// <summary>按 Id 合并（导入"合并"模式）。返回实际新增条数。</summary>
    public int MergeById(IEnumerable<ApiEntry> items)
    {
        var ids = new HashSet<string>(_entries.Select(e => e.Id), StringComparer.Ordinal);
        int added = 0;
        foreach (var e in items)
        {
            if (ids.Add(e.Id))
            {
                _entries.Add(e);
                added++;
            }
        }
        return added;
    }

    /// <summary>
    /// 覆盖库文件前先落一份带时间戳的副本。
    /// 导入"替换"等破坏性操作前调用，是零成本的数据保险。
    /// 返回备份路径；库文件不存在或复制失败时返回 null（不阻断主流程）。
    /// </summary>
    public string? BackupBeforeOverwrite()
    {
        if (!File.Exists(VaultPath)) return null;
        try
        {
            string backup = $"{VaultPath}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            File.Copy(VaultPath, backup, overwrite: false);
            return backup;
        }
        catch
        {
            return null;
        }
    }
}
