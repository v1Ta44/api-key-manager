# Capture the main window of a process to a PNG (for UI verification).
# Usage: .\tools\shot.ps1 -ExePath <path> -Out <png> [-AppArgs <string[]>] [-Delay 6] [-ClickTheme]
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][string]$Out,
    [string[]]$AppArgs = @(),
    [int]$Delay = 6,
    [switch]$ClickTheme
)

Add-Type -AssemblyName System.Windows.Forms, System.Drawing

Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public class ShotWin {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern IntPtr SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
  static readonly IntPtr TOPMOST = new IntPtr(-1);
  static readonly IntPtr NOTOPMOST = new IntPtr(-2);
  const uint NOMOVE=0x0002, NOSIZE=0x0001, SHOW=0x0040;
  public struct R { public int L, T, Rt, B; }
  public static List<IntPtr> Find(uint t){ var l=new List<IntPtr>();
    EnumWindows((h,x)=>{ uint p; GetWindowThreadProcessId(h,out p);
      if(p==t && IsWindowVisible(h)){ var sb=new StringBuilder(256); GetWindowText(h,sb,256);
        if(sb.Length>0) l.Add(h);} return true;}, IntPtr.Zero); return l; }
  public static void Force(IntPtr h){
    ShowWindow(h, 9);
    SetWindowPos(h, TOPMOST, 0,0,0,0, NOMOVE|NOSIZE|SHOW);
    BringWindowToTop(h); SetForegroundWindow(h);
    SetWindowPos(h, NOTOPMOST, 0,0,0,0, NOMOVE|NOSIZE|SHOW);
  }
  public static void Click(){ mouse_event(2,0,0,0,IntPtr.Zero); mouse_event(4,0,0,0,IntPtr.Zero); }
}
"@

$proc = if ($AppArgs.Count -gt 0) {
    Start-Process $ExePath -ArgumentList $AppArgs -PassThru
} else {
    Start-Process $ExePath -PassThru
}

Start-Sleep -Seconds $Delay

if ($proc.HasExited) {
    Write-Output ("ERROR: process exited early, code=" + $proc.ExitCode)
    exit 1
}

$wins = [ShotWin]::Find([uint32]$proc.Id)
if ($wins.Count -eq 0) {
    Write-Output "ERROR: no visible window found"
    $proc.Kill()
    exit 1
}

$h = $wins[0]
[ShotWin]::Force($h) | Out-Null
Start-Sleep -Milliseconds 1200

if ($ClickTheme) {
    $r0 = New-Object ShotWin+R
    [ShotWin]::GetWindowRect($h, [ref]$r0) | Out-Null
    $prev = [System.Windows.Forms.Cursor]::Position
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point (($r0.Rt - 60), ($r0.T + 82))
    Start-Sleep -Milliseconds 250
    [ShotWin]::Click()
    Start-Sleep -Milliseconds 900
    [System.Windows.Forms.Cursor]::Position = $prev
    [ShotWin]::Force($h) | Out-Null
    Start-Sleep -Milliseconds 600
}

$r = New-Object ShotWin+R
[ShotWin]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Rt - $r.L
$ht = $r.B - $r.T

$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size $w, $ht))
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()

Write-Output ("captured {0}x{1} -> {2}" -f $w, $ht, $Out)
$proc.Kill()
