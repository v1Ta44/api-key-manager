using System.Xml.Linq;
using Xunit;

namespace ApiKeyManager.Tests;

public sealed class ThemeParityTests
{
    private static string Themes
    {
        get { var dir = new DirectoryInfo(AppContext.BaseDirectory); while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ApiKeyManager.sln"))) dir = dir.Parent; return Path.Combine(dir!.FullName, "src/ApiKeyManager.Wpf/Themes"); }
    }
    private static Dictionary<string, string> Palette(string name)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(Path.Combine(Themes, name + ".xaml")).Root!.Elements().ToDictionary(e => e.Attribute(x + "Key")!.Value, e => e.Attribute("Color")!.Value);
    }
    [Fact]
    public void LightAndDarkDefineAllSemanticColors()
    {
        var light = Palette("Light"); var dark = Palette("Dark");
        Assert.Equal(light.Keys.Order(), dark.Keys.Order());
        foreach (var key in new[] { "PCanvas", "PSurface", "PText", "PMuted", "PAccent", "POnAccent", "PBorder", "PDanger", "PWarning", "POverlay" }) Assert.Contains(key, light.Keys);
    }
    [Fact]
    public void NoColorAndStyleKeysCollide()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var styles = XDocument.Load(Path.Combine(Themes, "WorkspaceStyles.xaml")).Descendants().Where(e => e.Name.LocalName == "Style").Select(e => e.Attribute(x + "Key")?.Value);
        Assert.Empty(styles.Intersect(Palette("Light").Keys));
    }
    [Fact]
    public void BodyTextMeetsContrastRequirementInBothThemes()
    {
        static double Luminance(string hex)
        {
            var values = new[] { 1, 3, 5 }.Select(i => Convert.ToInt32(hex.Substring(i, 2), 16) / 255d).Select(v => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4)).ToArray();
            return values[0] * .2126 + values[1] * .7152 + values[2] * .0722;
        }
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var colors = Palette(theme);
            foreach (var pair in new[] { ("PText", "PCanvas"), ("PMuted", "PSurface"), ("PMuted", "PCanvas"), ("POnAccent", "PAccent"), ("PWarning", "PSurface"), ("PDanger", "PSurface") })
            {
                var a = Luminance(colors[pair.Item1]); var b = Luminance(colors[pair.Item2]);
                Assert.True((Math.Max(a, b) + .05) / (Math.Min(a, b) + .05) >= 4.5, theme + " " + pair);
            }
        }
    }
}
