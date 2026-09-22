using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ApiKeyManager;

internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    private const int AttachParentProcess = -1;

    /// <summary>--preview：演示模式下自动打开示例编辑对话框（界面预览用）。</summary>
    internal static bool PreviewDialogs;

    [STAThread]
    private static int Main(string[] args)
    {
        PreviewDialogs = args.Any(a => string.Equals(a, "--preview", StringComparison.OrdinalIgnoreCase));

        if (args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            AttachConsole(AttachParentProcess);
            return SelfTest.Run();
        }

        string? iconOut = GetArgValue(args, "--makeicon");
        if (iconOut != null)
        {
            AttachConsole(AttachParentProcess);
            IconFactory.Write(iconOut);
            Console.WriteLine("icon written: " + Path.GetFullPath(iconOut));
            return 0;
        }

        if (args.Any(a => string.Equals(a, "--glyphcheck", StringComparison.OrdinalIgnoreCase)))
        {
            AttachConsole(AttachParentProcess);
            return GlyphCheck();
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(new Font("Segoe UI", 10F));

        // 注意顺序：--demo 会通过 DataPaths.UseDirectory 切换数据目录，
        // 因此所有依赖 DataPaths.Resolve() 的逻辑都必须放在它之后，
        // 否则会把演示目录的设置写进真实数据目录（或反之）。
        string? autoPassword = null;
        if (args.Any(a => string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase)))
        {
            Demo.Prepare();
            autoPassword = Demo.Password;
        }

        if (args.Any(a => string.Equals(a, "--dark", StringComparison.OrdinalIgnoreCase)))
        {
            var (dir, _) = DataPaths.Resolve();
            var st = SettingsStore.Load(dir);
            st.Theme = "Dark";
            SettingsStore.Save(dir, st);
        }

        Application.Run(new MainForm(autoPassword));
        return 0;
    }

    private static string? GetArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static int GlyphCheck()
    {
        string fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segmdl2.ttf");
        if (!File.Exists(fontPath))
        {
            Console.WriteLine("segmdl2.ttf not found: " + fontPath);
            return 2;
        }
        byte[] font = File.ReadAllBytes(fontPath);
        var icons = new (string Name, string Glyph)[]
        {
            ("Add", Theme.IcoAdd), ("Edit", Theme.IcoEdit), ("Delete", Theme.IcoDelete),
            ("Copy", Theme.IcoCopy), ("Link", Theme.IcoLink), ("View", Theme.IcoView),
            ("Import", Theme.IcoImport), ("Export", Theme.IcoExport), ("File", Theme.IcoFile),
            ("Settings", Theme.IcoSettings), ("Lock", Theme.IcoLock), ("Search", Theme.IcoSearch),
            ("Sun", Theme.IcoSun), ("Moon", Theme.IcoMoon),
        };
        int missing = 0;
        foreach (var (name, glyph) in icons)
        {
            int cp = glyph[0];
            bool ok = TtfGlyphChecker.HasGlyph(font, cp);
            Console.WriteLine($"U+{cp:X4} {name,-8} {(ok ? "OK" : "MISSING")}");
            if (!ok) missing++;
        }
        Console.WriteLine(missing == 0 ? "ALL GLYPHS PRESENT" : $"{missing} GLYPH(S) MISSING");
        return missing == 0 ? 0 : 1;
    }
}
