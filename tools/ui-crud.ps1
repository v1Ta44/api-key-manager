# Verify the CRUD flow end to end by driving the real UI.
# Usage: .\tools\ui-crud.ps1 -ExePath <exe> -OutDir <dir>
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][string]$OutDir
)

Add-Type -AssemblyName System.Windows.Forms, System.Drawing

Add-Type -ReferencedAssemblies 'System.Drawing','System.Windows.Forms' -TypeDefinition @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
using System.Drawing; using System.Drawing.Imaging;
public class CR {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EP cb, IntPtr l);
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
  const uint NOSIZE=0x0001, SHOW=0x0040;
  public struct R { public int L, T, Rt, B; }

  public static List<IntPtr> All(uint t){ var l=new List<IntPtr>();
    EnumWindows((h,x)=>{ uint p; GetWindowThreadProcessId(h,out p);
      if(p==t && IsWindowVisible(h)){ var sb=new StringBuilder(256); GetWindowText(h,sb,256);
        if(sb.Length>0) l.Add(h);} return true;}, IntPtr.Zero); return l; }

  public static string T(IntPtr h){ var sb=new StringBuilder(256); GetWindowText(h,sb,256); return sb.ToString(); }
  public static List<string> Titles(uint t){ var l=new List<string>(); foreach(var h in All(t)) l.Add(T(h)); return l; }

  public static IntPtr Find(uint t, string part){ foreach(var h in All(t)){ if(T(h).Contains(part)) return h; } return IntPtr.Zero; }

  public static void Force(IntPtr h){
    ShowWindow(h, 9);
    SetWindowPos(h, TOPMOST, 0,0,0,0, NOSIZE|SHOW);
    BringWindowToTop(h); SetForegroundWindow(h);
    SetWindowPos(h, NOTOPMOST, 0,0,0,0, NOSIZE|SHOW);
    System.Threading.Thread.Sleep(300);
  }

  public static void Click(int x, int y){
    SetCursorPos(x, y); System.Threading.Thread.Sleep(200);
    mouse_event(2,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(70);
    mouse_event(4,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(350);
  }

  public static void Key(byte vk){
    keybd_event(vk,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(50);
    keybd_event(vk,0,2,IntPtr.Zero); System.Threading.Thread.Sleep(120);
  }

  /// Ctrl + 指定键
  public static void CtrlKey(byte vk){
    keybd_event(0x11,0,0,IntPtr.Zero);          // Ctrl down
    System.Threading.Thread.Sleep(60);
    keybd_event(vk,0,0,IntPtr.Zero);
    System.Threading.Thread.Sleep(60);
    keybd_event(vk,0,2,IntPtr.Zero);
    System.Threading.Thread.Sleep(60);
    keybd_event(0x11,0,2,IntPtr.Zero);          // Ctrl up
    System.Threading.Thread.Sleep(150);
  }

  public static void Type(string s){
    foreach(char c in s){
      short vk = VkKeyScan(c);
      bool sh = (vk & 0x100) != 0;
      if(sh) keybd_event(0x10,0,0,IntPtr.Zero);
      keybd_event((byte)(vk & 0xFF),0,0,IntPtr.Zero);
      keybd_event((byte)(vk & 0xFF),0,2,IntPtr.Zero);
      if(sh) keybd_event(0x10,0,2,IntPtr.Zero);
      System.Threading.Thread.Sleep(25);
    }
  }

  public static void Shot(IntPtr h, string path){
    R r; GetWindowRect(h, out r);
    int w = r.Rt-r.L, ht = r.B-r.T;
    var bmp = new Bitmap(w, ht);
    var g = Graphics.FromImage(bmp);
    g.CopyFromScreen(r.L, r.T, 0, 0, new Size(w, ht));
    bmp.Save(path, ImageFormat.Png);
    g.Dispose(); bmp.Dispose();
  }
}
"@

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$dir = Join-Path $env:TEMP ("akm-crud-" + [guid]::NewGuid().ToString("N").Substring(0,8))
$p = Start-Process $ExePath -ArgumentList @("--demo", "--dir", $dir) -PassThru
Start-Sleep -Seconds 6
$procId = [uint32]$p.Id

# ---- 解锁 ----
$dlg = [CR]::Find($procId, "请输入密码")
if ($dlg -ne [IntPtr]::Zero) {
    [CR]::Force($dlg)
    [CR]::Type("demo")
    [CR]::Key(0x0D)
    Start-Sleep -Seconds 2
}

$main = [CR]::Find($procId, "API Key 管理器")
if ($main -eq [IntPtr]::Zero) { Write-Output "ERROR: no main"; $p.Kill(); exit 1 }
[CR]::Force($main)
Start-Sleep -Milliseconds 600

# 关掉到期提醒对话框：它是独立窗口，必须先聚焦到它再按回车
$alert = [CR]::Find($procId, "密钥到期提醒")
if ($alert -ne [IntPtr]::Zero) {
    [CR]::Force($alert)
    Start-Sleep -Milliseconds 700
    [CR]::Key(0x0D)
    Start-Sleep -Milliseconds 900
}

$main = [CR]::Find($procId, "API Key 管理器")
[CR]::Force($main)
Start-Sleep -Milliseconds 700

$r = New-Object CR+R
[CR]::GetWindowRect($main, [ref]$r) | Out-Null
Write-Output ("main: {0},{1} {2}x{3}" -f $r.L, $r.T, ($r.Rt-$r.L), ($r.B-$r.T))
Write-Output ("windows: " + ([CR]::Titles($procId) -join ' | '))

# ---- 打开新增对话框 ----
# 优先用 Ctrl+N 快捷键，避免依赖像素坐标（坐标随字号/DPI 漂移）
Write-Output "sending Ctrl+N"
[CR]::CtrlKey(0x4E)
Start-Sleep -Seconds 2
Write-Output ("after Ctrl+N: " + ([CR]::Titles($procId) -join ' | '))

$edit = [CR]::Find($procId, "新增 API Key")
if ($edit -eq [IntPtr]::Zero) { $edit = [CR]::Find($procId, "编辑 API Key") }

if ($edit -eq [IntPtr]::Zero) {
    Write-Output "accelerator did not fire; falling back to coordinate click"
    [CR]::Click(($r.L + 102), ($r.T + 276))
    Start-Sleep -Seconds 2
    Write-Output ("after click: " + ([CR]::Titles($procId) -join ' | '))
    $edit = [CR]::Find($procId, "新增 API Key")
    if ($edit -eq [IntPtr]::Zero) { $edit = [CR]::Find($procId, "编辑 API Key") }
}

if ($edit -ne [IntPtr]::Zero) {
    [CR]::Force($edit)
    Start-Sleep -Milliseconds 900
    $er = New-Object CR+R
    [CR]::GetWindowRect($edit, [ref]$er) | Out-Null
    Write-Output ("edit dialog: {0},{1} {2}x{3}" -f $er.L, $er.T, ($er.Rt-$er.L), ($er.B-$er.T))
    [CR]::Shot($edit, (Join-Path $OutDir "10-edit-dialog.png"))
    Write-Output "shot 10-edit-dialog.png"
} else {
    Write-Output "WARN: edit dialog not found"
}

$p.Kill()
Write-Output "DIR=$dir"
