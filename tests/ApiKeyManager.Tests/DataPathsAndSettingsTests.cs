using System;
using System.IO;
using ApiKeyManager;
using Xunit;

namespace ApiKeyManager.Tests;

/// <summary>
/// 数据目录解析（覆盖 P0-1：演示目录覆盖必须可还原，
/// 否则 --demo 会把演示数据写进用户真实数据目录）。
/// </summary>
public sealed class DataPathsTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "akm-paths-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolve_WithoutOverride_DoesNotUseDemoDirectory()
    {
        var (dir, vault) = DataPaths.Resolve();

        Assert.False(DataPaths.IsOverridden);
        Assert.DoesNotContain("akm-demo", dir, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("vault.akv", vault);
    }

    [Fact]
    public void UseDirectory_RedirectsBothDirAndVaultPath()
    {
        string target = TempDir();
        try
        {
            using (DataPaths.UseDirectory(target))
            {
                var (dir, vault) = DataPaths.Resolve();

                Assert.True(DataPaths.IsOverridden);
                Assert.Equal(target, dir);
                Assert.Equal(Path.Combine(target, "vault.akv"), vault);
            }
        }
        finally
        {
            try { Directory.Delete(target, true); } catch { }
        }
    }

    [Fact]
    public void UseDirectory_RevertsWhenScopeIsDisposed()
    {
        string target = TempDir();
        string? before;
        try
        {
            (before, _) = DataPaths.Resolve();

            using (DataPaths.UseDirectory(target))
            {
                Assert.Equal(target, DataPaths.Resolve().DataDir);
            }

            // 作用域释放后必须回到自动解析，而不是继续指向演示目录
            Assert.False(DataPaths.IsOverridden);
            Assert.Equal(before, DataPaths.Resolve().DataDir);
        }
        finally
        {
            try { Directory.Delete(target, true); } catch { }
        }
    }

    [Fact]
    public void UseDirectory_RevertsEvenWhenDisposedTwice()
    {
        string target = TempDir();
        try
        {
            var scope = DataPaths.UseDirectory(target);
            scope.Dispose();
            scope.Dispose(); // 幂等，不应抛异常或破坏状态

            Assert.False(DataPaths.IsOverridden);
        }
        finally
        {
            try { Directory.Delete(target, true); } catch { }
        }
    }
}

/// <summary>设置读写：损坏 / 缺失时必须回落到默认值而不是崩溃。</summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir;

    public SettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "akm-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Load_ReturnsDefaultsWhenFileMissing()
    {
        var s = SettingsStore.Load(_dir);

        Assert.Equal(5, s.AutoLockMinutes);
        Assert.Equal(30, s.ClipboardClearSeconds);
        Assert.False(s.ShowKeys);
        Assert.Equal("Light", s.Theme);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        SettingsStore.Save(_dir, new AppSettings
        {
            AutoLockMinutes = 15, ClipboardClearSeconds = 0, ShowKeys = true, Theme = "Dark"
        });

        var s = SettingsStore.Load(_dir);

        Assert.Equal(15, s.AutoLockMinutes);
        Assert.Equal(0, s.ClipboardClearSeconds);
        Assert.True(s.ShowKeys);
        Assert.Equal("Dark", s.Theme);
    }

    [Fact]
    public void Load_ReturnsDefaultsWhenFileCorrupt()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ this is not json");

        var s = SettingsStore.Load(_dir); // 不应抛出

        Assert.Equal(5, s.AutoLockMinutes);
        Assert.Equal("Light", s.Theme);
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        string nested = Path.Combine(_dir, "a", "b");
        SettingsStore.Save(nested, new AppSettings { Theme = "Dark" });

        Assert.True(File.Exists(Path.Combine(nested, "settings.json")));
    }
}
