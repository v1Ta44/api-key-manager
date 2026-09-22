using System;
using System.Windows;

namespace ApiKeyManager.Wpf.Themes;

/// <summary>
/// 主题管理器 —— 取代 WinForms 版的 Theme.Current 静态字段 + RebuildUi() 重建控件树。
///
/// 核心机制：只替换 MergedDictionaries 里"那一份主题字典"，
/// 视觉树保持不变。因为所有控件都用 DynamicResource 引用颜色，
/// 字典一换，WPF 会通知全部绑定重新取值 —— 没有重建、没有闪烁、不丢状态。
///
/// 关键约束：Light.xaml 与 Dark.xaml 必须提供完全相同的 key 集合。
/// 少一个 key 的后果是该控件静默丢掉颜色（不报错），所以有 ThemeKeyParityTests 兜底。
/// </summary>
public static class ThemeManager
{
    /// <summary>主题字典在 MergedDictionaries 中的固定位置（由 App.xaml 决定）。</summary>
    private const int ThemeDictionaryIndex = 1;

    private const string LightSource = "Themes/Light.xaml";
    private const string DarkSource = "Themes/Dark.xaml";

    /// <summary>当前是否为深色主题。</summary>
    public static bool IsDark { get; private set; }

    /// <summary>主题切换完成时触发（参数为切换后的 IsDark）。</summary>
    public static event Action<bool>? ThemeChanged;

    /// <summary>
    /// 应用主题。<paramref name="dark"/> 为 true 用深色。
    /// 若主题未变化则直接返回（幂等）。
    /// </summary>
    public static void Apply(bool dark)
    {
        if (IsDark == dark && AlreadyLoaded(dark)) return;

        var app = Application.Current;
        if (app == null) return;

        var merged = app.Resources.MergedDictionaries;
        if (merged.Count <= ThemeDictionaryIndex) return;

        var dict = new ResourceDictionary
        {
            Source = new Uri(dark ? DarkSource : LightSource, UriKind.Relative)
        };

        // 只换这一份，其余（Metrics / Controls）原样保留
        merged[ThemeDictionaryIndex] = dict;

        IsDark = dark;
        ThemeChanged?.Invoke(dark);
    }

    /// <summary>按字符串名称应用（"Dark" / 其它视为浅色），用于读取 settings.json。</summary>
    public static void ApplyByName(string? name) =>
        Apply(string.Equals(name, "Dark", StringComparison.OrdinalIgnoreCase));

    /// <summary>在浅色与深色之间切换。</summary>
    public static void Toggle() => Apply(!IsDark);

    /// <summary>当前主题名（写回 settings.json 用）。</summary>
    public static string CurrentName => IsDark ? "Dark" : "Light";

    /// <summary>
    /// 判断当前已加载的是否就是目标主题。
    /// 用于避免 App 启动时重复加载（App.xaml 已默认载入 Light）。
    /// </summary>
    private static bool AlreadyLoaded(bool dark)
    {
        var app = Application.Current;
        if (app == null) return false;

        var merged = app.Resources.MergedDictionaries;
        if (merged.Count <= ThemeDictionaryIndex) return false;

        string? src = merged[ThemeDictionaryIndex].Source?.OriginalString;
        if (src == null) return false;

        return src.EndsWith(dark ? "Dark.xaml" : "Light.xaml", StringComparison.OrdinalIgnoreCase);
    }
}
