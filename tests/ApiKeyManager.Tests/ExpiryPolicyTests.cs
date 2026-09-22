using System;
using System.Collections.Generic;
using System.Linq;
using ApiKeyManager;
using Xunit;

namespace ApiKeyManager.Tests;

/// <summary>到期判定（日期边界是重点）。</summary>
public sealed class ExpiryPolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 14, 30, 0, DateTimeKind.Local);

    private static ApiEntry With(DateTime? expires) =>
        new() { Name = "x", ApiKey = "k", ExpiresUtc = expires };

    [Fact]
    public void NoExpiry_IsNone()
    {
        var info = ExpiryPolicy.Evaluate(With(null), Now);

        Assert.Equal(ExpiryState.None, info.State);
        Assert.Null(info.DaysLeft);
        Assert.False(info.NeedsAttention);
        Assert.Equal("未设置到期日", info.Describe());
    }

    [Fact]
    public void ZeroDaysLeft_IsExpiringSoonNotExpired()
    {
        // 当天到期：必须算"即将到期"，不能因为时分秒已过 14:30 就报已过期
        var info = ExpiryPolicy.Evaluate(With(Now.Date), Now);

        Assert.Equal(ExpiryState.ExpiringSoon, info.State);
        Assert.Equal(0, info.DaysLeft);
        Assert.Equal("今天到期", info.Describe());
    }

    [Fact]
    public void Yesterday_IsExpired()
    {
        var info = ExpiryPolicy.Evaluate(With(Now.Date.AddDays(-1)), Now);

        Assert.Equal(ExpiryState.Expired, info.State);
        Assert.Equal(-1, info.DaysLeft);
        Assert.Equal("已过期 1 天", info.Describe());
    }

    [Fact]
    public void LaterTodayButPastTime_IsStillExpiringSoon()
    {
        // 昨天 23:59 已过；但"今天 00:01"虽然早于 now，仍应是今天到期
        var info = ExpiryPolicy.Evaluate(With(Now.Date.AddMinutes(1)), Now);

        Assert.Equal(ExpiryState.ExpiringSoon, info.State);
        Assert.Equal(0, info.DaysLeft);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(14)]
    public void WithinWindow_IsExpiringSoon(int days)
    {
        var info = ExpiryPolicy.Evaluate(With(Now.Date.AddDays(days)), Now);

        Assert.Equal(ExpiryState.ExpiringSoon, info.State);
        Assert.Equal(days, info.DaysLeft);
        Assert.True(info.NeedsAttention);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(365)]
    public void BeyondWindow_IsValid(int days)
    {
        var info = ExpiryPolicy.Evaluate(With(Now.Date.AddDays(days)), Now);

        Assert.Equal(ExpiryState.Valid, info.State);
        Assert.False(info.NeedsAttention);
    }

    [Fact]
    public void WindowBoundary_IsInclusive()
    {
        // 14 天 = 提醒；15 天 = 不提醒。这个边界被显式固定下来。
        Assert.Equal(ExpiryState.ExpiringSoon, ExpiryPolicy.Evaluate(With(Now.Date.AddDays(14)), Now).State);
        Assert.Equal(ExpiryState.Valid, ExpiryPolicy.Evaluate(With(Now.Date.AddDays(15)), Now).State);
    }

    [Fact]
    public void Describe_FormatsLongOverdueCorrectly()
    {
        var info = ExpiryPolicy.Evaluate(With(Now.Date.AddDays(-45)), Now);
        Assert.Equal("已过期 45 天", info.Describe());
    }

    // ---------------- 汇总 ----------------

    [Fact]
    public void FindAlerts_ExcludesNoneAndValid()
    {
        var entries = new List<ApiEntry>
        {
            With(null),                    // None
            With(Now.Date.AddDays(100)),   // Valid
            With(Now.Date.AddDays(3)),     // Soon
            With(Now.Date.AddDays(-2)),    // Expired
        };

        var alerts = ExpiryPolicy.FindAlerts(entries, Now);

        Assert.Equal(2, alerts.Count);
    }

    [Fact]
    public void FindAlerts_PutsExpiredFirstThenByUrgency()
    {
        var entries = new List<ApiEntry>
        {
            new() { Name = "soon10", ExpiresUtc = Now.Date.AddDays(10) },
            new() { Name = "expired1", ExpiresUtc = Now.Date.AddDays(-1) },
            new() { Name = "soon2", ExpiresUtc = Now.Date.AddDays(2) },
            new() { Name = "expired30", ExpiresUtc = Now.Date.AddDays(-30) },
        };

        var names = ExpiryPolicy.FindAlerts(entries, Now).Select(x => x.Entry.Name).ToList();

        // 过期的最先（过期久的在前），然后是即将到期（快到的在前）
        Assert.Equal(new[] { "expired30", "expired1", "soon2", "soon10" }, names);
    }

    [Fact]
    public void FindAlerts_EmptyWhenNothingNeedsAttention()
    {
        var entries = new List<ApiEntry> { With(null), With(Now.Date.AddDays(90)) };

        Assert.Empty(ExpiryPolicy.FindAlerts(entries, Now));
    }

    [Fact]
    public void SoonWindowIsConfigurableConstant()
    {
        Assert.Equal(14, ExpiryPolicy.SoonWindowDays);
    }
}

/// <summary>ApiEntry 新增字段的序列化兼容性。</summary>
public sealed class ExpiryPersistenceTests : IDisposable
{
    private readonly string _dir;
    private const string Password = "expiry-pass-123";

    public ExpiryPersistenceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "akm-expiry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void ExpiryRoundTripsThroughVault()
    {
        string path = Path.Combine(_dir, "v.akv");
        var when = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Local);

        VaultStore.Save(path, new VaultData
        {
            Entries =
            {
                new ApiEntry { Name = "有期限", ApiKey = "k1", ExpiresUtc = when },
                new ApiEntry { Name = "无期限", ApiKey = "k2", ExpiresUtc = null },
            }
        }, Password, 10_000);

        var loaded = VaultStore.Load(path, Password);

        Assert.Equal(when, loaded.Entries[0].ExpiresUtc);
        Assert.Null(loaded.Entries[1].ExpiresUtc);
    }

    [Fact]
    public void CloneCopiesExpiry()
    {
        var when = DateTime.Today.AddDays(5);
        var entry = new ApiEntry { Name = "x", ExpiresUtc = when };

        Assert.Equal(when, entry.Clone().ExpiresUtc);
    }
}
