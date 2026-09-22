using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using ApiKeyManager.Wpf.Themes;


namespace ApiKeyManager.Wpf;

public partial class App : System.Windows.Application
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

    protected override async void OnStartup(StartupEventArgs e)
    {
        Args = e.Args ?? Array.Empty<string>();

        // 原型必须在所有 DataPaths / Settings / Vault 操作前分流。
        if (Has("--prototype-check"))
        {
            AttachConsoleForTool();
            Shutdown(Prototype.PrototypeChecks.Run(Value("--prototype-check")));
            return;
        }
        if (Has("--prototype"))
        {
            base.OnStartup(e);
            var prototype = new Prototype.PrototypeWindow(Has("--dark"));
            MainWindow = prototype;
            prototype.Show();
            return;
        }

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

        if (Has("--workspace-check") || Has("--layoutcheck") || Has("--dialogcheck"))
        {
            AttachConsoleForTool();
            Shutdown(await Workspace.WorkspaceChecks.Run(Value("--workspace-check") ?? Path.Combine(Path.GetTempPath(), "akm-workspace-check-" + Guid.NewGuid().ToString("N"))));
            return;
        }

        base.OnStartup(e);
        try
        {
            string? dir = Value("--dir");
            if (Has("--demo")) dir = MakeDemoDir(dir);
            if (dir != null) _directoryScope = DataPaths.UseDirectory(dir);
            var paths = DataPaths.Resolve();
            _lease = new ApiKeyManager.Application.VaultLease(paths.VaultPath);
            var settings = SettingsStore.Load(paths.DataDir);
            if (Has("--dark")) settings.Theme = "Dark";
            var clipboard = new Services.ClipboardService();
            var model = new ViewModels.WorkspaceViewModel(
                new ApiKeyManager.Application.VaultSession(new ApiKeyManager.Application.FileVaultStorage(paths.VaultPath)),
                settings, next => SettingsStore.TrySave(paths.DataDir, next), clipboard.SetText);
            var window = new Workspace.WorkspaceWindow(model, paths.VaultPath, clipboard, Has("--demo"));
            MainWindow = window;
            window.Show();
        }
        catch (Exception)
        {
            MessageBox.Show("无法打开本地空间。可能已有窗口正在使用同一个库，或目录不可访问。请关闭同库窗口，或检查目录权限后重试。", "无法打开空间", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
        }
    }

    private IDisposable? _directoryScope;
    private IDisposable? _lease;
    protected override void OnExit(ExitEventArgs e)
    {
        _lease?.Dispose(); _directoryScope?.Dispose();
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
    /// <summary>
    /// 造一个演示数据目录。
    /// preferred 非空时使用它（来自 --dir），这样调用方知道数据落在哪里；
    /// 否则在系统临时目录下新建一个随机目录。
    /// </summary>
    private static string MakeDemoDir(string? preferred = null)
    {
        string dir = string.IsNullOrWhiteSpace(preferred)
            ? Path.Combine(Path.GetTempPath(), "akm-demo-" + Guid.NewGuid().ToString("N")[..8])
            : preferred;
        Directory.CreateDirectory(dir);
        using var demoLease = new ApiKeyManager.Application.VaultLease(Path.Combine(dir, "vault.akv"));
        if (File.Exists(Path.Combine(dir, "vault.akv")) || File.Exists(Path.Combine(dir, "settings.json")))
            throw new IOException("演示目录已经有库文件，不能覆盖。请选择空目录。");

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

}
