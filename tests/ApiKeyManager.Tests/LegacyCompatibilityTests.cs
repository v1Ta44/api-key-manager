using System.Text.Json;
using Xunit;

namespace ApiKeyManager.Tests;

/// <summary>固定字节样本由独立实现构造，避免保存器和读取器一起变化时自证兼容。</summary>
public sealed class LegacyCompatibilityTests
{
    private static JsonDocument ReadFixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null)
        {
            var path = Path.Combine(root.FullName, "tests", "ApiKeyManager.Tests", "Fixtures", "legacy-v1.json");
            if (File.Exists(path)) return JsonDocument.Parse(File.ReadAllText(path));
            root = root.Parent;
        }
        throw new FileNotFoundException("Missing frozen legacy compatibility fixture.");
    }

    [Theory]
    [InlineData("vaultBase64", false)]
    [InlineData("backupBase64", true)]
    public void FrozenV1Bytes_PreserveFieldsThroughCurrentWriter(string source, bool hasSettings)
    {
        using var fixture = ReadFixture();
        var root = fixture.RootElement;
        var password = root.GetProperty("password").GetString()!;
        var bytes = Convert.FromBase64String(root.GetProperty(source).GetString()!);
        var data = VaultStore.Parse(bytes, password);
        Assert.Equal(2, data.Entries.Count);
        var a = data.Entries[0];
        var b = data.Entries[1];
        Assert.Equal("legacy-openai", a.Id);
        Assert.Equal("生产 / 中文", a.Name);
        Assert.Equal("OpenAI", a.Provider);
        Assert.Equal("fixture-not-a-real-key-1234", a.ApiKey);
        Assert.Equal("https://example.invalid/v1", a.BaseUrl);
        Assert.Equal("example-model", a.Model);
        Assert.Equal("生产,中文；测试", a.Tags);
        Assert.Equal("第一行\n第二行", a.Notes);
        Assert.Equal(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc), a.CreatedUtc);
        Assert.Equal(new DateTime(2025, 6, 7, 8, 9, 10, DateTimeKind.Utc), a.UpdatedUtc);
        Assert.Null(a.ExpiresUtc);
        Assert.Equal(a.Name, b.Name);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Empty(b.ApiKey);
        Assert.Equal(new DateTime(2026, 12, 31), b.ExpiresUtc);
        Assert.Equal(DateTimeKind.Unspecified, b.ExpiresUtc!.Value.Kind);
        Assert.Equal(hasSettings, data.Settings != null);
        if (hasSettings)
        {
            Assert.Equal("Dark", data.Settings!.Theme);
            Assert.Equal(15, data.Settings.AutoLockMinutes);
            Assert.Equal(45, data.Settings.ClipboardClearSeconds);
            Assert.True(data.Settings.ShowKeys); // 可读兼容，不表示新版允许恢复此状态。
        }
        var dir = Path.Combine(Path.GetTempPath(), "akm-compat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "roundtrip.akv");
            VaultStore.Save(path, data, password);
            Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(VaultStore.Load(path, password)));
        }
        finally { Directory.Delete(dir, true); }
        var before = bytes.ToArray();
        Assert.Throws<VaultException>(() => VaultStore.Parse(bytes, "wrong-password"));
        Assert.Equal(before, bytes);
    }
}
