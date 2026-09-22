using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ApiKeyManager;
using Xunit;

namespace ApiKeyManager.Tests;

/// <summary>迭代次数策略（纯逻辑）。</summary>
public sealed class IterationPolicyTests
{
    [Fact]
    public void CurrentMeetsRecommendedFloor()
    {
        // OWASP 对 PBKDF2-HMAC-SHA256 的建议下限是 600 000
        Assert.True(IterationPolicy.Current >= 600_000);
    }

    [Fact]
    public void ThresholdIsHalfOfCurrent()
    {
        Assert.Equal(IterationPolicy.Current / 2, IterationPolicy.UpgradeThreshold);
    }

    [Theory]
    [InlineData(1_000, true)]
    [InlineData(100_000, true)]
    [InlineData(299_999, true)]   // 老库默认值 300_000 之下
    [InlineData(300_000, false)]  // 旧版写入的 300_000 已经 >= 阈值的一半
    [InlineData(600_000, false)]
    [InlineData(1_000_000, false)]
    public void NeedsUpgrade_OnlyFlagsClearlyWeakValues(int iterations, bool expected)
    {
        Assert.Equal(expected, IterationPolicy.NeedsUpgrade(iterations));
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData(1_000, true)]
    [InlineData(600_000, true)]
    [InlineData(10_000_000, true)]
    [InlineData(10_000_001, false)]
    [InlineData(int.MaxValue, false)]
    public void IsAcceptable_BoundsCheck(int iterations, bool expected)
    {
        Assert.Equal(expected, IterationPolicy.IsAcceptable(iterations));
    }

    [Fact]
    public void MinAcceptableIsBelowUpgradeThreshold()
    {
        // 很老的库必须还能打开（打开后升级），而不是被当成"头已损坏"
        Assert.True(IterationPolicy.MinAcceptable < IterationPolicy.UpgradeThreshold);
    }

    [Theory]
    [InlineData(50_000, "偏弱")]
    [InlineData(299_999, "一般")]
    [InlineData(400_000, "良好")]
    [InlineData(600_000, "强")]
    public void Describe_MapsToExpectedLabel(int iterations, string expected)
    {
        Assert.Equal(expected, IterationPolicy.Describe(iterations));
    }
}

/// <summary>库文件头探测与强度升级（不需要口令即可读迭代数）。</summary>
public sealed class VaultUpgradeTests : IDisposable
{
    private readonly string _dir;
    private const string Password = "upgrade-test-pass";

    public VaultUpgradeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "akm-upgrade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string VaultPath => Path.Combine(_dir, "vault.akv");

    private static VaultData Data(params string[] names)
    {
        var d = new VaultData();
        foreach (var n in names) d.Entries.Add(new ApiEntry { Name = n, ApiKey = "k-" + n });
        return d;
    }

    // ---------------- Inspect ----------------

    [Fact]
    public void Inspect_ReturnsNullWhenFileMissing()
    {
        Assert.Null(VaultStore.Inspect(Path.Combine(_dir, "nope.akv")));
    }

    [Fact]
    public void Inspect_ReturnsNullForNonVaultFile()
    {
        string path = Path.Combine(_dir, "junk.akv");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(new string('x', 200)));

        Assert.Null(VaultStore.Inspect(path));
    }

    [Fact]
    public void Inspect_ReturnsNullForTruncatedFile()
    {
        string path = Path.Combine(_dir, "short.akv");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("AKVLT001"));

        Assert.Null(VaultStore.Inspect(path));
    }

    [Fact]
    public void Inspect_ReadsIterationsWithoutPassword()
    {
        VaultStore.Save(VaultPath, Data("A"), Password, 12_345);

        var meta = VaultStore.Inspect(VaultPath);

        Assert.NotNull(meta);
        Assert.Equal(1, meta!.Value.FormatVersion);
        Assert.Equal(12_345, meta.Value.Iterations);
    }

    [Fact]
    public void Inspect_WorksEvenWithWrongPassword()
    {
        // 迭代数在未认证的明文头里，探测不需要（也无法验证）口令
        VaultStore.Save(VaultPath, Data("A"), Password, 5_000);

        Assert.Equal(5_000, VaultStore.Inspect(VaultPath)!.Value.Iterations);
    }

    [Fact]
    public void Inspect_RejectsOutOfRangeIterations()
    {
        // 手工构造一个迭代数荒谬的头：应被视为无效，而不是尝试计算（可能挂起）
        byte[] raw = BuildRawHeader(iterations: 50_000_000);
        string path = Path.Combine(_dir, "hostile.akv");
        File.WriteAllBytes(path, raw);

        Assert.Null(VaultStore.Inspect(path));
        Assert.Throws<VaultException>(() => VaultStore.Parse(raw, Password));
    }

    [Fact]
    public void Metadata_FlagsWeakVaultForUpgrade()
    {
        VaultStore.Save(VaultPath, Data("A"), Password, 10_000);

        var meta = VaultStore.Inspect(VaultPath)!.Value;

        Assert.True(meta.NeedsUpgrade);
        Assert.Equal("偏弱", meta.Strength);
    }

    [Fact]
    public void Metadata_DoesNotFlagCurrentVault()
    {
        VaultStore.Save(VaultPath, Data("A"), Password, IterationPolicy.Current);

        var meta = VaultStore.Inspect(VaultPath)!.Value;

        Assert.False(meta.NeedsUpgrade);
        Assert.Equal("强", meta.Strength);
    }

    // ---------------- 升级本身 ----------------

    [Fact]
    public void Upgrade_RaisesIterationsAndPreservesData()
    {
        VaultStore.Save(VaultPath, Data("甲", "乙"), Password, 10_000);

        var repo = new EntryRepository(VaultPath);
        repo.Load(VaultStore.Load(VaultPath, Password));
        repo.Save(Password, IterationPolicy.Current);

        var meta = VaultStore.Inspect(VaultPath)!.Value;
        Assert.Equal(IterationPolicy.Current, meta.Iterations);
        Assert.False(meta.NeedsUpgrade);

        // 同一口令仍能解开，且内容完好
        var reloaded = VaultStore.Load(VaultPath, Password);
        Assert.Equal(2, reloaded.Entries.Count);
        Assert.Equal("甲", reloaded.Entries[0].Name);
        Assert.Equal("k-甲", reloaded.Entries[0].ApiKey);
    }

    [Fact]
    public void Upgrade_ChangesSaltSoCiphertextDiffers()
    {
        // 升级必须换新盐/新 nonce，否则等于用旧参数原地覆盖，达不到提升效果
        VaultStore.Save(VaultPath, Data("A"), Password, 10_000);
        byte[] before = File.ReadAllBytes(VaultPath);

        var repo = new EntryRepository(VaultPath);
        repo.Load(VaultStore.Load(VaultPath, Password));
        repo.Save(Password, IterationPolicy.Current);

        byte[] after = File.ReadAllBytes(VaultPath);

        Assert.NotEqual(before.AsSpan(13, 16).ToArray(), after.AsSpan(13, 16).ToArray()); // 盐
        Assert.NotEqual(before.AsSpan(29, 12).ToArray(), after.AsSpan(29, 12).ToArray()); // nonce
    }

    [Fact]
    public void Upgrade_IsIdempotent()
    {
        VaultStore.Save(VaultPath, Data("A"), Password, 10_000);

        var repo = new EntryRepository(VaultPath);
        repo.Load(VaultStore.Load(VaultPath, Password));

        repo.Save(Password, IterationPolicy.Current);
        byte[] first = File.ReadAllBytes(VaultPath);

        repo.Save(Password, IterationPolicy.Current);
        byte[] second = File.ReadAllBytes(VaultPath);

        Assert.Equal(IterationPolicy.Current, VaultStore.Inspect(VaultPath)!.Value.Iterations);
        // 再写一次不应改变迭代数（密文必然不同，因为盐每次随机）
        Assert.Equal(first.AsSpan(9, 4).ToArray(), second.AsSpan(9, 4).ToArray());
        Assert.False(VaultStore.Inspect(VaultPath)!.Value.NeedsUpgrade);
    }

    [Fact]
    public void VaultWrittenWithWrongPasswordIsUnreadable()
    {
        // 用错口令做升级会写出一个打不开的库——这正是升级只在
        // 成功解密之后触发的原因。这里固化该前提，防止将来误用。
        VaultStore.Save(VaultPath, Data("A"), Password, 10_000);

        var repo = new EntryRepository(VaultPath);
        repo.Load(VaultStore.Load(VaultPath, Password));
        repo.Save("wrong-password", IterationPolicy.Current);

        Assert.Throws<VaultException>(() => VaultStore.Load(VaultPath, Password));
        Assert.Equal(IterationPolicy.Current, VaultStore.Inspect(VaultPath)!.Value.Iterations);
    }

    [Fact]
    public void OldVaultWithVeryLowIterations_StillOpens()
    {
        // 极老的库（低于升级阈值但仍可接受）必须能打开，否则用户被锁在门外
        VaultStore.Save(VaultPath, Data("A"), Password, IterationPolicy.MinAcceptable);

        var loaded = VaultStore.Load(VaultPath, Password);

        Assert.Single(loaded.Entries);
        Assert.True(VaultStore.Inspect(VaultPath)!.Value.NeedsUpgrade);
    }

    [Fact]
    public void HeaderTamperingBetweenInspectAndLoad_IsCaughtByAad()
    {
        // 攻击者改了明文头里的迭代数：Inspect 会照读，但解密失败（AAD 绑定）
        VaultStore.Save(VaultPath, Data("A"), Password, IterationPolicy.Current);

        byte[] raw = File.ReadAllBytes(VaultPath);
        raw[9] ^= 0x01; // 改迭代数最低字节

        Assert.Throws<VaultException>(() => VaultStore.Parse(raw, Password));
    }

    private static byte[] BuildRawHeader(int iterations)
    {
        byte[] header = new byte[41];
        Buffer.BlockCopy(Encoding.ASCII.GetBytes("AKVLT001"), 0, header, 0, 8);
        header[8] = 1;
        Buffer.BlockCopy(BitConverter.GetBytes(iterations), 0, header, 9, 4);
        // 盐 / nonce / tag 留零即可——只测头部校验
        return header;
    }
}
