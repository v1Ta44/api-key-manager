using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ApiKeyManager;
using Xunit;

namespace ApiKeyManager.Tests;

/// <summary>加密库往返 / 防篡改 / 口令校验（原 SelfTest 的 1–6、8、9 项）。</summary>
public sealed class VaultStoreTests : IDisposable
{
    private readonly string _dir;
    private const string Password = "test-password-123";

    public VaultStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "akm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* 忽略 */ }
    }

    private string Path_(string name) => Path.Combine(_dir, name);

    private static VaultData SampleData() => new()
    {
        Entries = new List<ApiEntry>
        {
            new() { Name = "OpenAI", Provider = "OpenAI", ApiKey = "sk-test-0123456789",
                    BaseUrl = "https://api.openai.com/v1", Model = "gpt-4o", Tags = "ai,官方", Notes = "备注 A" },
            new() { Name = "DeepSeek", Provider = "DeepSeek", ApiKey = "sk-deep-9876543210",
                    BaseUrl = "https://api.deepseek.com", Model = "deepseek-chat", Tags = "ai", Notes = "备注 B" },
        }
    };

    [Fact]
    public void DeriveKey_IsStableAndSizedCorrectly()
    {
        byte[] salt = new byte[16];
        byte[] k1 = VaultStore.DeriveKey(Password, salt, 10_000);
        byte[] k2 = VaultStore.DeriveKey(Password, salt, 10_000);
        byte[] k3 = VaultStore.DeriveKey(Password + "x", salt, 10_000);

        Assert.Equal(32, k1.Length);
        Assert.Equal(k1, k2);
        Assert.NotEqual(k1, k3);
    }

    [Fact]
    public void SaveLoad_RoundTripsIncludingChinese()
    {
        string path = Path_("vault.akv");
        VaultStore.Save(path, SampleData(), Password, 2_000);

        var loaded = VaultStore.Load(path, Password);

        Assert.Equal(2, loaded.Entries.Count);
        Assert.Equal("OpenAI", loaded.Entries[0].Name);
        Assert.Equal("sk-test-0123456789", loaded.Entries[0].ApiKey);
        Assert.Equal("https://api.openai.com/v1", loaded.Entries[0].BaseUrl);
        Assert.Equal("备注 B", loaded.Entries[1].Notes);
    }

    [Fact]
    public void Load_RejectsWrongPassword()
    {
        string path = Path_("vault.akv");
        VaultStore.Save(path, SampleData(), Password, 2_000);

        Assert.Throws<VaultException>(() => VaultStore.Load(path, "wrong-password"));
    }

    [Fact]
    public void Parse_DetectsTamperedCiphertext()
    {
        string path = Path_("vault.akv");
        VaultStore.Save(path, SampleData(), Password, 2_000);

        byte[] raw = File.ReadAllBytes(path);
        raw[^1] ^= 0x5A;

        Assert.Throws<VaultException>(() => VaultStore.Parse(raw, Password));
    }

    [Fact]
    public void Parse_DetectsTamperedHeader()
    {
        string path = Path_("vault.akv");
        VaultStore.Save(path, SampleData(), Password, 2_000);

        byte[] raw = File.ReadAllBytes(path);
        raw[15] ^= 0x01; // 盐区字节，属于 AAD

        Assert.Throws<VaultException>(() => VaultStore.Parse(raw, Password));
    }

    [Fact]
    public void Parse_RejectsNonVaultFile()
    {
        byte[] junk = Encoding.UTF8.GetBytes("this is not a vault file at all .........");
        Assert.Throws<VaultException>(() => VaultStore.Parse(junk, Password));
    }

    [Fact]
    public void RoundTripsTwoHundredEntries()
    {
        var big = new VaultData();
        for (int i = 0; i < 200; i++)
            big.Entries.Add(new ApiEntry { Name = "服务-" + i, ApiKey = "key-" + i, Notes = "备注 " + i });

        string path = Path_("big.akv");
        VaultStore.Save(path, big, "another-pass-456", 2_000);

        var loaded = VaultStore.Load(path, "another-pass-456");
        Assert.Equal(200, loaded.Entries.Count);
        Assert.Equal("服务-199", loaded.Entries[199].Name);
    }

    [Fact]
    public void RoundTripsEmptyVault()
    {
        string path = Path_("empty.akv");
        VaultStore.Save(path, new VaultData(), Password, 2_000);
        Assert.Empty(VaultStore.Load(path, Password).Entries);
    }

    [Fact]
    public void BackupBundle_CarriesSettings()
    {
        string path = Path_("backup.akvbak");
        var data = SampleData();
        data.Settings = new AppSettings { AutoLockMinutes = 12, Theme = "Dark", ShowKeys = true };

        VaultStore.Save(path, data, Password, 2_000);
        var loaded = VaultStore.Load(path, Password);

        Assert.NotNull(loaded.Settings);
        Assert.Equal(12, loaded.Settings!.AutoLockMinutes);
        Assert.Equal("Dark", loaded.Settings.Theme);
        Assert.True(loaded.Settings.ShowKeys);
    }

    [Fact]
    public void OldVaultWithoutSettingsField_StillLoads()
    {
        // 模拟旧版本写出的数据（无 Settings 字段），确认向后兼容
        string path = Path_("old.akv");
        VaultStore.Save(path, new VaultData { Entries = SampleData().Entries }, Password, 2_000);

        var loaded = VaultStore.Load(path, Password);
        Assert.Equal(2, loaded.Entries.Count);
        Assert.Null(loaded.Settings);
    }

    [Fact]
    public void Save_LeavesNoTempFiles()
    {
        string path = Path_("clean.akv");
        VaultStore.Save(path, SampleData(), Password, 2_000);

        var leftovers = Directory.GetFiles(_dir, "*.tmp");
        Assert.Empty(leftovers);
    }
}
