using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using ApiKeyManager.Wpf.Themes;
using ApiKeyManager.Wpf.Views;

namespace ApiKeyManager.Wpf;

public partial class App : Application
{
    /// <summary>
    /// WPF 应用默认没有控制台窗口。从命令行运行 --selftest / --glyphcheck 等
    /// 工具时必须 attach 到父进程的控制台，否则看不到任何输出。
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    private const int AttachParentProcess = -1;

    private static void AttachConsoleForTool()
    {
        try { AttachConsole(AttachParentProcess); } catch { /* 非控制台启动时忽略 */ }
    }

    /// <summary>
    /// 命令行开关（与原 WinForms 版保持兼容）：
    ///   --selftest            无界面自检，退出码 0=全通过
    ///   --makeicon &lt;file&gt;   生成应用图标
    ///   --glyphcheck          检查图标字形
    ///   --demo                用临时目录 + 演示数据启动（不碰真实库）
    ///   --dark                以深色主题启动
    ///   --dir &lt;path&gt;         指定数据目录
    /// </summary>
    private static string[] Args = Array.Empty<string>();

    protected override void OnStartup(StartupEventArgs e)
    {
        Args = e.Args ?? Array.Empty<string>();

        // 无界面模式必须在创建任何窗口之前处理
        if (Has("--selftest"))
        {
            AttachConsoleForTool();
            Shutdown(SelfTest.Run());
            return;
        }

        if (Has("--makeicon"))
        {
            AttachConsoleForTool();
            Shutdown(RunMakeIcon());
            return;
        }

        if (Has("--glyphcheck"))
        {
            AttachConsoleForTool();
            Shutdown(RunGlyphCheck());
            return;
        }

        if (Has("--layoutcheck"))
        {
            AttachConsoleForTool();
            Shutdown(RunLayoutCheck());
            return;
        }

        // 数据目录重定向（--demo / --dir）必须在读取设置之前完成
        var scopes = new List<IDisposable>();
        string? dir = Value("--dir");
        if (Has("--demo")) dir = MakeDemoDir();
        if (dir != null) scopes.Add(DataPaths.UseDirectory(dir));

        if (Has("--dark")) ThemeManager.Apply(true);

        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
    }

    // ---------------- 命令行解析 ----------------

    private static bool Has(string flag) =>
        Args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static string? Value(string flag)
    {
        for (int i = 0; i < Args.Length - 1; i++)
        {
            if (string.Equals(Args[i], flag, StringComparison.OrdinalIgnoreCase))
                return Args[i + 1];
        }
        return null;
    }

    // ---------------- 演示数据 ----------------

    /// <summary>
    /// 建一个临时数据目录并写入演示库。用于人工验收 UI，
    /// 绝不会碰到用户真实的 vault。目录路径会打印到控制台。
    /// </summary>
    private static string MakeDemoDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "akm-demo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);

        const string demoPassword = "demo";

        var data = new VaultData
        {
            Entries = new List<ApiEntry>
            {
                new() { Name = "OpenAI 生产", Provider = "OpenAI", ApiKey = "sk-proj-9f2Ka8Lm3Qw7Zx1Vb6Nc4Rd0Tg5Hy",
                        BaseUrl = "https://api.openai.com/v1", Model = "gpt-4o",
                        Tags = "ai,官方,生产", Notes = "计费账号，额度充足",
                        ExpiresUtc = DateTime.Today.AddDays(45), UpdatedUtc = DateTime.UtcNow.AddHours(-3) },

                new() { Name = "DeepSeek 测试", Provider = "DeepSeek", ApiKey = "sk-deep-7Ha2Kd9Lm4Qp8Xz",
                        BaseUrl = "https://api.deepseek.com", Model = "deepseek-chat",
                        Tags = "ai,便宜", Notes = "日常调试主力",
                        ExpiresUtc = DateTime.Today.AddDays(7), UpdatedUtc = DateTime.UtcNow.AddDays(-1) },

                new() { Name = "Claude 研究", Provider = "Anthropic", ApiKey = "sk-ant-api03-Kx8Lm2Nq4Rt6Vw9Yz",
                        BaseUrl = "https://api.anthropic.com", Model = "claude-sonnet-4-5",
                        Tags = "ai,长文本", Notes = "长上下文实验用",
                        ExpiresUtc = DateTime.Today.AddDays(-4), UpdatedUtc = DateTime.UtcNow.AddDays(-6) },

                new() { Name = "内部网关", Provider = "自建", ApiKey = "gw-live-3Fk9Pa2Lm7Qd4Zx8",
                        BaseUrl = "https://gateway.internal.corp/v2", Model = "qwen-max",
                        Tags = "内网,生产,计费,重要", Notes = "所有内部服务的统一入口",
                        ExpiresUtc = null, UpdatedUtc = DateTime.UtcNow.AddMinutes(-40) },

                new() { Name = "Gemini 备用", Provider = "Google", ApiKey = "AIzaSyD-4Lm9Nq2Rt5Vw8Yz1",
                        BaseUrl = "https://generativelanguage.googleapis.com", Model = "gemini-2.5-pro",
                        Tags = "ai", Notes = "",
                        ExpiresUtc = DateTime.Today.AddDays(120), UpdatedUtc = DateTime.UtcNow.AddDays(-12) },

                new() { Name = "本地 Ollama", Provider = "本地", ApiKey = "ollama",
                        BaseUrl = "http://127.0.0.1:11434/v1", Model = "llama3.1",
                        Tags = "本地,免费", Notes = "无需联网",
                        ExpiresUtc = null, UpdatedUtc = DateTime.UtcNow.AddDays(-30) },
            }
        };

        VaultStore.Save(Path.Combine(dir, "vault.akv"), data, demoPassword, 100_000);

        var settings = new AppSettings
        {
            AutoLockMinutes = 5,
            ClipboardClearSeconds = 30,
            ShowKeys = false,
            Theme = Has("--dark") ? "Dark" : "Light",
        };
        SettingsStore.Save(dir, settings);

        try
        {
            Console.WriteLine("演示数据目录：" + dir);
            Console.WriteLine("演示主密码：  " + demoPassword);
        }
        catch { /* 无控制台时忽略 */ }

        return dir;
    }

    // ---------------- 构建工具 ----------------

    private static int RunMakeIcon()
    {
        try
        {
            string outPath = Value("--makeicon") ?? "icon.ico";
            IconFactory.Write(outPath);
            try { Console.WriteLine("图标已生成：" + Path.GetFullPath(outPath)); } catch { }
            return 0;
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine("生成图标失败：" + ex.Message); } catch { }
            return 1;
        }
    }

    private static int RunGlyphCheck()
    {
        void Report(string line)
        {
            try { Console.WriteLine(line); } catch { }
        }

        int missing = GlyphCatalog.CheckAll(Report);
        return missing == 0 ? 0 : missing < 0 ? 2 : 1;
    }

    private static readonly List<string> _layoutLog = new();

    /// <summary>
    /// 布局自检：创建主窗口并在真实排版后打印关键元素的实测尺寸。
    /// 用来回答"内容到底需要多宽 / 哪一列被裁"，而不是靠截图猜。
    ///
    /// 注意：不调用 Show()（那会弹解锁框并阻塞）。用 Measure/Arrange
    /// 在离屏状态下强制一次真实排版，拿到的 ActualWidth 同样是可靠的。
    /// </summary>
    private static int RunLayoutCheck()
    {
        void Say(string line)
        {
            try { Console.WriteLine(line); } catch { }
            try { _layoutLog.Add(line); } catch { }
        }

        var window = new MainWindow();

        // Window 本身在未 Show 时不会排版内容，因此直接对它的内容根排版。
        // 客户区尺寸 = 窗口尺寸减去标题栏与边框（用 SystemParameters 估）：
        //   标题栏约 30 DIP，左右边框各 1 DIP
        double clientW = window.Width - 2;
        double clientH = window.Height - 39;

        if (window.Content is System.Windows.FrameworkElement root)
        {
            root.Measure(new System.Windows.Size(clientW, clientH));
            root.Arrange(new System.Windows.Rect(0, 0, clientW, clientH));
            root.UpdateLayout();
            Say($"内容根 DesiredWidth={root.DesiredSize.Width:N1}  ActualWidth={root.ActualWidth:N1}");
        }
        else
        {
            Say("内容根未找到");
        }

        Say("=== 布局实测 ===");
        Say($"窗口 Width={window.Width}  Height={window.Height}");
        Say($"假设客户区 {clientW:N0} x {clientH:N0}");

        if (window.FindName("EntryList") is System.Windows.FrameworkElement list)
        {
            Say($"EntryList ActualWidth={list.ActualWidth:N1}  DesiredWidth={list.DesiredSize.Width:N1}");
        }
        else
        {
            Say("EntryList 未找到");
        }

        var found = new List<(string Path, double W)>();
        if (window.Content is System.Windows.DependencyObject croot) Walk(croot, "", found);

        Say("");
        Say("=== 表格 Grid 列宽实测 ===");
        foreach (var (path, w) in found.OrderByDescending(x => x.W).Take(10))
        {
            Say($"  {w,9:N1}  {path}");
        }

        window.Close();

        // WPF 应用可能没有可用的 stdout，结果同时落盘，确保一定拿得到
        try
        {
            string outPath = Path.Combine(Path.GetTempPath(), "akm-layoutcheck.txt");
            File.WriteAllText(outPath, string.Join(Environment.NewLine, _layoutLog));
        }
        catch { }

        return 0;
    }

    private static void Walk(System.Windows.DependencyObject root, string path,
                             List<(string, double)> sink)
    {
        if (root is System.Windows.Controls.Grid g && g.ColumnDefinitions.Count >= 4)
        {
            double sum = 0;
            var widths = new List<string>();
            foreach (var c in g.ColumnDefinitions)
            {
                sum += c.ActualWidth;
                widths.Add(c.ActualWidth.ToString("N0"));
            }
            sink.Add(($"cols={g.ColumnDefinitions.Count} sum={sum:N0}  [{string.Join(",", widths)}]", sum));
        }

        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            Walk(System.Windows.Media.VisualTreeHelper.GetChild(root, i), path + "/" + i, sink);
        }
    }
}
