using ApiKeyManager.Application;
using Xunit;

namespace ApiKeyManager.Tests;

/// <summary>真实文件句柄制造的失败，只访问随机创建的合成目录。</summary>
public sealed class ReleaseStorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "akm-release-" + Guid.NewGuid().ToString("N"), "中文 space");
    private const string Password = "release-fixture-password";
    private string VaultPath => Path.Combine(_directory, "vault.akv");
    public ReleaseStorageTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_directory)!, true);

    private async Task<VaultSession> Create()
    {
        var session = new VaultSession(new FileVaultStorage(VaultPath));
        Assert.True((await session.UnlockAsync(Password, true)).Succeeded);
        Assert.True((await session.SaveEntryAsync(new() { Id = "original", Name = "原记录", ApiKey = "synthetic-release-only" }, session.Generation)).Succeeded);
        return session;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockedDestinationPreservesOriginalAndAllowsRetry(bool changePassword)
    {
        var session = await Create();
        var original = File.ReadAllBytes(VaultPath);
        var draft = session.GetDraft("original")!; draft.Name = "修改后";
        Task<OperationResult> Save() => changePassword
            ? session.ChangePasswordAsync(Password, "next-fixture-password", session.Generation)
            : session.SaveEntryAsync(draft, session.Generation);
        using (new FileStream(VaultPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(ResultCode.StorageFailure, (await Save()).Code);
            Assert.Equal(original, File.ReadAllBytes(VaultPath));
            Assert.Equal("原记录", session.GetEntries().Single().Name);
            Assert.Empty(Directory.GetFiles(_directory, "*.candidate-*"));
        }
        Assert.True((await Save()).Succeeded);
        session.Lock();
        Assert.True((await session.UnlockAsync(changePassword ? "next-fixture-password" : Password)).Succeeded);
        Assert.Equal(changePassword ? "原记录" : "修改后", session.GetEntries().Single().Name);
    }

    [Fact]
    public async Task BackupFailureStopsReplacementAndRecoveryCopyRemainsUsable()
    {
        var session = await Create();
        var original = File.ReadAllBytes(VaultPath);
        var incoming = Path.Combine(_directory, "incoming.akvbak");
        VaultStore.Save(incoming, new() { Entries = [new() { Id = "incoming", Name = "导入记录" }] }, Password);
        Assert.True((await session.PrepareImportAsync(incoming, Password)).Result.Succeeded);
        using (new FileStream(VaultPath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(ResultCode.BackupFailure, (await session.CommitImportAsync(true, false)).Result.Code);
        Assert.Equal(original, File.ReadAllBytes(VaultPath));
        Assert.Equal("original", session.GetEntries().Single().Id);
        Assert.True((await session.CommitImportAsync(true, false)).Result.Succeeded);
        var readableBackups = Directory.GetFiles(_directory, "*.bak").Where(p => File.ReadAllBytes(p).SequenceEqual(original)).ToArray();
        Assert.NotEmpty(readableBackups);
        Assert.All(readableBackups, p => Assert.Equal("original", VaultStore.Load(p, Password).Entries.Single().Id));
        Assert.Equal("incoming", VaultStore.Load(VaultPath, Password).Entries.Single().Id);
    }

    [Fact]
    public async Task CorruptAndWrongPasswordNeverRewriteVault()
    {
        var session = await Create(); session.Lock();
        var original = File.ReadAllBytes(VaultPath);
        Assert.Equal(ResultCode.InvalidVault, (await session.UnlockAsync("wrong")).Code);
        Assert.Equal(original, File.ReadAllBytes(VaultPath));
        original[^1] ^= 1; File.WriteAllBytes(VaultPath, original);
        Assert.Equal(ResultCode.InvalidVault, (await session.UnlockAsync(Password)).Code);
        Assert.Equal(original, File.ReadAllBytes(VaultPath));
        Assert.False(session.IsUnlocked); Assert.Empty(session.GetEntries());
    }

    [Fact]
    public async Task ExportAndSettingsFailuresPreserveExistingFiles()
    {
        var session = await Create();
        var output = Path.Combine(_directory, "existing.csv");
        File.WriteAllText(output, "existing-export");
        using (new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Equal(ResultCode.StorageFailure, (await session.ExportAsync(output, null, new(), true)).Code);
        Assert.Equal("existing-export", File.ReadAllText(output));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.True(SettingsStore.TrySave(_directory, new() { Theme = "Dark" }));
        var settings = Path.Combine(_directory, "settings.json");
        using (new FileStream(settings, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.False(SettingsStore.TrySave(_directory, new() { Theme = "Light" }));
        Assert.Equal("Dark", SettingsStore.Load(_directory).Theme);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }
}
