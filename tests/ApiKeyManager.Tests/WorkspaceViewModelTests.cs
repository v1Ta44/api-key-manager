using ApiKeyManager.Application;
using ApiKeyManager.Wpf.ViewModels;
using Xunit;

namespace ApiKeyManager.Tests;

public sealed class WorkspaceViewModelTests
{
    private sealed class FailingStorage : IVaultStorage
    {
        private readonly MemoryVaultStorage _inner = new(new VaultData { Entries = [new() { Id = "entry", Name = "Saved", ApiKey = "test-secret" }] });
        public bool Fail;
        public bool Exists => true;
        public VaultData Load(string password) => _inner.Load(password);
        public VaultMetadata? Inspect() => _inner.Inspect();
        public string Backup() => _inner.Backup();
        public void Save(VaultData data, string password) { if (Fail) throw new IOException(); _inner.Save(data, password); }
    }
    [Fact]
    public async Task FailedSaveKeepsEditorAndAllowsRetryWithoutPublishing()
    {
        var storage = new FailingStorage();
        var model = new WorkspaceViewModel(new VaultSession(storage), new(), _ => true, (_, _) => true);
        await model.Unlock("demo", ""); model.BeginEdit(false);
        var input = model.Draft!.Clone(); input.Name = "Changed";
        storage.Fail = true; await model.SaveDraft(input, "");
        Assert.Equal("Edit", model.Pane); Assert.NotNull(model.Draft);
        Assert.Equal("Saved", model.Selected!.Name); Assert.False(model.Busy); Assert.NotEmpty(model.Error);
        storage.Fail = false; await model.SaveDraft(input, "");
        Assert.Equal("", model.Pane); Assert.Null(model.Draft); Assert.Equal("Changed", model.Selected!.Name);
    }
    [Fact]
    public void FailedSettingsSaveKeepsOriginalThemeAndTimer()
    {
        var model = new WorkspaceViewModel(new VaultSession(new FailingStorage()), new(), _ => false, (_, _) => true);
        model.ToggleTheme(); Assert.Equal("Light", model.Settings.Theme); Assert.NotEmpty(model.Error);
        model.SavePreferences("1", "5"); Assert.Equal(5, model.Settings.AutoLockMinutes); Assert.Equal(30, model.Settings.ClipboardClearSeconds);
    }
    [Fact]
    public async Task LockClearsImportDraftRevealAndSelection()
    {
        var model = new WorkspaceViewModel(new VaultSession(new FailingStorage()), new(), _ => true, (_, _) => true);
        await model.Unlock("demo", ""); model.Reveal(); Assert.NotNull(model.RevealedSecret);
        model.BeginEdit(false); model.Lock();
        Assert.True(model.IsLocked); Assert.Null(model.Draft); Assert.Null(model.RevealedSecret); Assert.Null(model.Selected); Assert.Empty(model.Entries);
        model.BeginEdit(true); Assert.Null(model.Draft);
    }

    [Fact]
    public async Task ImportReportsPartialSuccessWhenPreferencesCannotBeSaved()
    {
        var path = Path.Combine(Path.GetTempPath(), "akm-preferences-" + Guid.NewGuid().ToString("N") + ".akvbak");
        try
        {
            VaultStore.Save(path, new() { Entries = [new() { Id = "new", Name = "Imported" }], Settings = new() { Theme = "Dark" } }, "test", 2000);
            var model = new WorkspaceViewModel(new VaultSession(new FailingStorage()), new(), _ => false, (_, _) => true);
            await model.Unlock("demo", ""); await model.PrepareImport(path, "test"); await model.CommitImport(false, true);
            Assert.Equal(2, model.Entries.Count); Assert.Equal("Light", model.Settings.Theme);
            Assert.Contains("记录已导入", model.Feedback); Assert.Contains("偏好保存失败", model.Feedback);
            Assert.Empty(model.Error); Assert.Equal("", model.Pane);
        }
        finally { File.Delete(path); }
    }
}
