using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ApiKeyManager.Wpf.Workspace;

public partial class WorkspaceWindow
{
    private static readonly Geometry MaximizeIcon = Geometry.Parse("M 3,3 L 13,3 L 13,13 L 3,13 Z");
    private static readonly Geometry RestoreIcon = Geometry.Parse("M 5,3 L 5,1 L 15,1 L 15,11 L 13,11 M 1,5 L 11,5 L 11,15 L 1,15 Z");
    private HwndSource? _chromeSource;

    private void InitializeWindowChrome()
    {
        SourceInitialized += (_, _) =>
        {
            _chromeSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _chromeSource?.AddHook(ConstrainMaximizedBounds);
        };
        Closed += (_, _) =>
        {
            if (_chromeSource is { IsDisposed: false }) _chromeSource.RemoveHook(ConstrainMaximizedBounds);
            _chromeSource = null;
        };
        StateChanged += (_, _) =>
        {
            var maximized = WindowState == WindowState.Maximized;
            MaximizeGlyph.Data = maximized ? RestoreIcon : MaximizeIcon;
            MaximizeWindowButton.ToolTip = maximized ? "还原" : "最大化";
            AutomationProperties.SetName(MaximizeWindowButton, maximized ? "还原窗口" : "最大化窗口");
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.System || e.SystemKey != Key.Space) return;
            SystemCommands.ShowSystemMenu(this, PointToScreen(new Point(12, TitleBar.ActualHeight)));
            e.Handled = true;
        };
    }

    private void OnMinimizeWindow(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnToggleMaximizeWindow(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    // 走原有 Closing 流程，保存期间的关闭保护继续生效。
    private void OnCloseWindow(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    // WindowStyle.None 默认可能最大化到整屏；使用该 HWND 所在屏幕的工作区避开任务栏。
    private IntPtr ConstrainMaximizedBounds(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x24) return IntPtr.Zero; // WM_GETMINMAXINFO
        var monitor = new MonitorBounds { Size = Marshal.SizeOf<MonitorBounds>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor)) return IntPtr.Zero;
        var limits = Marshal.PtrToStructure<MinMaxBounds>(lParam);
        limits.MaxPosition = new(monitor.Work.Left - monitor.Screen.Left, monitor.Work.Top - monitor.Screen.Top);
        limits.MaxSize = new(monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top);
        var dpi = VisualTreeHelper.GetDpi(this);
        limits.MinTrackSize = new((int)Math.Ceiling(MinWidth * dpi.DpiScaleX), (int)Math.Ceiling(MinHeight * dpi.DpiScaleY));
        Marshal.StructureToPtr(limits, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxBounds { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorBounds { public int Size; public NativeRect Screen, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorBounds info);
}
