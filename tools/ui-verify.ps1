# Capture the WPF main window after unlocking, with the window moved fully onscreen.
# Usage: .\tools\ui-verify.ps1 -ExePath <exe> -OutDir <dir> [-Dark]
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$Password = "demo",
    [switch]$Dark
)

Add-Type -AssemblyName System.Windows.Forms, System.Drawing

Add-Type -ReferencedAssemblies 'System.Drawing','System.Windows.Forms' -TypeDefinition @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
using System.Drawing; using System.Drawing.Imaging;
public class UV {
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

  // 移动到指定位置，保证整个窗口都在屏幕内
  public static void Place(IntPtr h, int x, int y){
    ShowWindow(h, 9);
    SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, NOMOVE==0?0:NOSIZE);
    SetForegroundWindow(h);
  }

  public static void MoveTo(IntPtr h, int x, int y, int w, int ht){
    ShowWindow(h, 9);
    SetWindowPos(h, TOPMOST, x, y, w, ht, SHOW);
    BringWindowToTop(h); SetForegroundWindow(h);
    SetWindowPos(h, NOTOPMOST, x, y, w, ht, SHOW);
    System.Threading.Thread.Sleep(400);
  }

  // 只移动、不改变尺寸（保留应用自己算出的尺寸）
  public static void MoveOnly(IntPtr h, int x, int y){
    ShowWindow(h, 9);
    SetWindowPos(h, TOPMOST, x, y, 0, 0, NOSIZE|SHOW);
    BringWindowToTop(h); SetForegroundWindow(h);
    SetWindowPos(h, NOTOPMOST, x, y, 0, 0, NOSIZE|SHOW);
    System.Threading.Thread.Sleep(400);
  }

  public static void Maximize(IntPtr h){
    ShowWindow(h, 3);   // SW_MAXIMIZE
    System.Threading.Thread.Sleep(600);
  }

  public static void ClickAt(int x, int y){
    SetCursorPos(x, y);
    System.Threading.Thread.Sleep(150);
    mouse_event(2,0,0,0,IntPtr.Zero);
    System.Threading.Thread.Sleep(70);
    mouse_event(4,0,0,0,IntPtr.Zero);
    System.Threading.Thread.Sleep(250);
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

$args = @("--demo", "--dir", (Join-Path $env:TEMP ("akm-uv-" + [guid]::NewGuid().ToString("N").Substring(0,8))))
if ($Dark) { $args += "--dark" }

# 先用 --demo 建库，然后关掉；再用同一个 --dir 启动（避免每次都是新库）
$p0 = Start-Process $ExePath -ArgumentList $args -PassThru
Start-Sleep -Seconds 5
$dlg0 = [UV]::FindByTitle([uint32]$p0.Id, "请输入密码")
if ($dlg0 -ne [IntPtr]::Zero) {
    [UV]::MoveTo($dlg0, 300, 200, 460, 300)
    [UV]::TypeText($Password)
    Start-Sleep -Milliseconds 300
    [UV]::Key(0x0D)
    Start-Sleep -Seconds 2
}
$main0 = [UV]::FindByTitle([uint32]$p0.Id, "API Key 管理器")
if ($main0 -eq [IntPtr]::Zero) { Write-Output "ERROR: no main window"; $p0.Kill(); exit 1 }

# 关掉可能弹出的到期提醒
[UV]::Key(0x0D)
Start-Sleep -Milliseconds 500
[UV]::Key(0x0D)
Start-Sleep -Milliseconds 800

# 只移动位置，不强行指定尺寸。
# 强行 SetWindowPos 会与 WPF 自己的 MinWidth/Width 打架，
# 让窗口被压到比内容窄，右侧列被裁掉（截图工具的坑，不是应用问题）。
[UV]::MoveOnly($main0, 8, 8)
Start-Sleep -Milliseconds 1500

$r = New-Object UV+R
[UV]::GetWindowRect($main0, [ref]$r) | Out-Null
$wPx = $r.Rt - $r.L
$hPx = $r.B - $r.T
Write-Output ("window rect: {0},{1}  {2}x{3} px" -f $r.L, $r.T, $wPx, $hPx)

# 窗口比屏幕宽时，CopyFromScreen 截不到超出部分。先最大化再截。
Add-Type -AssemblyName System.Windows.Forms
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
if ($wPx -ge $screen.Width) {
    Write-Output "window as wide as screen; maximizing for a complete capture"
    [UV]::Maximize($main0)
    Start-Sleep -Milliseconds 1400
    [UV]::GetWindowRect($main0, [ref]$r) | Out-Null
    Write-Output ("after maximize: {0},{1}  {2}x{3} px" -f $r.L, $r.T, ($r.Rt-$r.L), ($r.B-$r.T))
}

[UV]::Shot($main0, (Join-Path $OutDir "list-light.png"))
Write-Output "shot list-light.png"

# 切深色：主题按钮在品牌行最右侧
$r2 = New-Object UV+R
[UV]::GetWindowRect($main0, [ref]$r2) | Out-Null
[UV]::ClickAt(($r2.Rt - 62), ($r2.T + 84))
Start-Sleep -Milliseconds 1300
[UV]::MoveOnly($main0, 8, 8)
Start-Sleep -Milliseconds 900
[UV]::Shot($main0, (Join-Path $OutDir "list-dark.png"))
Write-Output "shot list-dark.png"

Write-Output "PID=$($p0.Id)"
Write-Output "DIR=$($args[2])"
# 不杀进程，留给后续交互
