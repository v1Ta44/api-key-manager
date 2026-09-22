# Drive the WPF app through a scripted UI session (for verification).
# Usage: .\tools\ui-session.ps1 -ExePath <exe> -AppArgs @('--demo') -OutDir <dir>
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [string[]]$AppArgs = @(),
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$Password = "demo"
)

Add-Type -AssemblyName System.Windows.Forms, System.Drawing

Add-Type -ReferencedAssemblies 'System.Drawing','System.Windows.Forms' -TypeDefinition @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
using System.Drawing; using System.Drawing.Imaging;
public class UI {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern IntPtr SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
  [DllImport("user32.dll")] public static extern short VkKeyScan(char ch);
  static readonly IntPtr TOPMOST = new IntPtr(-1), NOTOPMOST = new IntPtr(-2);
  const uint NOMOVE=0x0002, NOSIZE=0x0001, SHOW=0x0040;
  public struct R { public int L, T, Rt, B; }

  public static List<IntPtr> All(uint t){ var l=new List<IntPtr>();
    EnumWindows((h,x)=>{ uint p; GetWindowThreadProcessId(h,out p);
      if(p==t && IsWindowVisible(h)){ var sb=new StringBuilder(256); GetWindowText(h,sb,256);
        if(sb.Length>0) l.Add(h);} return true;}, IntPtr.Zero); return l; }

  public static string TextOf(IntPtr h){ var sb=new StringBuilder(256); GetWindowText(h,sb,256); return sb.ToString(); }

  public static IntPtr FindByTitle(uint t, string part){
    foreach(var h in All(t)){ if(TextOf(h).Contains(part)) return h; }
    return IntPtr.Zero; }

  public static void Force(IntPtr h){
    ShowWindow(h, 9);
    SetWindowPos(h, TOPMOST, 0,0,0,0, NOMOVE|NOSIZE|SHOW);
    BringWindowToTop(h); SetForegroundWindow(h);
    SetWindowPos(h, NOTOPMOST, 0,0,0,0, NOMOVE|NOSIZE|SHOW);
  }

  public static void ClickAt(int x, int y){
    SetCursorPos(x, y);
    System.Threading.Thread.Sleep(150);
    mouse_event(2,0,0,0,IntPtr.Zero);
    System.Threading.Thread.Sleep(70);
    mouse_event(4,0,0,0,IntPtr.Zero);
    System.Threading.Thread.Sleep(150);
  }

  public static void Key(byte vk){
    keybd_event(vk, 0, 0, IntPtr.Zero);
    System.Threading.Thread.Sleep(40);
    keybd_event(vk, 0, 2, IntPtr.Zero);
    System.Threading.Thread.Sleep(80);
  }

  public static void TypeText(string s){
    foreach(char c in s){
      short vk = VkKeyScan(c);
      bool shift = (vk & 0x100) != 0;
      if(shift) keybd_event(0x10,0,0,IntPtr.Zero);
      keybd_event((byte)(vk & 0xFF), 0, 0, IntPtr.Zero);
      keybd_event((byte)(vk & 0xFF), 0, 2, IntPtr.Zero);
      if(shift) keybd_event(0x10,0,2,IntPtr.Zero);
      System.Threading.Thread.Sleep(20);
    }
  }

  public static void Shot(IntPtr h, string path){
    R r; GetWindowRect(h, out r);
    int w = r.Rt - r.L, ht = r.B - r.T;
    var bmp = new Bitmap(w, ht);
    var g = Graphics.FromImage(bmp);
    g.CopyFromScreen(r.L, r.T, 0, 0, new Size(w, ht));
    bmp.Save(path, ImageFormat.Png);
    g.Dispose(); bmp.Dispose();
  }
}
"@

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$proc = if ($AppArgs.Count -gt 0) {
    Start-Process $ExePath -ArgumentList $AppArgs -PassThru
} else { Start-Process $ExePath -PassThru }

Start-Sleep -Seconds 6
$procId32 = [uint32]$proc.Id

# ---- 1. 解锁 ----
$dlg = [UI]::FindByTitle($procId32, "请输入密码")
if ($dlg -ne [IntPtr]::Zero) {
    [UI]::Force($dlg) | Out-Null
    Start-Sleep -Milliseconds 900
    [UI]::TypeText($Password)
    Start-Sleep -Milliseconds 400
    [UI]::Key(0x0D)
    Start-Sleep -Seconds 2
    Write-Output "unlocked"
} else {
    Write-Output "no password dialog found"
}

$main = [UI]::FindByTitle($procId32, "API Key 管理器")
if ($main -eq [IntPtr]::Zero) {
    Write-Output "ERROR: main window not found"
    $proc.Kill()
    exit 1
}

[UI]::Force($main) | Out-Null
Start-Sleep -Milliseconds 1500
[UI]::Shot($main, (Join-Path $OutDir "01-list-light.png"))
Write-Output "shot 01-list-light.png"

$r = New-Object UI+R
[UI]::GetWindowRect($main, [ref]$r) | Out-Null

# ---- 2. 切深色（主题按钮在品牌行最右）----
[UI]::ClickAt(($r.Rt - 60), ($r.T + 82))
Start-Sleep -Milliseconds 1200
[UI]::Force($main) | Out-Null
Start-Sleep -Milliseconds 800
[UI]::Shot($main, (Join-Path $OutDir "02-list-dark.png"))
Write-Output "shot 02-list-dark.png"

# ---- 3. 切回浅色 ----
[UI]::ClickAt(($r.Rt - 60), ($r.T + 82))
Start-Sleep -Milliseconds 1200
[UI]::Force($main) | Out-Null
Start-Sleep -Milliseconds 800
[UI]::Shot($main, (Join-Path $OutDir "03-list-light-back.png"))
Write-Output "shot 03-list-light-back.png"

Write-Output ("window: {0},{1} size {2}x{3}" -f $r.L, $r.T, ($r.Rt-$r.L), ($r.B-$r.T))
Write-Output "PID=$($proc.Id)"
$proc.Kill()
