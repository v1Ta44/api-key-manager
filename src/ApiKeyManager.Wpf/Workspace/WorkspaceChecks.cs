using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ApiKeyManager.Application;
using ApiKeyManager.Wpf.ViewModels;

namespace ApiKeyManager.Wpf.Workspace;

/// <summary>生产窗口、生产应用层及临时文件的组合验收。不触碰用户库或系统剪贴板。</summary>
internal static class WorkspaceChecks
{
    internal static async Task<int> Run(string directory)
    {
        var output = Path.GetFullPath(directory);
        Directory.CreateDirectory(output);
        var work = Path.Combine(output, "test-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var checks = new List<string>();
        var errors = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        WorkspaceWindow? window = null;
        var sw = Stopwatch.StartNew();
        try
        {
            var vault = Path.Combine(work, "vault.akv");
            var session = new VaultSession(new FileVaultStorage(vault));
            var copied = new List<string>();
            var model = new WorkspaceViewModel(session, new AppSettings(), s => SettingsStore.TrySave(work, s), (text, _) => { copied.Add(text); return true; });
            window = new WorkspaceWindow(model, vault) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
            void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            void ProtectedInput(PasswordBox input)
            {
                var peer = new PasswordBoxAutomationPeer(input);
                var readable = false;
                try { readable = peer.GetPattern(PatternInterface.Value) is IValueProvider value && !string.IsNullOrEmpty(value.Value); }
                catch (InvalidOperationException) { /* WPF deliberately refuses reading a password. */ }
                Check(peer.IsPassword() && !string.IsNullOrWhiteSpace(peer.GetName()) && !readable,
                    input.Name + " automation peer names and protects password");
            }
            async Task Settle()
            {
                for (var i = 0; i < 1000 && model.Busy; i++) await Task.Delay(20);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                Check(!model.Busy, "async operation completed");
            }
            void Layout(double width = 1180, double height = 760)
            {
                window.Width = width; window.Height = height;
                window.Root.Width = double.NaN; window.Root.Height = double.NaN;
                window.UpdateLayout(); window.ApplyResponsiveLayout(); window.UpdateLayout();
            }
            void Inside(FrameworkElement control, string label)
            {
                var rect = control.TransformToAncestor(window.Root).TransformBounds(new Rect(control.RenderSize));
                Check(control.IsVisible && rect.Width > 0 && rect.Height > 0 && rect.Left >= -1 && rect.Top >= -1 && rect.Right <= window.Root.ActualWidth + 1 && rect.Bottom <= window.Root.ActualHeight + 1, label + " within viewport");
            }
            void Capture(string name, double width = 1180, double height = 760)
            {
                Layout(width, height);
                var bitmap = new RenderTargetBitmap((int)window.Root.ActualWidth, (int)window.Root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window.Root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
            }
            Check(model.IsLocked && model.IsNew, "first use shows create screen");
            Capture("01-create"); Inside(window.UnlockButton, "create action");
            await WindowChromeChecks.Run(window, Check);
            foreach (var button in new[] { window.MinimizeWindowButton, window.MaximizeWindowButton, window.CloseWindowButton })
            {
                var peer = new ButtonAutomationPeer(button);
                Check(!string.IsNullOrWhiteSpace(peer.GetName()) && peer.GetPattern(PatternInterface.Invoke) != null,
                    button.Name + " exposes named automation invoke");
            }
            window.UnlockPassword.Password = "workspace-test-password"; window.ConfirmPassword.Password = "different";
            ProtectedInput(window.UnlockPassword); ProtectedInput(window.ConfirmPassword);
            Click(window.UnlockButton); await Settle(); Check(model.IsLocked && !File.Exists(vault), "confirmation mismatch does not create vault");
            window.UnlockPassword.Password = "workspace-test-password"; window.ConfirmPassword.Password = "workspace-test-password";
            Click(window.UnlockButton); await Settle(); Check(!model.IsLocked && File.Exists(vault), "create persisted and unlocked");
            Click(window.AddButton);
            window.EditName.Text = "研究 · Alpha"; window.EditProvider.Text = "示例平台"; window.EditKey.Password = "synthetic-key-alpha-1234";
            window.EditUrl.Text = "https://example.invalid/v1/very-long-endpoint"; window.EditModel.Text = "example-model";
            window.EditTags.Text = "研究,测试"; window.EditNotes.Text = "第一行\n第二行"; window.EditExpiry.Text = DateTime.Today.AddDays(7).ToString("yyyy-MM-dd");
            ProtectedInput(window.EditKey);
            Capture("02-editor"); Inside(window.SaveEditButton, "editor save action");
            Click(window.SaveEditButton); await Settle();
            Check(model.Entries.Count == 1 && VaultStore.Load(vault, "workspace-test-password").Entries[0].Name == "研究 · Alpha", "routed save writes encrypted record");
            Check(!window.DetailKey.Text.Contains("synthetic-key"), "list and detail default masked");
            Click(window.CopyButton); Check(copied.Last() == "synthetic-key-alpha-1234", "copy adapter receives full key");
            Click(window.RevealButton); Check(window.DetailKey.Text == "synthetic-key-alpha-1234", "explicit reveal only selected key");
            await Task.Delay(TimeSpan.FromSeconds(11));
            Check(model.RevealedSecret == null && !window.DetailKey.Text.Contains("synthetic-key"), "dispatcher timer hides revealed key automatically");
            model.Copy(model.Selected, true); Check(copied.Last().EndsWith("very-long-endpoint"), "URL copy keeps complete value");
            model.HideSecret();
            model.BeginEdit(false); window.EditName.Text = "取消的修改"; Click(window.CancelEditButton);
            Check(model.Selected?.Name == "研究 · Alpha", "cancel edit keeps saved record");
            model.BeginEdit(true); window.EditName.Text = "本地服务"; window.EditUrl.Text = "http://localhost:11434/v1"; window.EditExpiry.Text = "bad-date";
            Click(window.SaveEditButton); await Settle(); Check(model.Pane == "Edit" && model.Error.Contains("yyyy-MM-dd"), "invalid expiry retains draft");
            window.EditExpiry.Text = DateTime.Today.AddDays(-3).ToString("yyyy-MM-dd"); Click(window.SaveEditButton); await Settle();
            Check(model.Entries.Count == 2, "second entry created");
            window.SearchBox.Text = "example.invalid"; Check(model.Entries.Count == 1, "URL is searchable");
            window.SearchBox.Text = "synthetic-key-alpha"; Check(model.Entries.Count == 0, "secret is not searchable");
            Capture("03-empty-search"); window.SearchBox.Clear();
            window.FilterSoon.IsChecked = true; Check(model.Entries.Count == 1 && model.Selected?.Name == "研究 · Alpha", "soon filter");
            window.FilterExpired.IsChecked = true; Check(model.Entries.Count == 1 && model.Selected?.Name == "本地服务", "expired filter");
            window.FilterAll.IsChecked = true; Capture("04-workspace-light");
            window.PlatformFilter.SelectedItem = "示例平台"; Check(model.Entries.Count == 1, "platform dropdown filters summaries");
            window.PlatformFilter.SelectedItem = "全部平台"; window.TagFilter.SelectedItem = "研究"; Check(model.Entries.Count == 1, "tag dropdown filters summaries");
            model.ResetFilters();
            Capture("11-wide", 1600, 760);
            Click(window.ThemeButton); Capture("05-workspace-dark"); Check(SettingsStore.Load(work).Theme == "Dark", "theme persisted");
            Click(window.ThemeButton);
            model.OpenPane("Settings"); Capture("06-settings"); Inside(window.SubmitManagement, "settings action");
            model.SavePreferences("3", "12"); Check(SettingsStore.Load(work).ClipboardClearSeconds == 12, "timers persisted");
            var backup = Path.Combine(work, "export.akvbak");
            model.OpenPane("Backup"); await model.Export(backup, "backup-only", "backup-only", false);
            Check(VaultStore.Load(backup, "backup-only").Entries.Count == 2, "independent backup password roundtrip");
            model.OpenPane("Csv"); await model.Export(Path.Combine(work, "export.csv"), "", "", true);
            Check(File.ReadAllText(Path.Combine(work, "export.csv")).Contains("synthetic-key-alpha-1234"), "CSV includes preserved secret columns");
            model.OpenPane("Password");
            window.CurrentPassword.Password = "synthetic-current"; window.OperationPassword.Password = "synthetic-next"; window.OperationConfirmation.Password = "synthetic-next";
            ProtectedInput(window.CurrentPassword); ProtectedInput(window.OperationPassword); ProtectedInput(window.OperationConfirmation);
            await model.ChangePassword("wrong", "new-password-123", "new-password-123");
            Check(model.Pane == "Password" && model.Error.Length > 0, "wrong current password rejected");
            await model.ChangePassword("workspace-test-password", "new-password-123", "new-password-123");
            Check(VaultStore.Load(vault, "new-password-123").Entries.Count == 2, "password change verified on disk");
            model.OpenPane("Delete"); await model.Delete(); Check(model.Entries.Count == 1, "delete committed");
            model.OpenPane("Import"); await model.PrepareImport(backup, "wrong"); Check(model.Pane == "Import", "wrong backup password retains import form");
            await model.PrepareImport(backup, "backup-only"); Check(model.ImportPreview?.Added == 1 && model.ImportPreview.Duplicate == 1, "import preview Id counts");
            Capture("07-import-preview"); Inside(window.SubmitManagement, "import confirmation");
            await model.CommitImport(false, true); Check(model.Entries.Count == 2 && model.Settings.ClipboardClearSeconds == 12, "merge and restore preferences");
            model.OpenPane("Import"); await model.PrepareImport(backup, "backup-only"); await model.CommitImport(true, false);
            Check(Directory.GetFiles(work, "vault.akv.*.bak").Length > 0, "replacement created recoverable backup");
            Capture("08-narrow", 900, 600); Inside(window.AddButton, "narrow add action");
            Inside(window.MinimizeWindowButton, "narrow minimize action"); Inside(window.MaximizeWindowButton, "narrow maximize action"); Inside(window.CloseWindowButton, "narrow close action");
            Check(window.CaptionDragArea.ActualWidth >= 24, "narrow header keeps a dedicated drag area");
            model.BeginEdit(false); Capture("09-narrow-editor", 900, 600); Inside(window.SaveEditButton, "narrow editor action");
            Click(window.LockButton); Check(model.IsLocked && model.Draft == null && window.EditKey.Password.Length == 0 && model.Entries.Count == 0, "lock destroys draft and visible secrets");
            Capture("10-locked", 900, 600); Inside(window.UnlockButton, "narrow unlock action");
            await model.Unlock("new-password-123", ""); Check(model.Entries.Count == 2, "reopen after password change");
            var fakeNow = DateTime.UtcNow;
            using (var idle = new Services.AutoLockWatcher(() => 1, model.Lock, now: () => fakeNow))
            {
                idle.Start(); model.BeginEdit(false);
                fakeNow = fakeNow.AddSeconds(59); idle.Pulse(); Check(!model.IsLocked, "idle threshold not reached");
                fakeNow = fakeNow.AddSeconds(2); idle.Pulse(); Check(model.IsLocked && model.Draft == null, "idle timeout locks and clears draft (injected clock)");
            }
            // 生产列表的 10k 摘要压力检查；仅内存，不执行 10k 次文件写入。
            var large = new VaultData { Entries = Enumerable.Range(0, 10000).Select(i => new ApiEntry { Name = $"条目 {i:D5}", Provider = "合成平台", ApiKey = "synthetic-secret" }).ToList() };
            var largeModel = new WorkspaceViewModel(new VaultSession(new MemoryVaultStorage(large)), new AppSettings(), _ => true, (_, _) => true);
            var started = Stopwatch.StartNew(); await largeModel.Unlock("demo", ""); largeModel.Search("09999", "All");
            Check(largeModel.Entries.Count == 1, "10k summaries searchable");
            checks.Add($"10k load+search elapsed {started.ElapsedMilliseconds} ms (local observation, not SLA)");
            using (var performance = new PerformanceWindow(largeModel))
            {
                performance.Window.Show();
                foreach (var count in new[] { 100, 1000 })
                {
                    var sample = new VaultData { Entries = large.Entries.Take(count).Select(e => e.Clone()).ToList() };
                    sample.Entries[0].Name = new string('长', 300); sample.Entries[0].BaseUrl = "https://example.invalid/" + new string('a', 2000);
                    var sampleModel = new WorkspaceViewModel(new VaultSession(new MemoryVaultStorage(sample)), new(), _ => true, (_, _) => true);
                    await sampleModel.Unlock("demo", "");
                    performance.Window.Close();
                    performance.Window = new WorkspaceWindow(sampleModel, "in-memory performance fixture")
                    { Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
                    performance.Window.Show();
                    await performance.Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    performance.Window.UpdateLayout();
                    var realized = Enumerable.Range(0, count).Count(i => performance.Window.EntryList.ItemContainerGenerator.ContainerFromIndex(i) != null);
                    Check(realized > 0 && realized < count, $"{count} entries use virtualized row containers ({realized} realized)");
                    var timings = new List<double>();
                    for (var i = 0; i < 30; i++)
                    {
                        var watch = Stopwatch.StartNew();
                        performance.Window.SearchBox.Text = i % 2 == 0 ? "00099" : "";
                        await performance.Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                        performance.Window.UpdateLayout(); watch.Stop(); timings.Add(watch.Elapsed.TotalMilliseconds);
                    }
                    var p95 = timings.Order().ElementAt(28);
                    checks.Add($"{count} entries input-to-layout P95 {p95:F1} ms; {Environment.OSVersion}; {Environment.ProcessorCount} logical CPUs; .NET {Environment.Version}");
                    Check(p95 < 200, $"{count} entry search local P95 under 200 ms");
                }
            }
            Check(errors.Messages.Count == 0, "no WPF binding warnings");
            File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, elapsedMs = sw.ElapsedMilliseconds,
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                windows = Environment.OSVersion.VersionString, dpiScale = VisualTreeHelper.GetDpi(window).DpiScaleX,
                boundaries = "Actual WPF routed events + isolated disk + automation peers. Clipboard adapter is injected; no OS input, DPI switching, screen reader or human acceptance." }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, failure = ex.ToString(), bindingErrors = errors.Messages }, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
        finally { window?.Close(); PresentationTraceSources.DataBindingSource.Listeners.Remove(errors); }
    }
    private sealed class BindingErrors : TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
    private sealed class PerformanceWindow : IDisposable
    {
        public WorkspaceWindow Window;
        public PerformanceWindow(WorkspaceViewModel model) => Window = new WorkspaceWindow(model, "in-memory performance fixture")
        { Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        public void Dispose() => Window.Close();
    }
}
