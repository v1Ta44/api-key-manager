using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ApiKeyManager.Wpf.Services;
using ApiKeyManager.Wpf.Themes;
using ApiKeyManager.Wpf.ViewModels;

namespace ApiKeyManager.Wpf.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly WpfDialogService _dialogs;

    public MainWindow()
    {
        InitializeComponent();

        _dialogs = new WpfDialogService(this);
        _vm = new MainViewModel(_dialogs);
        DataContext = _vm;

        Loaded += OnLoaded;
        Closing += OnClosing;

        // 主题切换后刷新那些"取过一次就缓存"的绑定（主题按钮文字）
        ThemeManager.ThemeChanged += OnThemeChanged;

        // 键盘快捷键
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Find, (_, _) => FocusSearch()));
        InputBindings.Add(new KeyBinding(_vm.LockCommand, Key.L, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.AddCommand, Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => _vm.ToggleThemeCommand.Execute(null)),
            Key.T, ModifierKeys.Control));
    }

    private void FocusSearch()
    {
        // 在可视树里找搜索框（它没有 x:Name，避免与绑定命名冲突）
        var box = FindDescendant<TextBox>(this, tb => tb.Style == (Style)FindResource("TextBoxSearch"));
        box?.Focus();
        box?.SelectAll();
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t && predicate(t)) return t;
            var found = FindDescendant(child, predicate);
            if (found != null) return found;
        }
        return null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 命令行开关留给阶段4；这里只处理常规启动
        _vm.Start();

        // 启动流程可能已经解锁（首次创建密码），也可能需要弹解锁框
        if (_vm.IsLocked) _vm.Unlock();
    }

    private void OnThemeChanged(bool isDark)
    {
        // ThemeLabel 是计算属性，切换后需要手动通知一次
        _vm.Raise(nameof(MainViewModel.ThemeLabel));
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _vm.Shutdown();
        ThemeManager.ThemeChanged -= OnThemeChanged;
    }
}
