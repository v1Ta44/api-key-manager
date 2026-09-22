using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ApiKeyManager;

namespace ApiKeyManager.Wpf.Converters;

/// <summary>
/// 到期状态 → 颜色令牌名 → 实际画刷。
///
/// 关键点：转换器返回的是从 DynamicResource 取出的画刷，
/// 但 WPF 的转换器在绑定求值时是"一次性"的 —— 直接用 FindResource 拿到的画刷
/// 在主题切换后不会自动更新。
///
/// 因此这里返回的是 <see cref="Brush"/> 实例，且调用方必须让绑定在主题切换后重新求值。
/// 更稳妥的做法（本类采用前半段、XAML 采用后半段）：
///   转换器只负责判定"状态枚举"，颜色交由 XAML 的 DataTrigger + DynamicResource 决定。
/// 本转换器仅用于必须用代码决定的少量场景，并在内部处理主题重取。
/// </summary>
public sealed class ExpiryBrushConverter : IValueConverter
{
    /// <summary>返回 true 时输出"文本色"，否则输出"背景色"。</summary>
    public bool AsText { get; set; } = true;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var state = value is ExpiryState s ? s : ExpiryState.None;

        string key = state switch
        {
            ExpiryState.Expired => AsText ? "ExpiryExpiredText" : "ExpiryExpired",
            ExpiryState.ExpiringSoon => AsText ? "ExpirySoonText" : "ExpirySoon",
            ExpiryState.Valid => AsText ? "ExpiryValidText" : "ExpiryValid",
            _ => AsText ? "TextDim" : "ExpiryNone",
        };

        return Resolve(key) ?? Brushes.Gray;
    }

    private static Brush? Resolve(string key)
    {
        var app = Application.Current;
        if (app == null) return null;

        // 主题切换后需要重新取；TryFindResource 会沿当前合并字典查找
        return app.TryFindResource(key) as Brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>把 bool 取反（用于"锁定/解锁"两个互斥面板的 Visibility）。</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;
}

/// <summary>bool → Visibility（true 显示）。可传 "invert" 反转。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is bool v && v;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase)) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility vis && vis == Visibility.Visible;
}

/// <summary>集合/数量为 0 → Visible（用于空列表提示）。可传 "invert"。</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int n = value switch
        {
            int i => i,
            System.Collections.ICollection c => c.Count,
            null => 0,
            _ => 1,
        };

        bool isZero = n == 0;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase)) isZero = !isZero;
        return isZero ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
