using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ApiKeyManager.Wpf.Workspace;

/// <summary>只向检查器自己的 HWND 发送消息；不驱动系统键鼠或其他应用。</summary>
internal static class WindowChromeChecks
{
    internal static async Task Run(WorkspaceWindow window, Action<bool, string> check)
    {
        var handle = new WindowInteropHelper(window).Handle;
        async Task Flush()
        {
            await Task.Delay(50);
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
        }
        int Hit(FrameworkElement element)
        {
            var point = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
            return (int)SendMessage(handle, 0x84, IntPtr.Zero, Pack(point));
        }
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(window.WindowStyle == WindowStyle.None && !window.AllowsTransparency, "custom frame replaces system caption without layered transparency");
        check(Hit(window.CaptionDragArea) == 2, "empty header returns HTCAPTION for native drag/double-click");
        check(Hit(window.MinimizeWindowButton) == 1 && Hit(window.MaximizeWindowButton) == 1 && Hit(window.CloseWindowButton) == 1,
            "caption buttons remain clickable client controls");
        var edge = window.PointToScreen(new Point(2, window.ActualHeight / 2));
        check((int)SendMessage(handle, 0x84, IntPtr.Zero, Pack(edge)) == 10, "left border returns native resize hit target");
        // 最大化可能把屏幕外窗口带到当前屏幕，状态检查期间保持完全透明。
        var previousOpacity = window.Opacity;
        var previousLeft = window.Left; var previousTop = window.Top;
        window.Opacity = 0;
        try
        {
            Click(window.MinimizeWindowButton); await Flush();
            check(window.WindowState == WindowState.Minimized, "minimize button changes native window state");
            SystemCommands.RestoreWindow(window); await Flush();
            Click(window.MaximizeWindowButton); await Flush();
            check(window.WindowState == WindowState.Maximized, "maximize button changes native window state");
            check(AutomationProperties.GetName(window.MaximizeWindowButton) == "还原窗口", "maximized button exposes restore label");
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            check(GetMonitorInfo(MonitorFromWindow(handle, 2), ref info), "current monitor work area obtained");
            var topLeft = window.Root.PointToScreen(new Point(0, 0));
            var bottomRight = window.Root.PointToScreen(new Point(window.Root.ActualWidth, window.Root.ActualHeight));
            check(topLeft.X >= info.Work.Left - 1 && topLeft.Y >= info.Work.Top - 1 && bottomRight.X <= info.Work.Right + 1 && bottomRight.Y <= info.Work.Bottom + 1,
                $"maximized content fits work area: {topLeft} to {bottomRight}; work {info.Work.Left},{info.Work.Top},{info.Work.Right},{info.Work.Bottom}");
            Click(window.MaximizeWindowButton); await Flush();
            check(window.WindowState == WindowState.Normal && AutomationProperties.GetName(window.MaximizeWindowButton) == "最大化窗口", "restore button restores state and label");
            var caption = window.CaptionDragArea.PointToScreen(new Point(10, 20));
            SendMessage(handle, 0xA3, new IntPtr(2), Pack(caption)); await Flush();
            check(window.WindowState == WindowState.Maximized, "native caption double-click maximizes");
            SystemCommands.RestoreWindow(window); await Flush();
            var closed = false;
            void Cancel(object? sender, System.ComponentModel.CancelEventArgs e) { closed = true; e.Cancel = true; }
            window.Closing += Cancel;
            try { Click(window.CloseWindowButton); await Flush(); check(closed && window.IsVisible, "close button respects cancellable Closing path"); }
            finally { window.Closing -= Cancel; }
        }
        finally
        {
            window.WindowState = WindowState.Normal;
            window.Left = previousLeft; window.Top = previousTop;
            await Flush(); window.Opacity = previousOpacity;
        }
    }
    private static IntPtr Pack(Point point) => new(unchecked(((int)point.Y << 16) | ((int)point.X & 0xffff)));
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
