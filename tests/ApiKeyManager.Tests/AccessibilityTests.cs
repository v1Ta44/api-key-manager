using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace ApiKeyManager.Tests;

/// <summary>
/// 无障碍元数据回归测试（WPF 版）。
///
/// 为什么改成"解析 XAML"而不是"实例化控件"：
/// 旧版测的是自绘的 WinForms 控件，那种控件必须手动设置 AccessibleName，
/// 所以有"每个控件都要记得设"这条规则，值得用反射逐个断言。
///
/// WPF 不一样：标准控件（Button / TextBox / CheckBox）会自动把
/// Content / Text 暴露给 UI Automation，不需要额外标注。
/// 真正需要人工保证的是两件事：
///   1. 自绘或纯图标控件（GlyphBadge 图标、无文本的 Button）必须有
///      AutomationProperties.Name，否则读屏软件念不出内容；
///   2. 可交互控件要在 Tab 顺序里可达（不能 IsTabStop=False 又没替代路径）。
///
/// 这两点都能从 XAML 文本直接判定，而且比实例化更稳：
/// 不需要 STA 线程、不需要启动 Application、不会因为资源字典
/// 加载顺序不同而抖动，跑得也快得多。
/// </summary>
public sealed class AccessibilityTests
{
    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ApiKeyManager.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("找不到仓库根目录（ApiKeyManager.sln）");
    }

    private static string WpfViewDir =>
        Path.Combine(RepoRoot, "src", "ApiKeyManager.Wpf", "Views");

    private static IEnumerable<(string Path, string Text)> ViewXamls()
    {
        foreach (var f in Directory.GetFiles(WpfViewDir, "*.xaml"))
            yield return (Path.GetFileName(f), File.ReadAllText(f));
    }

    /// <summary>
    /// 纯图标按钮（没有 Content 文字、只放一个字形 TextBlock）必须显式给
    /// AutomationProperties.Name，否则读屏软件只会念"按钮"。
    /// </summary>
    [Fact]
    public void IconOnlyButtons_DeclareAutomationName()
    {
        // 匹配 <Button ...> ... </Button>，且整段里没有 Content="..." 文字
        var buttonBlock = new Regex(@"<Button\b(?<attrs>[^>]*?)(?:/>|>(?<inner>.*?)</Button>)",
            RegexOptions.Singleline);

        var offenders = new List<string>();

        foreach (var (file, text) in ViewXamls())
        {
            foreach (Match m in buttonBlock.Matches(text))
            {
                string attrs = m.Groups["attrs"].Value;
                string inner = m.Groups["inner"].Success ? m.Groups["inner"].Value : "";

                bool hasTextContent = Regex.IsMatch(attrs, @"Content\s*=\s*""[^""]*[\p{L}\p{N}]");
                bool hasTextInBody = Regex.IsMatch(inner, @">\s*[^<\s]");

                if (hasTextContent || hasTextInBody) continue;

                // 纯图标：必须有 AutomationProperties.Name
                if (!attrs.Contains("AutomationProperties.Name"))
                {
                    string snippet = attrs.Length > 60 ? attrs[..60] + "..." : attrs;
                    offenders.Add($"{file}: <Button {snippet}>");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "以下纯图标按钮缺少 AutomationProperties.Name：\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// 主窗口的关键控件必须有无障碍名称，保证读屏软件能报出用途。
    /// </summary>
    [Fact]
    public void MainWindow_KeyControlsAreLabelled()
    {
        string path = Path.Combine(WpfViewDir, "MainWindow.xaml");
        string text = File.ReadAllText(path);

        // 这两项没有可见的文字标签或 Content，读屏软件无从得知用途，
        // 必须显式给 AutomationProperties.Name
        string[] mustBeLabelled = { "EntryList", "TxtSearch" };

        foreach (var name in mustBeLabelled)
        {
            int idx = text.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
            Assert.True(idx >= 0, $"MainWindow.xaml 里找不到 {name}");

            int start = text.LastIndexOf('<', idx);
            int end = text.IndexOf('>', idx);
            string element = text[start..end];

            Assert.True(element.Contains("AutomationProperties.Name"),
                $"{name} 缺少 AutomationProperties.Name（读屏软件无法报出用途）");
        }
    }

    /// <summary>
    /// 键盘快捷键必须有查找、锁定、新增三个常用入口，且不重复。
    ///
    /// 快捷键用代码注册（InputBindings），不用 XAML 的 InputBinding，
    /// 因为命令来自 ViewModel，绑在代码里才能直接引用实例。
    /// </summary>
    [Fact]
    public void KeyboardShortcuts_AreDeclaredAndUnique()
    {
        string path = Path.Combine(RepoRoot, "src", "ApiKeyManager.Wpf", "Views", "MainWindow.xaml.cs");
        string text = File.ReadAllText(path);

        // KeyBinding 的键位与修饰键可能分处两行，因此用 [\s\S] 跨行匹配，
        // 并允许中间出现其他参数（如命令表达式里的括号与逗号）。
        var gestures = Regex.Matches(text,
                @"new\s+KeyBinding\([\s\S]*?Key\.(?<k>\w+)\s*,\s*ModifierKeys\.(?<m>\w+)\s*\)")
            .Select(m => (Key: m.Groups["k"].Value, Mods: m.Groups["m"].Value))
            .ToList();

        Assert.True(gestures.Count > 0, "MainWindow.xaml.cs 没有注册任何快捷键");

        var dupes = gestures.GroupBy(g => (g.Key, g.Mods))
            .Where(g => g.Count() > 1)
            .Select(g => $"Ctrl+{g.Key.Key}")
            .ToList();

        Assert.True(dupes.Count == 0, "存在重复的快捷键：" + string.Join(", ", dupes));

        // 锁定与新增用 KeyBinding 直接绑命令
        Assert.Contains(gestures, g => g.Key == "L");
        Assert.Contains(gestures, g => g.Key == "N");

        // 查找走 ApplicationCommands.Find：这是 WPF 的标准查找命令，
        // 用 CommandBinding 而非 KeyBinding 注册，辅助技术才能识别出
        // "本窗口提供查找功能"，而不是一个匿名的 Ctrl+F。
        Assert.Contains("ApplicationCommands.Find", text);
    }

    /// <summary>
    /// Tab 顺序里不能有被主动禁用的可交互控件。
    /// </summary>
    [Fact]
    public void NoInteractiveControlIsRemovedFromTabOrder()
    {
        var offenders = new List<string>();

        foreach (var (file, text) in ViewXamls())
        {
            foreach (Match m in Regex.Matches(text, @"IsTabStop\s*=\s*""False"""))
            {
                int line = text[..m.Index].Count(c => c == '\n') + 1;
                offenders.Add($"{file}:{line}");
            }
        }

        Assert.True(offenders.Count == 0,
            "以下位置把控件移出了 Tab 顺序，键盘用户将无法到达：\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>
    /// 每个 XAML 都必须能解析成合法 XML —— 这条能挡住拼错的标签、
    /// 未闭合的元素等，让 XML 层面的错误在测试阶段就暴露，而不是运行时。
    /// </summary>
    [Fact]
    public void AllViewXamlParsesAsValidXml()
    {
        foreach (var (file, text) in ViewXamls())
        {
            var ex = Record.Exception(() => XDocument.Parse(text));
            Assert.True(ex == null, $"{file} 不是合法 XML：{ex?.Message}");
        }
    }

    // ================= 对话框尺寸不变量 =================

    /// <summary>
    /// 对话框不能写死 Height。
    ///
    /// 写死高度在字段增删或文案变长后会把底部按钮挤出窗口底边，
    /// 表现是「确定」只露出一截 —— 用户点不到，弹窗等于卡死。
    /// 正确做法是 SizeToContent="Height" 由内容决定高度。
    /// </summary>
    [Fact]
    public void DialogsDoNotHardcodeHeight()
    {
        var offenders = new List<string>();

        foreach (var (file, text) in ViewXamls())
        {
            // 只在 Window 根标签的属性区里找 Height="数字"
            var rootTag = Regex.Match(text, @"<Window\b[^>]*>", RegexOptions.Singleline);
            if (!rootTag.Success) continue;

            var hardcoded = Regex.Match(rootTag.Value, @"(?<![\w.])Height\s*=\s*""[\d.]+""");
            if (hardcoded.Success)
                offenders.Add($"{file}: {hardcoded.Value}");
        }

        Assert.True(offenders.Count == 0,
            "以下对话框写死了高度，内容变高时会裁掉底部按钮：" +
            "\n  " + string.Join("\n  ", offenders) +
            "\n请改用 SizeToContent=\"Height\"。");
    }

    /// <summary>
    /// 按钮不能被包在 ScrollViewer 里。
    ///
    /// 若 ScrollViewer 包住含按钮的整块内容，内容一超高按钮就跟着滚出
    /// 可视区，用户无法确认。正确结构是"可滚动内容 + 固定底部按钮"。
    /// </summary>
    [Fact]
    public void DialogButtonsAreNotInsideScrollViewer()
    {
        var offenders = new List<string>();

        foreach (var (file, text) in ViewXamls())
        {
            var sv = Regex.Match(text, @"<ScrollViewer\b[^>]*>(?<inner>[\s\S]*?)</ScrollViewer>",
                RegexOptions.Singleline);
            if (!sv.Success) continue;

            // 在 ScrollViewer 内部找 IsDefault / IsCancel 按钮 ——
            // 这两个标记出现在对话框的主按钮上
            var inner = sv.Groups["inner"].Value;
            if (Regex.IsMatch(inner, @"<Button\b[^>]*IsDefault\s*=\s*""True""") ||
                Regex.IsMatch(inner, @"<Button\b[^>]*IsCancel\s*=\s*""True"""))
            {
                offenders.Add(file);
            }
        }

        Assert.True(offenders.Count == 0,
            "以下对话框把主按钮放在了 ScrollViewer 内部，内容超高时按钮会被滚走：" +
            "\n  " + string.Join("\n  ", offenders) +
            "\n请把按钮移到 ScrollViewer 外并固定在底部。");
    }

    /// <summary>
    /// 对话框根元素不能是 StackPanel。
    ///
    /// 垂直 StackPanel 以"无限宽"测量子项，内层 Grid 的 * 列会展开到
    /// 内容自然宽度而不收缩，导致内容横向溢出。根元素应为 Grid 或 Border。
    /// </summary>
    [Fact]
    public void DialogRootIsNotStackPanel()
    {
        var offenders = new List<string>();

        foreach (var (file, text) in ViewXamls())
        {
            // 取 Window 标签之后的第一个元素开始标签
            var rootTag = Regex.Match(text, @"<Window\b[^>]*>\s*(?<first><\w+)", RegexOptions.Singleline);
            if (!rootTag.Success) continue;

            if (rootTag.Groups["first"].Value.Contains("StackPanel"))
                offenders.Add(file);
        }

        Assert.True(offenders.Count == 0,
            "以下对话框以 StackPanel 为根元素，内容会被无限宽测量而横向溢出：" +
            "\n  " + string.Join("\n  ", offenders) +
            "\n请改用 Grid。");
    }
}
