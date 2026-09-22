using System;
using System.IO;
using ApiKeyManager;
using Xunit;

namespace ApiKeyManager.Tests;

/// <summary>
/// 端到端复现 MainForm.MaybeUpgradeIterations 的完整流程：
/// 造旧库 → 用口令解锁 → 备份 → 用新迭代数重写 → 回读校验。
/// 保证 UI 里那段逻辑的每一步语义在这里被固定下来。
/// </summary>
public sealed class IterationUpgradeFlowTests : IDisposable
{
    private readonly string _dir;
    private const string Password = "flow-test-pass-123";

    public IterationUpgradeFlowTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "akm-flow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string VaultPath => Path.Combine(_dir, "vault.akv");

    /// <summary>模拟 MainForm 解锁后调用 MaybeUpgradeIterations 的返回结果。</summary>
    private (bool Upgraded, string Status) UnlockAndUpgrade(string password)
    {
        // 1. 解锁（这一步失败就抛 VaultException，与 UI 里的重试循环一致）
        var data = VaultStore.Load(VaultPath, password);

        // 2. 装载到仓库
        var repo = new EntryRepository(VaultPath);
        repo.Load(data);

        // 3. 探测是否需要升级
        var meta = repo.Inspect();
        if (meta is not { } info || !info.NeedsUpgrade)
            return (false, "无需升级");

        // 4. 备份 → 重写 → 回读校验
        repo.BackupBeforeOverwrite();
        repo.Save(password, IterationPolicy.Current);

        var check = VaultStore.Load(VaultPath, password);
        if (check.Entries.Count != repo.Count)
            throw new VaultException("回读校验的记录数与预期不一致。");

        return (true, $"已升级到 {IterationPolicy.Current:N0} 次迭代");
    }

    [Fact]
    public void LegacyVault_IsUpgradedOnUnlock()
    {
        // 用旧版默认值 300_000 造库（当前阈值之下 → 会被判定为需要升级）
        VaultStore.Save(VaultPath, new VaultData
        {
            Entries =
            {
                new ApiEntry { Name = "OpenAI", ApiKey = "sk-legacy-1" },
                new ApiEntry { Name = "DeepSeek", ApiKey = "sk-legacy-2" },
            }
        }, Password, 50_000);

        Assert.Equal(50_000, VaultStore.Inspect(VaultPath)!.Value.Iterations);

        var (upgraded, status) = UnlockAndUpgrade(Password);

        Assert.True(upgraded);
        Assert.Contains("600,000", status);
        Assert.Equal(IterationPolicy.Current, VaultStore.Inspect(VaultPath)!.Value.Iterations);

        // 数据完整
        var final = VaultStore.Load(VaultPath, Password);
        Assert.Equal(2, final.Entries.Count);
        Assert.Equal("sk-legacy-1", final.Entries[0].ApiKey);
    }

    [Fact]
    public void SecondUnlock_DoesNotUpgradeAgain()
    {
        VaultStore.Save(VaultPath, new VaultData { Entries = { new ApiEntry { Name = "A" } } },
            Password, 50_000);

        Assert.True(UnlockAndUpgrade(Password).Upgraded);
        Assert.False(UnlockAndUpgrade(Password).Upgraded); // 第二次无需升级
    }

    [Fact]
    public void UpgradeLeavesRecoverableBackup()
    {
        VaultStore.Save(VaultPath, new VaultData { Entries = { new ApiEntry { Name = "重要", ApiKey = "k" } } },
            Password, 50_000);

        UnlockAndUpgrade(Password);

        var baks = Directory.GetFiles(_dir, "*.bak");
        Assert.NotEmpty(baks);

        // 备份应能用同一口令解开，且保留原迭代数（即升级前的状态）
        var restored = VaultStore.Load(baks[0], Password);
        Assert.Equal("重要", restored.Entries[0].Name);
        Assert.Equal(50_000, VaultStore.Inspect(baks[0])!.Value.Iterations);
    }

    [Fact]
    public void WrongPassword_ThrowsBeforeAnyWrite()
    {
        VaultStore.Save(VaultPath, new VaultData { Entries = { new ApiEntry { Name = "A" } } },
            Password, 50_000);
        byte[] before = File.ReadAllBytes(VaultPath);

        Assert.Throws<VaultException>(() => UnlockAndUpgrade("not-the-password"));

        // 口令错误时不允许动库文件
        Assert.Equal(before, File.ReadAllBytes(VaultPath));
        Assert.Empty(Directory.GetFiles(_dir, "*.bak"));
    }

    [Fact]
    public void AlreadyStrongVault_IsLeftUntouched()
    {
        VaultStore.Save(VaultPath, new VaultData { Entries = { new ApiEntry { Name = "A" } } },
            Password, IterationPolicy.Current);
        byte[] before = File.ReadAllBytes(VaultPath);

        var (upgraded, _) = UnlockAndUpgrade(Password);

        Assert.False(upgraded);
        Assert.Equal(before, File.ReadAllBytes(VaultPath)); // 一个字节都没变
    }

    [Fact]
    public void UpgradeSurvivesEmptyVault()
    {
        VaultStore.Save(VaultPath, new VaultData(), Password, 10_000);

        var (upgraded, _) = UnlockAndUpgrade(Password);

        Assert.True(upgraded);
        Assert.Empty(VaultStore.Load(VaultPath, Password).Entries);
    }
}
