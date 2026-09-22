using ApiKeyManager.Application;
using Xunit;

namespace ApiKeyManager.Tests;

public sealed class VaultSessionTests
{
    private sealed class Storage : IVaultStorage
    {
        public VaultData Data = new() { Entries = [new() { Id = "one", Name = "原记录", ApiKey = "synthetic-key" }] };
        public string Password = "old-password";
        public bool FailSave, FailBackup;
        public int Writes;
        public ManualResetEventSlim? Entered, Continue;
        public bool Exists => true;
        public VaultData Load(string password) => password == Password ? Copy(Data) : throw new VaultException("wrong");
        public void Save(VaultData data, string password)
        {
            Entered?.Set();
            if (Continue != null && !Continue.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            if (FailSave) throw new IOException("simulated write failure");
            Data = Copy(data); Password = password; Writes++;
        }
        public VaultMetadata? Inspect() => new(1, IterationPolicy.Current);
        public string Backup() => FailBackup ? throw new IOException("simulated backup failure") : "test-backup";
        private static VaultData Copy(VaultData data) => new() { Entries = data.Entries.Select(e => e.Clone()).ToList() };
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("add")]
    [InlineData("delete")]
    public async Task FailedWrites_DoNotPublishCandidates(string action)
    {
        var store = new Storage();
        var session = new VaultSession(store);
        Assert.True((await session.UnlockAsync("old-password")).Succeeded);
        store.FailSave = true;
        var draft = session.GetDraft("one")!;
        draft.Name = "不应保存";
        if (action == "add") draft.Id = "two";
        var result = action == "delete" ? await session.DeleteAsync("one", session.Generation) : await session.SaveEntryAsync(draft, session.Generation);
        Assert.Equal(ResultCode.StorageFailure, result.Code);
        Assert.Single(session.GetEntries());
        Assert.Equal("原记录", session.GetEntries()[0].Name);
        Assert.Equal("原记录", store.Data.Entries[0].Name);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task PasswordFailure_KeepsOldCredentialForNextSave()
    {
        var store = new Storage(); var session = new VaultSession(store);
        await session.UnlockAsync("old-password");
        store.FailSave = true;
        Assert.False((await session.ChangePasswordAsync("old-password", "new-password", session.Generation)).Succeeded);
        store.FailSave = false;
        Assert.True((await session.SaveEntryAsync(session.GetDraft("one")!, session.Generation)).Succeeded);
        Assert.Equal("old-password", store.Password);
    }

    [Fact]
    public async Task LockDuringWrite_DoesNotRestoreDataOrAcceptStaleDraft()
    {
        using var entered = new ManualResetEventSlim(); using var resume = new ManualResetEventSlim();
        var store = new Storage { Entered = entered, Continue = resume };
        var session = new VaultSession(store);
        await session.UnlockAsync("old-password");
        var version = session.Generation;
        var draft = session.GetDraft("one")!; draft.Name = "已开始的写入";
        var saving = session.SaveEntryAsync(draft, version);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        session.Lock(); resume.Set();
        Assert.Equal(ResultCode.SessionChanged, (await saving).Code);
        Assert.False(session.IsUnlocked);
        Assert.Empty(session.GetEntries());
        Assert.Null(session.GetSecret("one"));
        await session.UnlockAsync("old-password");
        Assert.Equal(ResultCode.SessionChanged, (await session.SaveEntryAsync(draft, version)).Code);
    }

    [Fact]
    public async Task DraftIsDetachedAndSummariesNeverContainSecret()
    {
        var session = new VaultSession(new Storage());
        await session.UnlockAsync("old-password");
        var draft = session.GetDraft("one")!; draft.ApiKey = "changed";
        Assert.Equal("synthetic-key", session.GetSecret("one"));
        Assert.DoesNotContain("synthetic-key", System.Text.Json.JsonSerializer.Serialize(session.GetEntries()));
    }

    [Fact]
    public async Task ReplaceImport_RequiresBackupAndPreservesCurrentDataOnFailure()
    {
        var dir = Path.Combine(Path.GetTempPath(), "akm-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "backup.akvbak");
            VaultStore.Save(path, new() { Entries = [new() { Id = "incoming", Name = "外来记录" }] }, "backup-password", 2000);
            var store = new Storage { FailBackup = true }; var session = new VaultSession(store);
            await session.UnlockAsync("old-password");
            var preview = await session.PrepareImportAsync(path, "backup-password");
            Assert.True(preview.Result.Succeeded); Assert.Equal(1, preview.Preview!.Added);
            Assert.Equal(ResultCode.BackupFailure, (await session.CommitImportAsync(true, false)).Result.Code);
            Assert.Equal("one", session.GetEntries().Single().Id); Assert.Equal(0, store.Writes);
            store.FailBackup = false;
            Assert.True((await session.CommitImportAsync(true, false)).Result.Succeeded);
            Assert.Equal("incoming", session.GetEntries().Single().Id);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Lease_ExcludesSamePathAndReleasesWithoutDeletingLockFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "akm-lease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "vault.akv");
            using (var first = new VaultLease(path))
            {
                Assert.Throws<IOException>(() => new VaultLease(path));
                using var other = new VaultLease(Path.Combine(dir, "other.akv"));
            }
            using var reopened = new VaultLease(path);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task ExportCannotOverwriteVaultLockOrSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "akm-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "vault.akv");
            var session = new VaultSession(new FileVaultStorage(path));
            Assert.True((await session.UnlockAsync("test-password", true)).Succeeded);
            var original = File.ReadAllBytes(path);
            foreach (var target in new[] { path, path + ".lock", Path.Combine(dir, "settings.json") })
                Assert.Equal(ResultCode.Validation, (await session.ExportAsync(target, "password", new(), true)).Code);
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task InvalidImportClearsPreviouslyPreparedCandidate()
    {
        var dir = Path.Combine(Path.GetTempPath(), "akm-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "backup.akvbak");
            VaultStore.Save(path, new(), "test", 2000);
            var session = new VaultSession(new Storage()); await session.UnlockAsync("old-password");
            Assert.True((await session.PrepareImportAsync(path, "test")).Result.Succeeded);
            Assert.False((await session.PrepareImportAsync(path, "wrong")).Result.Succeeded);
            Assert.Equal(ResultCode.Validation, (await session.CommitImportAsync(true, false)).Result.Code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task InvalidStructureCannotUnlock()
    {
        var store = new Storage(); store.Data.Entries.Add(store.Data.Entries[0].Clone());
        var session = new VaultSession(store);
        Assert.Equal(ResultCode.InvalidVault, (await session.UnlockAsync("old-password")).Code);
        Assert.Empty(session.GetEntries());
    }

}
