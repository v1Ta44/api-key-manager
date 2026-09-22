using System;
using System.Windows;
using ApiKeyManager.Wpf.Views;

namespace ApiKeyManager.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 阶段2 待接：--selftest / --makeicon / --glyphcheck / --demo / --dark 命令行分流

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
