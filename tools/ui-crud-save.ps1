# Verify the full add-record round trip through the real UI, then confirm
# the record was actually persisted to the encrypted vault on disk.
# Usage: .\tools\ui-crud-save.ps1 -ExePath <exe> -OutDir <dir>
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][string]$OutDir
)

Add-Type -AssemblyName System.Windows.Forms, System.Drawing

Add-Type -ReferencedAssemblies 'System.Drawing','System.Windows.Forms' -TypeDefinition @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
using System.Drawing; using System.Drawing.Imaging;
public class CS {
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
    System.Threading.Thread.Sleep(350);
  }

  public static void Click(int x, int y){
    SetCursorPos(x, y); System.Threading.Thread.Sleep(200);
    mouse_event(2,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(70);
    mouse_event(4,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(350);
  }

  public static void Key(byte vk){
    keybd_event(vk,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(50);
    keybd_event(vk,0,2,IntPtr.Zero); System.Threading.Thread.Sleep(150);
  }

  public static void Tab(){ Key(0x09); }

  /// Ctrl + 指定键
  public static void CtrlKey(byte vk){
    keybd_event(0x11,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(60);
    keybd_event(vk,0,0,IntPtr.Zero);   System.Threading.Thread.Sleep(60);
    keybd_event(vk,0,2,IntPtr.Zero);   System.Threading.Thread.Sleep(60);
    keybd_event(0x11,0,2,IntPtr.Zero); System.Threading.Thread.Sleep(150);
  }

  /// 用剪贴板粘贴中文，避免 VkKeyScan 处理不了 CJK
  public static void Paste(string s){
    System.Windows.Forms.Clipboard.SetText(s);
    System.Threading.Thread.Sleep(120);
    keybd_event(0x11,0,0,IntPtr.Zero);
    keybd_event(0x56,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(60);
    keybd_event(0x56,0,2,IntPtr.Zero);
    keybd_event(0x11,0,2,IntPtr.Zero);
    System.Threading.Thread.Sleep(200);
  }

  public static void Shot(IntPtr h, string path){
    R r; GetWindowRect(h, out r);
    int w = r.Rt-r.L, ht = r.B-r.T;
    var bmp = new Bitmap(w, ht);
    using(var g = Graphics.FromImage(bmp)){
      IntPtr dc = g.GetHdc();
      PrintWindow(h, dc, 2);
      g.ReleaseHdc(dc);
    }
    bmp.Save(path, ImageFormat.Png);
    bmp.Dispose();
  }
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
}
"@

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$dir = Join-Path $env:TEMP ("akm-save-" + [guid]::NewGuid().ToString("N").Substring(0,8))
$p = Start-Process $ExePath -ArgumentList @("--demo", "--dir", $dir) -PassThru
Start-Sleep -Seconds 6
$procId = [uint32]$p.Id

# 解锁
$dlg = [CS]::Find($procId, "请输入密码")
if ($dlg -ne [IntPtr]::Zero) {
    [CS]::Force($dlg)
    [CS]::Paste("demo")
    [CS]::Key(0x0D)
    Start-Sleep -Seconds 2
}
$alert = [CS]::Find($procId, "密钥到期提醒")
if ($alert -ne [IntPtr]::Zero) {
    [CS]::Force($alert); Start-Sleep -Milliseconds 500
    [CS]::Key(0x0D); Start-Sleep -Milliseconds 800
}
$main = [CS]::Find($procId, "API Key 管理器")
[CS]::Force($main); Start-Sleep -Milliseconds 700
Write-Output ("before: " + ([CS]::Titles($procId) -join ' | '))

# 记住新增前的记录数（用 Control+A 全选不行，改为直接看状态栏；这里只验证保存后文件变化）
$before = (Get-Item (Join-Path $dir "vault.akv")).Length
Write-Output "vault size before = $before"

# Ctrl+N 打开新增
[CS]::CtrlKey(0x4E)
Start-Sleep -Seconds 2
$edit = [CS]::Find($procId, "新增 API Key")
if ($edit -eq [IntPtr]::Zero) { Write-Output "ERROR: edit dialog not opened"; $p.Kill(); exit 1 }
[CS]::Force($edit); Start-Sleep -Milliseconds 800
Write-Output ("dialog: " + ([CS]::Titles($procId) -join ' | '))

# 焦点已在「名称」上，依次填字段（Tab 在字段间移动）
[CS]::Paste("自动化测试记录")
[CS]::Tab()
[CS]::Paste("TestProvider")
[CS]::Tab()
[CS]::Paste("sk-auto-test-1234567890abcdef")
[CS]::Tab()
[CS]::Paste("https://api.test.local/v1")
[CS]::Tab()
[CS]::Paste("test-model-1")
[CS]::Tab()
[CS]::Paste("自动,测试")
Start-Sleep -Milliseconds 400

[CS]::Shot($edit, (Join-Path $OutDir "20-edit-filled.png"))
Write-Output "shot 20-edit-filled.png"

# 保存（Enter 触发 IsDefault 的保存按钮）
[CS]::Key(0x0D)
Start-Sleep -Seconds 2
Write-Output ("after save: " + ([CS]::Titles($procId) -join ' | '))

$main = [CS]::Find($procId, "API Key 管理器")
[CS]::Force($main); Start-Sleep -Milliseconds 1000
[CS]::Shot($main, (Join-Path $OutDir "21-after-save.png"))
Write-Output "shot 21-after-save.png"

$after = (Get-Item (Join-Path $dir "vault.akv")).Length
Write-Output "vault size after = $after  (changed: $($before -ne $after))"

$p.Kill()
Write-Output "DIR=$dir"
