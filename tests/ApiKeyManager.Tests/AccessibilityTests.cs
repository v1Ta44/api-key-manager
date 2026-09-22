using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace ApiKeyManager.Tests;

public sealed class AccessibilityTests
{
    private static string Root
    {
        get { var dir = new DirectoryInfo(AppContext.BaseDirectory); while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ApiKeyManager.sln"))) dir = dir.Parent; return dir!.FullName; }
    }
    private static string View => Path.Combine(Root, "src/ApiKeyManager.Wpf/Workspace/WorkspaceWindow.xaml");
    private static XDocument Document => XDocument.Load(View);
    [Fact]
    public void AllInputsHaveAccessibleNames()
    {
        var inputs = Document.Descendants().Where(e => e.Name.LocalName is "TextBox" or "PasswordBox" or "ListBox").ToArray();
        Assert.NotEmpty(inputs);
        Assert.All(inputs, e => Assert.False(string.IsNullOrWhiteSpace(e.Attribute("AutomationProperties.Name")?.Value)));
    }
    [Fact]
    public void ActionsHaveVisibleOrAccessibleNames()
    {
        Assert.All(Document.Descendants().Where(e => e.Name.LocalName == "Button"), e =>
            Assert.True(e.Attribute("Content") != null || e.Attribute("AutomationProperties.Name") != null));
    }
    [Fact]
    public void NoInteractiveControlIsRemovedFromTabOrder() => Assert.DoesNotContain("IsTabStop=\"False\"", File.ReadAllText(View));
    [Fact]
    public void ProductionShortcutsIncludeFindAddLockTheme()
    {
        var code = File.ReadAllText(View + ".cs");
        Assert.Contains("ApplicationCommands.Find", code);
        foreach (var key in new[] { "F", "N", "L", "T" }) Assert.Contains("Key." + key, code);
        Assert.Contains("ModifierKeys.Control", code);
    }
    [Fact]
    public void SensitivePanelsHaveCyclicTabNavigation()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var name in new[] { "LockScreen", "EditorOverlay", "ManagementOverlay" })
            Assert.Equal("Cycle", Document.Descendants().Single(e => e.Attribute(x + "Name")?.Value == name).Attribute("KeyboardNavigation.TabNavigation")?.Value);
    }
    [Fact]
    public void SubmitActionsAreOutsideScrollingContent()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var name in new[] { "SaveEditButton", "SubmitManagement", "UnlockButton" })
            Assert.DoesNotContain(Document.Descendants().Single(e => e.Attribute(x + "Name")?.Value == name).Ancestors(), e => e.Name.LocalName == "ScrollViewer");
    }
    [Fact]
    public void ListsAreVirtualizedAndSecretsUsePasswordControls()
    {
        var text = File.ReadAllText(View);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", text);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var name in new[] { "EditKey", "UnlockPassword", "CurrentPassword", "OperationPassword", "OperationConfirmation" })
            Assert.Equal("PasswordBox", Document.Descendants().Single(e => e.Attribute(x + "Name")?.Value == name).Name.LocalName);
    }
}
