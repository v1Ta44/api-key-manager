using System.Windows;
using System.Windows.Media;

namespace ApiKeyManager.Wpf.Themes;

internal static class WorkspacePalette
{
    private static readonly string[] Keys =
    ["PCanvas", "PSurface", "PSubtle", "PBorder", "PText", "PMuted", "PAccent", "PAccentHover",
     "PAccentSoft", "POnAccent", "PSuccess", "PSuccessSoft", "PWarning", "PWarningSoft", "PDanger", "PDangerSoft", "POverlay"];

    public static void Apply(ResourceDictionary resources, bool dark, bool warm)
    {
        if (warm)
        {
            var palette = new ResourceDictionary { Source = new Uri($"/ApiKeyManager;component/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative) };
            foreach (var key in Keys) resources[key] = palette[key];
            return;
        }
        string[] colors = (dark, warm) switch
        {
            (false, false) => ["#F6F8FC", "#FFFFFF", "#F0F3F8", "#DFE5EE", "#17243B", "#5C6B82", "#285CE5", "#1747CA", "#EAF0FF", "#FFFFFF", "#257653", "#E9F5EF", "#956010", "#FFF4DF", "#B84242", "#FCECEC", "#70212E47"],
            (true, false) => ["#141923", "#1C2330", "#252E3E", "#343F51", "#EBF0FA", "#A6B3C8", "#9AB9FF", "#BDCEFF", "#283D63", "#142442", "#7AD8AF", "#213E36", "#F1C879", "#463922", "#FFAAAA", "#4A2D35", "#B0080C14"],
            (false, true) => ["#F5F3ED", "#FFFEFA", "#EDEDE5", "#DDDFD3", "#28392F", "#657063", "#37644D", "#254C39", "#E5EDE3", "#FFFFFF", "#37644D", "#E5EDE3", "#8F601C", "#F6EED9", "#AD493C", "#F8E9E2", "#70404135"],
            _ => ["#1C211C", "#252C25", "#303A30", "#414C40", "#F0F1E8", "#B0BAAB", "#B4D1AD", "#D0E7C8", "#344A38", "#223322", "#B4D1AD", "#344A38", "#E5C17C", "#493C28", "#F0A89A", "#4D342E", "#B00D130E"]
        };
        for (var i = 0; i < Keys.Length; i++)
            resources[Keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
    }
}
