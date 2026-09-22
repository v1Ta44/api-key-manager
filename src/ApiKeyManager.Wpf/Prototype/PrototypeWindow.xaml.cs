using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ApiKeyManager.Wpf.Prototype;

/// <summary>
/// 隔离的设计原型。只修改合成内存数据；不构造旧 ViewModel、不解析数据路径，
/// 不读写用户库或设置、不访问系统剪贴板。生产用例在设计确认后由 Application 接管。
/// </summary>
public partial class PrototypeWindow : Window
{
    private readonly List<PreviewEntry> _entries = PreviewEntry.Samples();
    private readonly DispatcherTimer _revealTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _feedbackTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _ready;
    private bool _dark;
    private bool _warm;
    private bool _revealed;
    private bool _detailRequested;
    private string _filter = "All";
    private string? _editingId;
    private Button? _copySource;

    internal PreviewEntry? Selected => EntryList.SelectedItem as PreviewEntry;
    internal int VisibleCount => EntryList.Items.Count;
    internal bool IsLocked => LockScreen.Visibility == Visibility.Visible;
    internal bool IsEditing => EditorOverlay.Visibility == Visibility.Visible;
    internal bool IsDark => _dark;
    internal bool IsWarm => _warm;

    public PrototypeWindow(bool dark = false)
    {
        InitializeComponent();
        _dark = dark;
        ApplyTheme();
        _revealTimer.Tick += HideRevealedKey;
        _feedbackTimer.Tick += ClearCopyFeedback;
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        PreviewKeyDown += OnPreviewKey;
        Closed += (_, _) =>
        {
            _revealTimer.Stop();
            _feedbackTimer.Stop();
            _revealTimer.Tick -= HideRevealedKey;
            _feedbackTimer.Tick -= ClearCopyFeedback;
            ClearDraft();
            UnlockPassword.Clear();
        };
        Loaded += (_, _) =>
        {
            // 按屏幕工作区约束初始尺寸；诊断渲染独立设置所需尺寸。
            Width = Math.Min(Width, SystemParameters.WorkArea.Width);
            Height = Math.Min(Height, SystemParameters.WorkArea.Height);
            SearchBox.Focus();
        };
        _ready = true;
        RefreshList();
    }

    private void ApplyTheme()
    {
        Themes.WorkspacePalette.Apply(Resources, _dark, _warm);
        ThemeButton.Content = _dark ? "浅色模式" : "深色模式";
        StyleButton.Content = _warm ? "返回：精密工具 →" : "对照：温润档案 →";
    }

    private void OnTheme(object sender, RoutedEventArgs e) { _dark = !_dark; ApplyTheme(); }
    private void OnStyle(object sender, RoutedEventArgs e) { _warm = !_warm; ApplyTheme(); }
    private void OnSearch(object sender, TextChangedEventArgs e) { if (_ready) RefreshList(); }
    private void OnFilter(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton { IsChecked: true } selected) return;
        _filter = selected == FilterSoon ? "Soon" : selected == FilterExpired ? "Expired" : "All";
        FilterAll.IsChecked = _filter == "All";
        FilterSoon.IsChecked = _filter == "Soon";
        FilterExpired.IsChecked = _filter == "Expired";
        RefreshList();
    }
    private void OnClearSearch(object sender, RoutedEventArgs e) { SearchBox.Clear(); SearchBox.Focus(); }
    private void OnResetFilter(object sender, RoutedEventArgs e) { SearchBox.Clear(); FilterAll.IsChecked = true; SearchBox.Focus(); }

    private void RefreshList(string? selectedId = null)
    {
        selectedId ??= Selected?.Id;
        var query = SearchBox.Text.Trim();
        var filtered = _entries.Where(e =>
            (query.Length == 0 || new[] { e.Name, e.Provider, e.Model, e.Tags, e.Notes, e.Url }
                .Any(s => s.Contains(query, StringComparison.OrdinalIgnoreCase))) &&
            (_filter == "All" || e.ExpiryKind == _filter)).ToList();
        EntryList.ItemsSource = filtered;
        EntryList.SelectedItem = filtered.FirstOrDefault(e => e.Id == selectedId) ?? filtered.FirstOrDefault();
        SearchPlaceholder.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FilterAll.Content = $"全部  {_entries.Count}";
        FilterSoon.Content = $"即将到期  {_entries.Count(e => e.ExpiryKind == "Soon")}";
        FilterExpired.Content = $"已过期  {_entries.Count(e => e.ExpiryKind == "Expired")}";
        CountLabel.Text = $"{filtered.Count} 条记录";
        UpdateDetail();
    }

    private void OnSelection(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        // 键盘/鼠标选择可在窄窗口打开详情；列表刷新不会无端覆盖搜索区域。
        if (EntryList.IsKeyboardFocusWithin || EntryList.IsMouseOver) _detailRequested = true;
        UpdateDetail();
    }

    private void OnListPointer(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(EntryList, source) is not ListBoxItem) return;
        for (var node = source; node != null && node != EntryList; node = VisualTreeHelper.GetParent(node))
            if (node is Button) return;
        _detailRequested = true;
        ApplyResponsiveLayout();
    }

    private void UpdateDetail()
    {
        HideRevealedKey(null, EventArgs.Empty);
        DetailContent.DataContext = Selected;
        EditButton.IsEnabled = Selected != null;
        CopyButton.IsEnabled = Selected != null && Selected.Key.Length > 0;
        RevealButton.IsEnabled = CopyButton.IsEnabled;
        ApplyResponsiveLayout();
    }

    internal void ApplyResponsiveLayout(double? availableWidth = null)
    {
        if (!_ready) return;
        var narrow = (availableWidth ?? ActualWidth) < 1080;
        var showDetail = Selected != null && (!narrow || _detailRequested);
        DetailGap.Width = new GridLength(!narrow && showDetail ? 20 : 0);
        DetailColumn.Width = new GridLength(!narrow && showDetail ? 306 : 0);
        Grid.SetColumn(DetailPane, narrow ? 0 : 2);
        Grid.SetColumnSpan(DetailPane, narrow ? 3 : 1);
        ListPane.Visibility = narrow && showDetail ? Visibility.Collapsed : Visibility.Visible;
        SearchPanel.Visibility = narrow && showDetail ? Visibility.Collapsed : Visibility.Visible;
        FilterPanel.Visibility = narrow && showDetail ? Visibility.Collapsed : Visibility.Visible;
        DetailPane.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
        CloseDetailButton.Content = narrow ? "← 返回列表" : "收起";
        // 宽窗口允许收起详情；重新选择记录会展开。
    }

    private void OnCloseDetail(object sender, RoutedEventArgs e)
    {
        _detailRequested = false;
        EntryList.SelectedItem = null;
        ApplyResponsiveLayout();
        EntryList.Focus();
    }

    private void OnRowCopy(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PreviewEntry entry } button) PreviewCopy(entry, button);
        e.Handled = true;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (Selected != null) PreviewCopy(Selected, CopyButton);
    }

    private void PreviewCopy(PreviewEntry entry, Button button)
    {
        if (IsLocked || IsEditing) return;
        ClearCopyFeedback(null, EventArgs.Empty);
        if (entry.Key.Length == 0) { Feedback.Text = "这条记录未设置密钥。"; return; }
        _copySource = button;
        button.Content = button == CopyButton ? "已复制 ✓" : "已复制";
        Feedback.Text = $"「{entry.Name}」复制反馈预览 · 未改动系统剪贴板";
        _feedbackTimer.Start();
    }

    private void ClearCopyFeedback(object? sender, EventArgs e)
    {
        _feedbackTimer.Stop();
        if (_copySource != null) _copySource.Content = _copySource == CopyButton ? "复制密钥" : "复制";
        _copySource = null;
    }

    private void OnReveal(object sender, RoutedEventArgs e)
    {
        if (_revealed) { HideRevealedKey(null, EventArgs.Empty); return; }
        if (Selected == null || IsLocked) return;
        _revealed = true;
        DetailKey.Text = Selected.Key;
        RevealButton.Content = "隐藏";
        Feedback.Text = "仅显示这一条示例密钥，10 秒后恢复隐藏。";
        _revealTimer.Start();
    }

    private void HideRevealedKey(object? sender, EventArgs e)
    {
        _revealTimer.Stop();
        _revealed = false;
        if (DetailKey == null) return;
        DetailKey.Text = Selected?.KeyHint ?? "";
        RevealButton.Content = "显示";
    }

    private void OnAdd(object sender, RoutedEventArgs e) => OpenEditor(null);
    private void OnEdit(object sender, RoutedEventArgs e) { if (Selected != null) OpenEditor(Selected); }

    private void OpenEditor(PreviewEntry? entry)
    {
        if (IsLocked) return;
        HideRevealedKey(null, EventArgs.Empty);
        _editingId = entry?.Id;
        EditorTitle.Text = entry == null ? "新增密钥" : "编辑记录";
        EditName.Text = entry?.Name ?? "";
        EditProvider.Text = entry?.Provider ?? "";
        EditKey.Password = entry?.Key ?? "";
        EditUrl.Text = entry?.Url ?? "";
        EditModel.Text = entry?.Model ?? "";
        EditTags.Text = entry?.Tags ?? "";
        EditNotes.Text = entry?.Notes ?? "";
        EditExpiry.Text = entry?.Expiry?.ToString("yyyy-MM-dd") ?? "";
        EditorError.Text = "";
        Feedback.Text = "";
        EditorOverlay.Visibility = Visibility.Visible;
        Workspace.IsEnabled = false;
        EditName.Focus();
        EditName.SelectAll();
    }

    private void OnCancelEdit(object sender, RoutedEventArgs e) => CloseEditor();

    private void CloseEditor()
    {
        EditorOverlay.Visibility = Visibility.Collapsed;
        Workspace.IsEnabled = true;
        ClearDraft();
        if (!IsLocked) SearchBox.Focus();
    }

    private void ClearDraft()
    {
        _editingId = null;
        EditName.Clear(); EditProvider.Clear(); EditKey.Clear(); EditUrl.Clear();
        EditModel.Clear(); EditTags.Clear(); EditNotes.Clear(); EditExpiry.Clear();
        EditorError.Text = "";
    }

    private void OnSaveEdit(object sender, RoutedEventArgs e)
    {
        if (IsLocked || !IsEditing) return;
        if (string.IsNullOrWhiteSpace(EditName.Text))
        {
            EditorError.Text = "请填写记录名称。"; EditName.Focus(); return;
        }
        DateTime? expiry = null;
        if (!string.IsNullOrWhiteSpace(EditExpiry.Text))
        {
            if (!DateTime.TryParseExact(EditExpiry.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            { EditorError.Text = "到期日请按 yyyy-MM-dd 填写，或留空。"; EditExpiry.Focus(); return; }
            expiry = parsed.Date;
        }
        var entry = new PreviewEntry(_editingId ?? Guid.NewGuid().ToString("N"), EditName.Text.Trim(), EditProvider.Text.Trim(),
            EditKey.Password.Trim(), EditUrl.Text.Trim(), EditModel.Text.Trim(), EditTags.Text.Trim(), EditNotes.Text, expiry);
        var index = _entries.FindIndex(e => e.Id == _editingId);
        if (index < 0) _entries.Insert(0, entry); else _entries[index] = entry;
        CloseEditor();
        SearchBox.Clear(); FilterAll.IsChecked = true;
        RefreshList(entry.Id);
        _detailRequested = true;
        ApplyResponsiveLayout();
        Feedback.Text = $"已更新「{entry.Name}」的内存预览，关闭原型后恢复。";
    }

    private void OnLock(object sender, RoutedEventArgs e)
    {
        CloseEditor();
        HideRevealedKey(null, EventArgs.Empty);
        ClearCopyFeedback(null, EventArgs.Empty);
        Workspace.Visibility = Visibility.Collapsed;
        LockScreen.Visibility = Visibility.Visible;
        SessionLabel.Text = "●  已锁定";
        LockButton.IsEnabled = false;
        Feedback.Text = "";
        UnlockError.Text = "";
        UnlockPassword.Clear();
        UnlockPassword.Focus();
    }

    private void OnUnlock(object sender, RoutedEventArgs e)
    {
        if (UnlockPassword.Password != "demo")
        { UnlockError.Text = "演示密码为 demo，请重新输入。"; UnlockPassword.Clear(); UnlockPassword.Focus(); return; }
        UnlockPassword.Clear();
        UnlockError.Text = "";
        LockScreen.Visibility = Visibility.Collapsed;
        Workspace.Visibility = Visibility.Visible;
        SessionLabel.Text = "●  已解锁";
        LockButton.IsEnabled = true;
        HideRevealedKey(null, EventArgs.Empty);
        SearchBox.Focus();
    }

    private void OnUnlockKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { OnUnlock(sender, e); e.Handled = true; } }

    private void OnPreviewKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.T) { OnTheme(sender, e); e.Handled = true; }
            if (e.Key == Key.L && !IsLocked) { OnLock(sender, e); e.Handled = true; }
            if (!IsLocked && !IsEditing && e.Key == Key.F)
            {
                _detailRequested = false;
                ApplyResponsiveLayout();
                SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true;
            }
            if (!IsLocked && !IsEditing && e.Key == Key.N) { OpenEditor(null); e.Handled = true; }
        }
        if (e.Key == Key.Escape && !IsLocked)
        {
            if (IsEditing) CloseEditor(); else if (SearchBox.Text.Length > 0) SearchBox.Clear(); else OnCloseDetail(sender, e);
            e.Handled = true;
        }
        if (e.Key == Key.Enter && EntryList.IsKeyboardFocusWithin && Keyboard.FocusedElement is not Button && !IsLocked && !IsEditing)
        { _detailRequested = true; ApplyResponsiveLayout(); EditButton.Focus(); e.Handled = true; }
    }
}

internal sealed record PreviewEntry(string Id, string Name, string Provider, string Key, string Url, string Model, string Tags, string Notes, DateTime? Expiry)
{
    public string Monogram => Provider.Length == 0 ? "K" : Provider[..1].ToUpperInvariant();
    public string Subtitle => string.IsNullOrWhiteSpace(Model) ? Provider : $"{Provider} · {Model}";
    public string KeyHint => Key.Length == 0 ? "未设置" : $"•••• •••• {Key[^Math.Min(4, Key.Length)..]}";
    public string CopyLabel => $"复制 {Name} 的密钥";
    public string ExpiryKind => Expiry == null ? "None" : Expiry.Value.Date < DateTime.Today ? "Expired" : Expiry.Value.Date <= DateTime.Today.AddDays(14) ? "Soon" : "Valid";
    public string ExpiryLabel => ExpiryKind switch { "Expired" => "已过期", "Soon" => $"{(Expiry!.Value.Date - DateTime.Today).Days} 天后到期", "Valid" => "有效", _ => "未设置" };

    internal static List<PreviewEntry> Samples() =>
    [
        new("openai", "OpenAI 生产", "OpenAI", "sample-not-real-openai-4a9f", "https://api.openai.com/v1", "gpt-4o", "生产 · 官方", "日常应用的默认连接。\n与测试环境分开管理。", DateTime.Today.AddDays(45)),
        new("claude", "Claude 研究", "Anthropic", "sample-not-real-claude-8c2b", "https://api.anthropic.com", "claude-sonnet-4-5", "研究 · 长文本", "长上下文研究用示例。", DateTime.Today.AddDays(-4)),
        new("deepseek", "DeepSeek 测试", "DeepSeek", "sample-not-real-deepseek-1d7e", "https://api.deepseek.com", "deepseek-chat", "测试 · 开发", "开发环境专用。", DateTime.Today.AddDays(7)),
        new("gemini", "Gemini 备用", "Google", "sample-not-real-gemini-3f6a", "https://generativelanguage.googleapis.com", "gemini-2.5-pro", "备用", "备用连接信息。", DateTime.Today.AddDays(120)),
        new("gateway", "内部网关", "自建", "sample-not-real-gateway-9b2c", "https://gateway.example.invalid/v1", "qwen-max", "内网 · 生产 · 团队", "统一网关示例地址。", null),
        new("ollama", "本地 Ollama", "本地", "", "http://127.0.0.1:11434/v1", "llama3.1", "本地 · 免费", "本地服务允许不设置密钥。", null)
    ];
}
