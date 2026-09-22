# Shared screenshot helper for WPF windows.
#
# WHY THIS EXISTS:
# On a high-DPI display GetWindowRect returns DPI-VIRTUALIZED coordinates.
# A WPF window with Width=520 actually renders 762 physical px wide at 146%
# scaling, but GetWindowRect still says 520. Copying 520x540 px into a bitmap
# therefore crops the right/bottom of the window and makes a perfectly correct
# layout look "clipped". DWMWA_EXTENDED_FRAME_BOUNDS reports the real physical
# rectangle, so always size the bitmap from that.

Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;

public class Shot {
    public delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);

    public struct RECT { public int Left, Top, Right, Bottom; }

    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    const uint PW_RENDERFULLCONTENT = 2;

    public static List<IntPtr> All(uint pid) {
        var list = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h)) {
                var sb = new StringBuilder(512);
                GetWindowText(h, sb, 512);
                if (sb.Length > 0) list.Add(h);
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static string Title(IntPtr h) {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, 512);
        return sb.ToString();
    }

    public static IntPtr Find(uint pid, string contains) {
        foreach (IntPtr h in All(pid))
            if (Title(h).Contains(contains)) return h;
        return IntPtr.Zero;
    }

    /// Physical rectangle of the window (DPI-correct).
    public static RECT PhysicalRect(IntPtr h) {
        RECT r;
        int hr = DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT)));
        if (hr != 0) GetWindowRect(h, out r);
        return r;
    }

    /// Capture the whole window, including the parts a DPI-unaware
    /// GetWindowRect-based capture would crop.
    public static void Capture(IntPtr h, string path) {
        RECT r = PhysicalRect(h);
        int w = r.Right - r.Left;
        int ht = r.Bottom - r.Top;
        if (w <= 0 || ht <= 0) return;
        using (var bmp = new Bitmap(w, ht))
        using (var g = Graphics.FromImage(bmp)) {
            IntPtr dc = g.GetHdc();
            PrintWindow(h, dc, PW_RENDERFULLCONTENT);
            g.ReleaseHdc(dc);
            bmp.Save(path, ImageFormat.Png);
        }
    }

    public static string Describe(IntPtr h) {
        RECT v; GetWindowRect(h, out v);
        RECT p = PhysicalRect(h);
        return string.Format("virtual {0}x{1}  physical {2}x{3}",
            v.Right - v.Left, v.Bottom - v.Top, p.Right - p.Left, p.Bottom - p.Top);
    }
}
"@
