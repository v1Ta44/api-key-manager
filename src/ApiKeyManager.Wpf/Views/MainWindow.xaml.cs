using System.Windows;
using ApiKeyManager.Wpf.Themes;

namespace ApiKeyManager.Wpf.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 阶段1 临时接线：验证主题切换机制（阶段2 会由 MainViewModel 接管）
        BtnTheme.Click += (_, _) =>
        {
            ThemeManager.Toggle();
            BtnTheme.Content = ThemeManager.IsDark ? "浅色" : "深色";
        };

        ThemeManager.ThemeChanged += dark =>
            BtnTheme.Content = dark ? "浅色" : "深色";
    }
}
