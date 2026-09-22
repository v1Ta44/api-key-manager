using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ApiKeyManager.Wpf;

/// <summary>
/// 图标字形目录。
///
/// 与 WinForms 版把字形写在 Theme.cs 常量里不同，这里直接从 XAML 源码扫描
/// 实际用到的 <c>&amp;#xE7xx;</c> 转义 —— 这样"检查字形是否存在于字体中"
/// 校验的就是真正会渲染的东西，而不是一份可能与界面脱节的常量表。
/// </summary>
internal static class GlyphCatalog
{
    /// <summary>语义名称，仅用于输出可读性。</summary>
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["E710"] = "Add（新增）",
        ["E70F"] = "Edit（编辑）",
        ["E74D"] = "Delete（删除）",
        ["E8C8"] = "Copy（复制）",
        ["E774"] = "Link（链接）",
        ["E890"] = "View（查看）",
        ["E896"] = "Import（导入）",
        ["E898"] = "Export（导出）",
        ["E8A5"] = "File（文件）",
        ["E713"] = "Settings（设置）",
        ["E72E"] = "Lock（锁）",
        ["E721"] = "Search（搜索）",
        ["E706"] = "Sun（浅色）",
        ["E708"] = "Moon（深色）",
        ["E73E"] = "CheckMark（勾选）",
        ["E70D"] = "ChevronDown（下拉）",
        ["E76C"] = "ChevronRight（右箭头）",
        ["E8B7"] = "Folder（文件夹）",
        ["E8E5"] = "Warning（警告）",
        ["EA39"] = "Clock（时间）",
        ["E7BA"] = "WarningAlt（提示）",
        ["E930"] = "Key（密钥）",
    };

    /// <summary>
    /// 扫描仓库中所有 XAML，提取用到的 Segoe MDL2 字形码位。
    /// </summary>
    public static IReadOnlyList<(string Code, string Name)> ScanUsedGlyphs()
    {
        string? root = FindRepoRoot();
        if (root == null) return Array.Empty<(string, string)>();

        string viewsDir = Path.Combine(root, "src", "ApiKeyManager.Wpf");
        if (!Directory.Exists(viewsDir)) return Array.Empty<(string, string)>();

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.GetFiles(viewsDir, "*.xaml", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);

            // 只扫描声明了 Segoe MDL2 Assets 的 TextBlock 所在文件里的转义：
            // 保守起见，全部扫描也没有误报风险（其它转义不是私用区码位）。
            foreach (Match m in Regex.Matches(text, "&#x([0-9A-Fa-f]{4,5});"))
            {
                string code = m.Groups[1].Value.ToUpperInvariant();

                // 私用区 E000–F8FF 才是图标字体
                if (int.TryParse(code, System.Globalization.NumberStyles.HexNumber, null, out int cp)
                    && cp >= 0xE000 && cp <= 0xF8FF)
                {
                    found.Add(code);
                }
            }
        }

        return found
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Select(c => (c, Known.TryGetValue(c, out var n) ? n : "（未登记）"))
            .ToList();
    }

    public static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ApiKeyManager.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>Segoe MDL2 Assets 字体路径（Win10+ 系统自带）。</summary>
    public static string FontPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segmdl2.ttf");

    /// <summary>
    /// 检查界面用到的所有字形在字体里是否都存在。
    /// 返回缺失数量；字体文件本身不存在返回 -1。
    /// </summary>
    public static int CheckAll(Action<string>? report = null)
    {
        report ??= _ => { };

        string fontPath = FontPath;
        if (!File.Exists(fontPath))
        {
            report("找不到图标字体：" + fontPath);
            report("这是 Windows 10 及以上系统自带字体；缺失会导致所有图标显示为方块。");
            return -1;
        }

        byte[] font = File.ReadAllBytes(fontPath);
        var used = ScanUsedGlyphs();

        report($"扫描到 {used.Count} 个字形（来源：src/ApiKeyManager.Wpf/**/*.xaml）");

        int missing = 0;
        foreach (var (code, name) in used)
        {
            int cp = int.Parse(code, System.Globalization.NumberStyles.HexNumber);
            bool ok = TtfGlyphChecker.HasGlyph(font, cp);
            report($"  U+{code}  {name,-18}  {(ok ? "OK" : "MISSING")}");
            if (!ok) missing++;
        }

        // 顺带检查字体本身是否可用（防止字体损坏但文件存在）
        if (used.Count == 0)
        {
            report("警告：没有扫描到任何字形，可能路径不对。");
            return -1;
        }

        report(missing == 0 ? "全部字形可用。" : $"缺失 {missing} 个字形。");
        return missing;
    }
}
