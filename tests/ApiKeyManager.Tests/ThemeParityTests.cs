using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ApiKeyManager.Tests;

/// <summary>
/// 主题字典一致性校验。
///
/// 为什么需要：主题切换是整份替换 MergedDictionaries 里的主题字典，
/// 控件用 DynamicResource 引用颜色。如果 Dark.xaml 少了一个 Light.xaml 里有的 key，
/// WPF 不会报错 —— 那个控件会静默丢掉颜色（通常表现为透明或回落到默认黑色），
/// 而这类问题在浅色主题下完全看不出来。
///
/// 这里直接解析两份 XAML 的 x:Key，属于静态契约检查，不需要启动 WPF。
/// </summary>
public sealed class ThemeParityTests
{
    /// <summary>定位仓库里的 Themes 目录（从测试输出目录向上找到工程根）。</summary>
    private static string ThemesDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "src", "ApiKeyManager.Wpf", "Themes");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("未找到 src/ApiKeyManager.Wpf/Themes 目录");
        }
    }

    private static HashSet<string> KeysOf(string fileName)
    {
        string path = Path.Combine(ThemesDir, fileName);
        Assert.True(File.Exists(path), $"缺少主题字典文件：{path}");

        string text = File.ReadAllText(path);
        return Regex.Matches(text, "x:Key=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void LightAndDarkDefineExactlyTheSameKeys()
    {
        var light = KeysOf("Light.xaml");
        var dark = KeysOf("Dark.xaml");

        var missingInDark = light.Except(dark).OrderBy(k => k).ToList();
        var missingInLight = dark.Except(light).OrderBy(k => k).ToList();

        Assert.True(missingInDark.Count == 0,
            "Dark.xaml 缺少这些 key（切换深色时对应控件会丢失颜色）：" + string.Join(", ", missingInDark));
        Assert.True(missingInLight.Count == 0,
            "Light.xaml 缺少这些 key：" + string.Join(", ", missingInLight));
    }

    /// <summary>
    /// 主题字典里的 key 不能与 Controls.xaml 里的 Style key 撞名。
    ///
    /// 踩过的坑：曾经同时定义 &lt;SolidColorBrush x:Key="ChipText"&gt; 和
    /// &lt;Style x:Key="ChipText"&gt;，后者覆盖前者，导致
    /// Foreground="{DynamicResource ChipText}" 拿到一个 Style 对象并在
    /// 首次布局时抛 InvalidOperationException（窗口直接出不来，且异常信息晦涩）。
    /// </summary>
    [Fact]
    public void ThemeKeysDoNotCollideWithControlStyleKeys()
    {
        var themeKeys = KeysOf("Light.xaml").Concat(KeysOf("Dark.xaml")).ToHashSet(StringComparer.Ordinal);

        string controlsPath = Path.Combine(ThemesDir, "Controls.xaml");
        Assert.True(File.Exists(controlsPath));

        string controlsText = File.ReadAllText(controlsPath);
        var styleKeys = Regex.Matches(controlsText, "<Style\\s+x:Key=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value);

        var collisions = styleKeys.Where(themeKeys.Contains).OrderBy(k => k).ToList();

        Assert.True(collisions.Count == 0,
            "Controls.xaml 的 Style key 与主题 key 撞名（会导致运行时类型错误）：" + string.Join(", ", collisions));
    }

    [Fact]
    public void BothThemesDefineTheCoreSemanticBrushes()
    {
        // 应用启动就要用到的最小集合：缺任何一个都会导致首屏异常
        string[] required =
        {
            "Back", "Surface", "SurfaceAlt", "Border",
            "Text", "TextDim", "Accent", "AccentHover", "AccentPressed", "AccentText",
            "Danger", "Hover", "Pressed", "Selection", "FocusRing",
            "RowHover", "GridLine", "ChipBack", "ChipText", "PillBack", "PillText",
            "ExpiryExpired", "ExpirySoon", "ExpiryValid", "ExpiryNone",
            "ScrollThumb", "ScrollThumbHover",
        };

        foreach (string theme in new[] { "Light.xaml", "Dark.xaml" })
        {
            var keys = KeysOf(theme);
            var missing = required.Where(r => !keys.Contains(r)).ToList();
            Assert.True(missing.Count == 0, $"{theme} 缺少必要画刷：" + string.Join(", ", missing));
        }
    }

    [Fact]
    public void MetricsFileExistsAndDefinesCoreTokens()
    {
        string path = Path.Combine(ThemesDir, "Metrics.xaml");
        Assert.True(File.Exists(path));

        string text = File.ReadAllText(path);
        string[] required =
        {
            "Space1", "Space2", "Space3", "Space4", "Space5",
            "RadiusCard", "RadiusControl", "RadiusChip",
            "HeightControl", "HeightRow",
            "BorderThin", "BorderFocus",
            "WindowWidth", "WindowHeight",
        };

        foreach (string token in required)
        {
            Assert.Contains($"x:Key=\"{token}\"", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NoXamlUsesRawHexColorInsteadOfTokens()
    {
        // 视图层不应出现裸的 #RRGGBB —— 颜色必须走主题令牌，
        // 否则深色主题下会留下"浅色残留"。
        string viewsDir = Path.Combine(Path.GetDirectoryName(ThemesDir)!, "Views");
        if (!Directory.Exists(viewsDir)) return;

        var offenders = new List<string>();
        foreach (string file in Directory.GetFiles(viewsDir, "*.xaml"))
        {
            string text = File.ReadAllText(file);
            // 排除注释行，只查真正的属性赋值
            foreach (Match m in Regex.Matches(text, "(?:Background|Foreground|BorderBrush|Fill|Stroke)=\"(#[0-9A-Fa-f]{3,8})\""))
            {
                offenders.Add($"{Path.GetFileName(file)}: {m.Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "视图里出现裸颜色值，应改用主题令牌：" + string.Join("; ", offenders));
    }
}
