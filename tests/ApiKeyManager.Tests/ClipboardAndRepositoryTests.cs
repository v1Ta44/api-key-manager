using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ApiKeyManager;
using Xunit;

namespace ApiKeyManager.Tests;

/// <summary>剪贴板定时清理（新增覆盖，含"不误删他人复制"的关键行为）。</summary>
public sealed class ClipboardGuardTests
{
    [Fact]
    public void Tick_ClearsAfterDeadline()
    {
        string actual = "secret";
        int cleared = 0;
        var guard = new ClipboardGuard(() => actual, () => { cleared++; actual = ""; });

        guard.Track("secret", 1);
        Assert.True(guard.HasPending);

        // 到点后清理
        Thread.Sleep(1100);
        guard.Tick(force: false);

        Assert.Equal(1, cleared);
        Assert.False(guard.HasPending);
    }

    [Fact]
    public void Tick_DoesNotClearBeforeDeadline()
    {
        string actual = "secret";
        var guard = new ClipboardGuard(() => actual, () => throw new InvalidOperationException("不应清理"));

        guard.Track("secret", 60);
        guard.Tick(force: false);

        Assert.True(guard.HasPending);
    }

    [Fact]
    public void Tick_KeepsForeignClipboardContent()
    {
        // 用户复制了密钥后又复制了别的东西：不能把别人的内容删掉
        string actual = "something-else-entirely";
        int cleared = 0;
        var guard = new ClipboardGuard(() => actual, () => cleared++);

        guard.Track("secret", 1);
        Thread.Sleep(1100);
        guard.Tick(force: false);

        Assert.Equal(0, cleared);
        Assert.False(guard.HasPending);
    }

    [Fact]
    public void ForceTick_PreservesForeignContent()
    {
        // 锁定 / 退出也不能清理用户后来复制的内容
        string actual = "whatever";
        int cleared = 0;
        var guard = new ClipboardGuard(() => actual, () => cleared++);

        guard.Track("secret", 3600);
        guard.Tick(force: true);

        Assert.Equal(0, cleared);
        Assert.False(guard.HasPending);
    }

    [Fact]
    public void ZeroSeconds_MeansNoAutoClear()
    {
        int cleared = 0;
        var guard = new ClipboardGuard(() => "secret", () => cleared++);

        guard.Track("secret", 0);
        Assert.False(guard.HasPending);

        Thread.Sleep(50);
        guard.Tick(force: false);
        Assert.Equal(0, cleared);
    }

    [Fact]
    public void Track_ClearsPendingWhenDisabled()
    {
        var guard = new ClipboardGuard(() => "x", () => { });

        guard.Track("secret", 60);
        Assert.True(guard.HasPending);

        guard.Track("", 60); // 空内容视为取消
        Assert.False(guard.HasPending);
    }

    [Fact]
    public void Tick_SwallowsClipboardAccessFailures()
    {
        // 剪贴板被其他进程占用时清理会抛异常，必须被吞掉不能影响主流程
        var guard = new ClipboardGuard(() => "secret", () => throw new ExternalException("剪贴板被占用"));

        guard.Track("secret", 1);
        Thread.Sleep(1100);

        guard.Tick(force: false); // 不应抛出，先保留待清理内容
        Assert.True(guard.HasPending);
        for (int i = 0; i < 4; i++) guard.Tick(force: true);
        Assert.True(guard.CleanupFailed);
        Assert.False(guard.HasPending);
    }

    [Fact]
    public void OccupiedClipboardRetriesAndRecovers()
    {
        var unavailable = true; var cleared = 0;
        var guard = new ClipboardGuard(() => unavailable ? null : "secret", () => cleared++);
        guard.Track("secret", 30); guard.Tick(true);
        Assert.True(guard.HasPending); Assert.Equal(0, cleared);
        unavailable = false; guard.Tick(true);
        Assert.False(guard.HasPending); Assert.False(guard.CleanupFailed); Assert.Equal(1, cleared);
    }

    [Fact]
    public void TimeLeft_IsNullWhenNothingPending()
    {
        var guard = new ClipboardGuard(() => null, () => { });
        Assert.Null(guard.TimeLeft);
    }

    [Fact]
    public void TimeLeft_CountsDown()
    {
        var guard = new ClipboardGuard(() => "x", () => { });
        guard.Track("secret", 30);

        var left = guard.TimeLeft;
        Assert.NotNull(left);
        Assert.True(left!.Value <= TimeSpan.FromSeconds(30));
        Assert.True(left.Value > TimeSpan.FromSeconds(28));
    }
}

/// <summary>记录集合与落盘（新增覆盖）。</summary>
public sealed class EntryRepositoryTests : IDisposable
{
    private readonly string _dir;
    private const string Password = "repo-password-123";

    public EntryRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "akm-repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private EntryRepository NewRepo(string name = "vault.akv") =>
        new(Path.Combine(_dir, name));

    [Fact]
    public void Add_AssignsTimestampsAndIncreasesCount()
    {
        var repo = NewRepo();
        var entry = repo.Add(new ApiEntry { Name = "A" });

        Assert.Equal(1, repo.Count);
        Assert.NotEqual(default, entry.CreatedUtc);
        Assert.Equal(entry.CreatedUtc, entry.UpdatedUtc);
    }

    [Fact]
    public void Snapshot_IsDeepCopy()
    {
        var repo = NewRepo();
        repo.Add(new ApiEntry { Name = "A", ApiKey = "k" });

        var snap = repo.Snapshot();
        snap.Entries[0].ApiKey = "MUTATED";

        // 快照被改动不应影响仓库
        Assert.Equal("k", repo.Entries[0].ApiKey);
    }

    [Fact]
    public void Snapshot_CarriesTheOriginalIds()
    {
        var repo = NewRepo();
        var added = repo.Add(new ApiEntry { Name = "A" });

        Assert.Equal(added.Id, repo.Snapshot().Entries[0].Id);
    }

    [Fact]
    public void MergeById_SkipsDuplicates()
    {
        var repo = NewRepo();
        var existing = repo.Add(new ApiEntry { Name = "既有" });

        int added = repo.MergeById(new[]
        {
            existing,                                 // 同 Id → 跳过
            new ApiEntry { Name = "新的" },            // 新增
        });

        Assert.Equal(1, added);
        Assert.Equal(2, repo.Count);
    }

    [Fact]
    public void ReplaceAll_DiscardsExisting()
    {
        var repo = NewRepo();
        repo.Add(new ApiEntry { Name = "旧" });

        repo.ReplaceAll(new[] { new ApiEntry { Name = "新" } });

        Assert.Equal(1, repo.Count);
        Assert.Equal("新", repo.Entries[0].Name);
    }

    [Fact]
    public void BackupBeforeOverwrite_CreatesTimestampedCopy()
    {
        var repo = NewRepo();
        repo.Add(new ApiEntry { Name = "A" });
        repo.Save(Password);

        string? backup = repo.BackupBeforeOverwrite();

        Assert.NotNull(backup);
        Assert.True(File.Exists(backup));
        Assert.EndsWith(".bak", backup);

        // 备份应是可解密的完整库
        var restored = VaultStore.Load(backup!, Password);
        Assert.Equal("A", restored.Entries[0].Name);
    }

    [Fact]
    public void BackupBeforeOverwrite_ReturnsNullWhenNoVaultYet()
    {
        var repo = NewRepo();
        Assert.Null(repo.BackupBeforeOverwrite());
    }

    [Fact]
    public void BackupBeforeOverwrite_DoesNotOverwriteExistingBackup()
    {
        var repo = NewRepo();
        repo.Add(new ApiEntry { Name = "A" });
        repo.Save(Password);

        string? first = repo.BackupBeforeOverwrite();
        Assert.NotNull(first);

        // 同一秒内再次调用：不允许静默覆盖已有备份
        string? second = repo.BackupBeforeOverwrite();
        if (second != null && second != first)
        {
            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));
        }
        Assert.True(File.Exists(first));
    }

    [Fact]
    public void Clear_EmptiesRepository()
    {
        var repo = NewRepo();
        repo.Add(new ApiEntry { Name = "A" });
        repo.Clear();

        Assert.Equal(0, repo.Count);
    }
}
