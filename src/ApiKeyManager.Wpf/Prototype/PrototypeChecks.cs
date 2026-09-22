using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ApiKeyManager.Wpf.Prototype;

/// <summary>真实 WPF 控件的离屏布局、路由按钮事件与渲染检查；不等于实机键鼠或用户验收。</summary>
internal static class PrototypeChecks
{
    internal static int Run(string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory)) return 2;
        var output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        var errors = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        PrototypeWindow? window = null;
        try
        {
            window = new PrototypeWindow();
            // 连接到真实 PresentationSource，覆盖 Loaded、模板及内部文本视图初始化。
            // 创建屏幕外、不激活的本进程窗口；不驱动系统键鼠或接触其他应用。
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.Top = -20000;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Show();
            void Check(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
                checks.Add(message);
            }
            void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            void Layout(double width = 1180, double height = 760)
            {
                window.Root.Width = width;
                window.Root.Height = height;
                window.SizeToContent = SizeToContent.WidthAndHeight;
                window.ApplyResponsiveLayout(width);
                window.Root.Measure(new Size(width, height));
                window.Root.Arrange(new Rect(0, 0, width, height));
                window.Root.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                window.Root.UpdateLayout();
            }
            void Inside(FrameworkElement element, string name)
            {
                var bounds = element.TransformToAncestor(window.Root).TransformBounds(new Rect(element.RenderSize));
                Check(element.ActualWidth > 0 && element.ActualHeight > 0 && bounds.Left >= -1 && bounds.Top >= -1 &&
                      bounds.Right <= window.Root.ActualWidth + 1 && bounds.Bottom <= window.Root.ActualHeight + 1,
                      name + " inside viewport");
            }
            void Capture(string name, double width = 1180, double height = 760, double scale = 1)
            {
                Layout(width, height);
                var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(window.Root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, name + ".png"));
                encoder.Save(file);
            }

            Layout();
            Check(window.VisibleCount == 6, "six synthetic entries loaded");
            Check(window.Selected?.Id == "openai", "initial selection");
            Check(window.SearchBox.ActualWidth >= 400, "search fills available space");
            Inside(window.AddButton, "add action");
            Inside(window.EditButton, "detail edit action");
            Capture("01-precision-light");
            Click(window.ThemeButton);
            Check(window.IsDark, "dark palette activated");
            Capture("02-precision-dark");
            Click(window.ThemeButton);
            Click(window.StyleButton);
            Check(window.IsWarm && !window.IsDark, "archive comparison palette activated");
            Capture("03-archive-light");
            Click(window.StyleButton);

            window.SearchBox.Text = "claude";
            Check(window.VisibleCount == 1 && window.Selected?.Id == "claude", "search selects matching entry");
            window.SearchBox.Text = "not-found";
            Check(window.VisibleCount == 0 && window.EmptyState.Visibility == Visibility.Visible, "empty search state");
            Capture("04-no-results");
            window.SearchBox.Clear();
            window.FilterSoon.IsChecked = true;
            Check(window.VisibleCount == 1 && window.Selected?.Id == "deepseek", "soon filter");
            window.FilterExpired.IsChecked = true;
            Check(window.VisibleCount == 1 && window.Selected?.Id == "claude", "expired filter");
            window.FilterAll.IsChecked = true;
            window.EntryList.SelectedIndex = 0;

            Click(window.CopyButton);
            Check(window.Feedback.Text.Contains("未改动系统剪贴板"), "copy simulation explicitly identified");
            Capture("05-copy-feedback");
            Click(window.RevealButton);
            Check(window.DetailKey.Text == window.Selected!.Key, "single-entry reveal");
            window.EntryList.SelectedIndex = 1;
            Check(window.DetailKey.Text == window.Selected!.KeyHint, "selection change hides plaintext");
            window.EntryList.SelectedIndex = 0;

            var original = window.Selected!.Name;
            Click(window.EditButton);
            window.EditName.Text = "未保存的草稿";
            Click(window.CancelEditButton);
            Check(window.Selected!.Name == original, "cancel preserves original entry");
            Click(window.EditButton);
            Check(window.EditName.Text == original && window.EditKey.Password.Length > 0, "editor populated from selected sample");
            Capture("06-edit");
            var textHost = (ScrollViewer)window.EditName.Template.FindName("PART_ContentHost", window.EditName);
            Check(textHost.Content is FrameworkElement textView && textView.ActualHeight >= window.EditName.FontSize,
                  "editor text viewport fits a complete text line");
            window.EditName.Clear();
            Click(window.SaveEditButton);
            Check(window.IsEditing && window.EditorError.Text.Length > 0, "required name validation");
            window.EditName.Text = original;
            window.EditExpiry.Text = "invalid";
            Click(window.SaveEditButton);
            Check(window.IsEditing && window.EditorError.Text.Contains("yyyy-MM-dd"), "date validation");
            window.EditExpiry.Clear();
            Click(window.SaveEditButton);
            Check(!window.IsEditing && window.Selected!.Expiry == null, "draft commits to memory only");

            Click(window.AddButton);
            window.EditName.Text = "临时测试";
            window.EditProvider.Text = "测试平台";
            Click(window.SaveEditButton);
            Check(window.VisibleCount == 7 && window.Selected!.Name == "临时测试", "add preview selects new entry");
            Click(window.EditButton);
            window.EditKey.Password = "synthetic-sensitive-draft";
            Click(window.LockButton);
            Check(window.IsLocked && !window.IsEditing && window.EditKey.Password.Length == 0 && window.EditName.Text.Length == 0,
                  "lock hides workspace and clears editor draft");
            Capture("07-locked");
            window.UnlockPassword.Password = "wrong";
            Click(window.UnlockButton);
            Check(window.IsLocked && window.UnlockError.Text.Length > 0, "invalid demo password stays locked");
            window.UnlockPassword.Password = "demo";
            Click(window.UnlockButton);
            Check(!window.IsLocked && window.UnlockPassword.Password.Length == 0, "demo unlock clears password field");

            window.SearchBox.Text = "OpenAI";
            window.SearchBox.Clear();
            Click(window.CloseDetailButton);
            window.EntryList.SelectedIndex = 0;
            Capture("08-narrow", 900, 600);
            Inside(window.AddButton, "narrow add action");
            Check(window.SearchBox.ActualWidth >= 400, "narrow search remains usable");
            Click(window.AddButton);
            Capture("09-narrow-editor", 900, 600);
            Inside(window.SaveEditButton, "narrow editor save");
            Inside(window.CancelEditButton, "narrow editor cancel");
            Click(window.CancelEditButton);
            Capture("10-wide", 1600, 760);
            Capture("11-render-200-percent", 1180, 760, 2);
            Check(errors.Messages.Count == 0, "no WPF binding warnings or errors");
            var priorFailure = Path.Combine(output, "failure.txt");
            if (File.Exists(priorFailure)) File.Delete(priorFailure);
            File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(new
            {
                evidence = "offscreen-wpf-window-layout-and-routed-events",
                driverAccepted = false,
                passed = checks.Count,
                checks,
                limitations = new[] { "No real keyboard/mouse or multi-monitor DPI validation", "No persistence or system clipboard in prototype", "2x bitmap is render coverage, not real OS scaling evidence" }
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "failure.txt"), ex + Environment.NewLine + string.Join(Environment.NewLine, errors.Messages));
            return 1;
        }
        finally
        {
            window?.Close();
            PresentationTraceSources.DataBindingSource.Listeners.Remove(errors);
        }
    }

    private sealed class BindingErrors : TraceListener
    {
        public List<string> Messages { get; } = new();
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
