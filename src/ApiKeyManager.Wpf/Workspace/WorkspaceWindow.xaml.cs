using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ApiKeyManager.Wpf.Services;
using ApiKeyManager.Wpf.Themes;
using ApiKeyManager.Wpf.ViewModels;
using Microsoft.Win32;

namespace ApiKeyManager.Wpf.Workspace;

/// <summary>仅负责控件、焦点、尺寸与 Windows 文件选择；业务状态由 ViewModel 管理。</summary>
public partial class WorkspaceWindow : Window
{
    internal WorkspaceViewModel Model { get; }
    private readonly ClipboardService? _clipboard;
    private readonly AutoLockWatcher _idle;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _ready, _rendering, _detailRequested;
    private string _lastPane = "";
    private bool? _dark;
    private readonly bool _demo;
    private Button? _copySource;
    private object? _copyOriginal;
    private DateTime _copyUntil;
    private DateTime _today = DateTime.Today;
    public WorkspaceWindow(WorkspaceViewModel model, string path, ClipboardService? clipboard = null, bool demo = false)
    {
        Model = model; _clipboard = clipboard; _demo = demo;
        InitializeComponent();
        InitializeWindowChrome();
        StoragePath.Text = path;
        StorageLabel.Text = demo ? "演示空间 · 合成数据" : "本地加密保存";
        DemoHint.Text = demo ? "演示密码：demo · 请勿录入真实密钥" : "主密码无法找回，请妥善保管。";
        _idle = new AutoLockWatcher(() => Model.Settings.AutoLockMinutes, Model.Lock, left =>
            ActivityLabel.Text = left.TotalSeconds <= 0 ? "自动锁定已关闭" : left.TotalSeconds <= 30 ? $"{Math.Ceiling(left.TotalSeconds)} 秒后锁定，未保存草稿将清除" : $"闲置 {Model.Settings.AutoLockMinutes} 分钟锁定");
        _idle.Start(); _idle.Pause();
        Model.SessionLocked += OnSessionLocked;
        Model.SessionUnlocked += OnSessionUnlocked;
        Model.Changed += Render;
        if (_clipboard != null) _clipboard.CleanupFailed += OnClipboardFailure;
        _timer.Tick += OnTick; _timer.Start();
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        PreviewKeyDown += OnPreviewKey;
        Deactivated += (_, _) => Model.HideSecret();
        Closing += (_, e) =>
        {
            if (!Model.Busy) return;
            e.Cancel = true; Model.Notify("正在完成文件操作，请完成后再关闭。你仍可立即锁定。");
        };
        Closed += (_, _) =>
        {
            Model.Lock(); Model.Changed -= Render; Model.SessionLocked -= OnSessionLocked; Model.SessionUnlocked -= OnSessionUnlocked;
            _idle.Dispose(); _timer.Stop(); _timer.Tick -= OnTick;
            if (_clipboard != null) _clipboard.CleanupFailed -= OnClipboardFailure;
            _clipboard?.Dispose(); ClearSensitiveControls();
        };
        Loaded += async (_, _) =>
        {
            Width = Math.Min(Width, SystemParameters.WorkArea.Width);
            Height = Math.Min(Height, SystemParameters.WorkArea.Height);
            UnlockPassword.Focus();
            if (_demo) await Model.Unlock("demo", "demo");
        };
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Find, (_, _) => FocusSearch(), (_, e) => e.CanExecute = !Model.IsLocked && Model.Pane.Length == 0));
        _ready = true; Render();
    }
    private void OnClipboardFailure() => Model.Notify("剪贴板连续被占用，自动清理未完成；请手动清理已复制的密钥。");
    private void OnTick(object? sender, EventArgs e)
    {
        Model.Tick();
        if (_copySource != null && DateTime.UtcNow >= _copyUntil) ResetCopyFeedback();
        if (_today != DateTime.Today) { _today = DateTime.Today; Model.Search(Model.Query, Model.Filter); }
    }
    private void OnSessionUnlocked() { _idle.Resume(); SearchBox.Focus(); }
    private void OnSessionLocked()
    {
        ResetCopyFeedback(); _idle.Pause(); _clipboard?.Tick(true); ClearSensitiveControls(); SearchBox.Clear();
        _detailRequested = false; ActivityLabel.Text = "";
        Dispatcher.BeginInvoke(() => UnlockPassword.Focus(), DispatcherPriority.Input);
    }
    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private void Render()
    {
        if (!_ready || _rendering) return;
        _rendering = true;
        try
        {
            var dark = Model.Settings.Theme == "Dark";
            if (_dark != dark) { WorkspacePalette.Apply(Resources, dark, true); _dark = dark; }
            ThemeButton.Content = dark ? "浅色模式" : "深色模式";
            LockScreen.Visibility = Visible(Model.IsLocked);
            Workspace.Visibility = Visible(!Model.IsLocked);
            Workspace.IsEnabled = !Model.Busy && Model.Pane.Length == 0;
            ManageButton.IsEnabled = !Model.IsLocked && !Model.Busy && Model.Pane.Length == 0;
            ThemeButton.IsEnabled = !Model.Busy;
            LockButton.IsEnabled = !Model.IsLocked;
            SessionLabel.Text = Model.IsLocked ? "●  已锁定" : Model.Busy ? "●  正在处理" : "●  已解锁";
            CreateConfirmation.Visibility = Visible(Model.IsNew);
            UnlockDescription.Text = Model.IsNew ? "创建主密码（至少 8 位），建立你的本地空间。" : "输入主密码，继续访问本地空间。";
            UnlockButton.Content = Model.Busy ? "正在处理…" : Model.IsNew ? "创建本地空间" : "解锁空间";
            UnlockButton.IsEnabled = !Model.Busy; UnlockPassword.IsEnabled = !Model.Busy; ConfirmPassword.IsEnabled = !Model.Busy;
            UnlockError.Text = Model.Error;
            Feedback.Text = Model.Busy ? "正在完成操作，请稍候…" : Model.Pane.Length == 0 && !Model.IsLocked && Model.Error.Length > 0 ? Model.Error : Model.Feedback;
            if (!ReferenceEquals(EntryList.ItemsSource, Model.Entries)) EntryList.ItemsSource = Model.Entries;
            EntryList.SelectedItem = Model.Selected;
            DetailContent.DataContext = Model.Selected;
            DetailKey.Text = Model.RevealedSecret ?? Model.Selected?.KeyHint ?? "";
            RevealButton.Content = Model.RevealedSecret == null ? "显示" : "隐藏";
            EditButton.IsEnabled = Model.Selected != null;
            CopyButton.IsEnabled = Model.Selected?.Value.HasKey == true; RevealButton.IsEnabled = CopyButton.IsEnabled;
            if (SearchBox.Text != Model.Query) SearchBox.Text = Model.Query;
            if (PlatformFilter.ItemsSource is not IReadOnlyList<string> platforms || !platforms.SequenceEqual(Model.Platforms)) PlatformFilter.ItemsSource = Model.Platforms;
            if (TagFilter.ItemsSource is not IReadOnlyList<string> tags || !tags.SequenceEqual(Model.Tags)) TagFilter.ItemsSource = Model.Tags;
            PlatformFilter.SelectedItem = Model.Platform; TagFilter.SelectedItem = Model.Tag;
            FilterAll.Content = Model.AllLabel; FilterSoon.Content = Model.SoonLabel; FilterExpired.Content = Model.ExpiredLabel;
            FilterAll.IsChecked = Model.Filter == "All"; FilterSoon.IsChecked = Model.Filter == "Soon"; FilterExpired.IsChecked = Model.Filter == "Expired";
            CountLabel.Text = $"{Model.Entries.Count} 条记录";
            SearchPlaceholder.Visibility = Visible(SearchBox.Text.Length == 0); ClearSearchButton.Visibility = Visible(SearchBox.Text.Length > 0);
            EmptyState.Visibility = Visible(Model.Entries.Count == 0);
            EmptyTitle.Text = Model.HasRecords ? "没有找到匹配的记录" : "从第一条密钥开始";
            EmptyDescription.Text = Model.HasRecords ? "试试平台名称，或清除当前筛选。" : "把连接信息收在一起，需要时随手取用。";
            EmptyAction.Content = Model.HasRecords ? "清除搜索与筛选" : "新增第一条记录";
            Feedback.SetResourceReference(TextBlock.ForegroundProperty, Model.Error.Length > 0 ? "PDanger" : "PSuccess");
            EditorOverlay.Visibility = Visible(Model.Pane == "Edit");
            ManagementOverlay.Visibility = Visible(Model.Pane.Length > 0 && Model.Pane != "Edit");
            if (_lastPane != Model.Pane) { InitializePane(); _lastPane = Model.Pane; }
            EditorError.Text = Model.Error; ManagementError.Text = Model.Error;
            EditorPanel.IsEnabled = !Model.Busy;
            ManagementOverlay.IsEnabled = !Model.Busy;
            SaveEditButton.Content = Model.Busy ? "正在保存…" : "保存记录";
            ApplyResponsiveLayout();
        }
        finally { _rendering = false; }
    }
    private void InitializePane()
    {
        ClearSensitiveControls();
        if (Model.Pane == "Edit" && Model.Draft is { } draft)
        {
            EditorTitle.Text = Model.NewDraft ? "新增密钥" : "编辑记录";
            EditName.Text = draft.Name; EditProvider.Text = draft.Provider; EditKey.Password = draft.ApiKey;
            EditUrl.Text = draft.BaseUrl; EditModel.Text = draft.Model; EditTags.Text = draft.Tags;
            EditNotes.Text = draft.Notes; EditExpiry.Text = draft.ExpiresUtc?.ToString("yyyy-MM-dd") ?? "";
            Dispatcher.BeginInvoke(() => EditName.Focus(), DispatcherPriority.Input);
        }
        var pane = Model.Pane;
        SettingsFields.Visibility = Visible(pane == "Settings");
        FileFields.Visibility = Visible(pane is "Backup" or "Import" or "Csv");
        PasswordFields.Visibility = Visible(pane is "Backup" or "Import" or "Password");
        CurrentPasswordFields.Visibility = Visible(pane == "Password");
        OperationConfirmationFields.Visibility = Visible(pane is "Backup" or "Password");
        PreviewFields.Visibility = Visible(pane == "Preview");
        CsvFields.Visibility = Visible(pane == "Csv"); DeleteDescription.Visibility = Visible(pane == "Delete");
        PasswordFieldLabel.Text = pane == "Password" ? "新主密码（至少 8 位）" : "备份密码";
        ManagementTitle.Text = pane switch { "Settings" => "数据与设置", "Backup" => "导出加密备份", "Import" => "从备份导入", "Preview" => "核对导入", "Password" => "修改主密码", "Csv" => "导出明文 CSV", "Delete" => "删除记录", _ => "" };
        ManagementSubtitle.Text = pane switch { "Settings" => "管理本地空间与自动保护。", "Backup" => "为这份备份设置独立密码，并妥善保管。", "Import" => "选择加密备份并输入该备份的密码。", "Preview" => "相同 Id 的记录视为同一条，名称相同仍可共存。", "Password" => "写入并验证成功后，新密码才会生效。", "Delete" => "请核对即将删除的记录。", _ => "仅在确实需要明文文件时使用。" };
        SubmitManagement.Content = pane switch { "Settings" => "保存设置", "Import" => "读取并预览", "Preview" => "确认导入", "Delete" => "确认删除", "Password" => "修改主密码", _ => "导出文件" };
        if (pane == "Settings")
        {
            AutoLockEnabled.IsChecked = Model.Settings.AutoLockMinutes > 0; ClipboardEnabled.IsChecked = Model.Settings.ClipboardClearSeconds > 0;
            IdleInput.Text = Math.Max(1, Model.Settings.AutoLockMinutes).ToString(); ClipboardInput.Text = Math.Max(1, Model.Settings.ClipboardClearSeconds).ToString();
        }
        if (pane == "Preview" && Model.ImportPreview is { } preview)
        {
            ImportCounts.Text = $"备份中有 {preview.Incoming} 条记录\n合并将新增 {preview.Added} 条，跳过 {preview.Duplicate} 条重复 Id\n当前空间有 {preview.Current} 条记录";
            RestorePreferences.IsEnabled = preview.HasSettings; RestorePreferences.IsChecked = false;
            MergeImport.IsChecked = true; ReplaceImport.IsChecked = false;
        }
        if (pane == "Delete") DeleteDescription.Text = $"「{Model.Selected?.Name}」\n删除后不能直接撤销。需要恢复时，请使用此前导出的备份。";
        if (pane is "Backup" or "Import" or "Csv") FilePath.Clear();
        ConfirmCsv.IsChecked = false;
        if (pane.Length > 0 && pane != "Edit") Dispatcher.BeginInvoke(() => CancelManagement.Focus(), DispatcherPriority.Input);
        if (pane.Length == 0 && !Model.IsLocked) Dispatcher.BeginInvoke(() => SearchBox.Focus(), DispatcherPriority.Input);
    }
    private void ClearSensitiveControls()
    {
        UnlockPassword.Clear(); ConfirmPassword.Clear(); EditKey.Clear(); CurrentPassword.Clear(); OperationPassword.Clear(); OperationConfirmation.Clear();
        EditName.Clear(); EditProvider.Clear(); EditUrl.Clear(); EditModel.Clear(); EditTags.Clear(); EditNotes.Clear(); EditExpiry.Clear();
        FilePath.Clear(); ImportCounts.Text = ""; DeleteDescription.Text = "";
    }
    internal void ApplyResponsiveLayout()
    {
        if (!_ready) return;
        var narrow = ActualWidth < 1080;
        SpaceBadge.Visibility = Visible(!narrow);
        var detail = Model.Selected != null && (!narrow || _detailRequested);
        DetailGap.Width = new GridLength(!narrow && detail ? 20 : 0);
        DetailColumn.Width = new GridLength(!narrow && detail ? 306 : 0);
        Grid.SetColumn(DetailPane, narrow ? 0 : 2); Grid.SetColumnSpan(DetailPane, narrow ? 3 : 1);
        ListPane.Visibility = Visible(!(narrow && detail)); SearchPanel.Visibility = ListPane.Visibility; FilterPanel.Visibility = ListPane.Visibility;
        DetailPane.Visibility = Visible(detail); CloseDetailButton.Content = narrow ? "← 返回列表" : "收起";
    }
    private void OnTheme(object sender, RoutedEventArgs e) => Model.ToggleTheme();
    private void OnLock(object sender, RoutedEventArgs e) => Model.Lock();
    private async void OnUnlock(object sender, RoutedEventArgs e)
    { var password = UnlockPassword.Password; var confirm = ConfirmPassword.Password; UnlockPassword.Clear(); ConfirmPassword.Clear(); await Model.Unlock(password, confirm); }
    private void OnUnlockKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { OnUnlock(sender, e); e.Handled = true; } }
    private void OnSearch(object sender, TextChangedEventArgs e) { if (_ready && !_rendering) Model.Search(SearchBox.Text, Model.Filter); }
    private void OnFilter(object sender, RoutedEventArgs e)
    { if (_ready && !_rendering && sender is RadioButton { IsChecked: true }) Model.Search(SearchBox.Text, sender == FilterSoon ? "Soon" : sender == FilterExpired ? "Expired" : "All"); }
    private void OnClearSearch(object sender, RoutedEventArgs e) { SearchBox.Clear(); FocusSearch(); }
    private void OnResetFilter(object sender, RoutedEventArgs e) { if (!Model.HasRecords) { Model.BeginEdit(true); return; } SearchBox.Clear(); Model.ResetFilters(); FocusSearch(); }
    private void OnCategoryFilter(object sender, SelectionChangedEventArgs e)
    { if (_ready && !_rendering) Model.FilterBy(PlatformFilter.SelectedItem as string ?? "全部平台", TagFilter.SelectedItem as string ?? "全部标签"); }
    private void OnSelection(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _rendering) return;
        if (EntryList.IsKeyboardFocusWithin || EntryList.IsMouseOver) _detailRequested = true;
        Model.Select(EntryList.SelectedItem as WorkspaceEntry);
    }
    private void OnListPointer(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(EntryList, source) is not ListBoxItem) return;
        for (var node = source; node != null && node != EntryList; node = VisualTreeHelper.GetParent(node)) if (node is Button) return;
        _detailRequested = true; ApplyResponsiveLayout();
    }
    private void OnCloseDetail(object sender, RoutedEventArgs e) { _detailRequested = false; Model.Select(null); EntryList.Focus(); }
    private void OnRowCopy(object sender, RoutedEventArgs e) { if (sender is Button button) CopyFeedback(button, button.Tag as WorkspaceEntry); e.Handled = true; }
    private void OnCopy(object sender, RoutedEventArgs e) => CopyFeedback(CopyButton, Model.Selected);
    private void OnCopyUrl(object sender, RoutedEventArgs e) => CopyFeedback(CopyUrlButton, Model.Selected, true);
    private void CopyFeedback(Button button, WorkspaceEntry? entry, bool url = false)
    {
        ResetCopyFeedback(); var success = Model.Copy(entry, url);
        _copySource = button; _copyOriginal = button.Content;
        button.Content = success ? "已复制" : "未复制"; _copyUntil = DateTime.UtcNow.AddSeconds(3);
    }
    private void ResetCopyFeedback()
    { if (_copySource != null) _copySource.Content = _copyOriginal; _copySource = null; _copyOriginal = null; }
    private void OnReveal(object sender, RoutedEventArgs e) => Model.Reveal();
    private void OnAdd(object sender, RoutedEventArgs e) => Model.BeginEdit(true);
    private void OnEdit(object sender, RoutedEventArgs e) => Model.BeginEdit(false);
    private void OnDelete(object sender, RoutedEventArgs e) => Model.OpenPane("Delete");
    private void OnCancelEdit(object sender, RoutedEventArgs e) => Model.ClosePane();
    private async void OnSaveEdit(object sender, RoutedEventArgs e) => await Model.SaveDraft(new ApiEntry
    { Name = EditName.Text, Provider = EditProvider.Text, ApiKey = EditKey.Password, BaseUrl = EditUrl.Text, Model = EditModel.Text, Tags = EditTags.Text, Notes = EditNotes.Text }, EditExpiry.Text);
    private void OnManage(object sender, RoutedEventArgs e) => Model.OpenPane("Settings");
    private void SwitchManagement(string pane) { Model.ClosePane(); Model.OpenPane(pane); }
    private void OnBackup(object sender, RoutedEventArgs e) => SwitchManagement("Backup");
    private void OnImport(object sender, RoutedEventArgs e) => SwitchManagement("Import");
    private void OnPassword(object sender, RoutedEventArgs e) => SwitchManagement("Password");
    private void OnCsv(object sender, RoutedEventArgs e) => SwitchManagement("Csv");
    private void OnCancelManagement(object sender, RoutedEventArgs e) => Model.ClosePane();
    private void OnChooseFile(object sender, RoutedEventArgs e)
    {
        var pane = Model.Pane;
        FileDialog dialog = pane == "Import" ? new OpenFileDialog { Filter = "加密备份|*.akvbak;*.akv;*.bak|所有文件|*.*" } :
            new SaveFileDialog { Filter = pane == "Csv" ? "CSV 文件|*.csv" : "加密备份|*.akvbak", DefaultExt = pane == "Csv" ? ".csv" : ".akvbak", FileName = "api-keys-" + DateTime.Today.ToString("yyyyMMdd") };
        if (dialog.ShowDialog(this) == true && !Model.IsLocked && Model.Pane == pane) FilePath.Text = dialog.FileName;
    }
    private async void OnSubmitManagement(object sender, RoutedEventArgs e)
    {
        var pane = Model.Pane;
        if (pane is "Backup" or "Import" or "Csv" && FilePath.Text.Length == 0) { Model.SetError("请先选择文件位置。"); return; }
        switch (pane)
        {
            case "Settings":
                if ((AutoLockEnabled.IsChecked == true && (!int.TryParse(IdleInput.Text, out var minutes) || minutes < 1)) ||
                    (ClipboardEnabled.IsChecked == true && (!int.TryParse(ClipboardInput.Text, out var seconds) || seconds < 1)))
                { Model.SetError("启用保护时，请填写大于 0 的时间。"); return; }
                Model.SavePreferences(AutoLockEnabled.IsChecked == true ? IdleInput.Text : "0", ClipboardEnabled.IsChecked == true ? ClipboardInput.Text : "0"); break;
            case "Password": await Model.ChangePassword(CurrentPassword.Password, OperationPassword.Password, OperationConfirmation.Password); break;
            case "Import": await Model.PrepareImport(FilePath.Text, OperationPassword.Password); break;
            case "Preview": await Model.CommitImport(ReplaceImport.IsChecked == true, RestorePreferences.IsChecked == true); break;
            case "Delete": await Model.Delete(); break;
            case "Backup": await Model.Export(FilePath.Text, OperationPassword.Password, OperationConfirmation.Password, false); break;
            case "Csv":
                if (ConfirmCsv.IsChecked != true) { Model.SetError("请先确认你了解明文导出的含义。"); return; }
                await Model.Export(FilePath.Text, "", "", true); break;
        }
    }
    private void FocusSearch() { if (Model.IsLocked || Model.Pane.Length > 0) return; _detailRequested = false; ApplyResponsiveLayout(); SearchBox.Focus(); SearchBox.SelectAll(); }
    private void OnPreviewKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.C && Model.Pane.Length == 0 && (e.OriginalSource is ListBoxItem || e.OriginalSource is ListBox)) { CopyFeedback(CopyButton, Model.Selected); e.Handled = true; }
            else if (e.Key == Key.L) { Model.Lock(); e.Handled = true; }
            else if (e.Key == Key.T && !Model.Busy) { Model.ToggleTheme(); e.Handled = true; }
            else if (e.Key == Key.F && Model.Pane.Length == 0) { FocusSearch(); e.Handled = true; }
            else if (e.Key == Key.N && Model.Pane.Length == 0 && !Model.IsLocked) { Model.BeginEdit(true); e.Handled = true; }
        }
        else if (e.Key == Key.Escape && Model.Pane.Length > 0) { Model.ClosePane(); e.Handled = true; }
        else if (e.Key == Key.Enter && Model.Pane.Length == 0 && EntryList.IsKeyboardFocusWithin && e.OriginalSource is not Button)
        { _detailRequested = true; ApplyResponsiveLayout(); e.Handled = true; }
    }
}
