# Enumerate children of Shell_TrayWnd to locate our test window.
$log = "C:\Users\h00569653\Desktop\MachineIdBar\taskbar_v2_children.txt"
$out = @()

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class X {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

$tray = [X]::FindWindow("Shell_TrayWnd", $null)
$out += "Shell_TrayWnd = $tray"
$cb = [X+EnumProc]{ param($h, $l)
    $cn = New-Object System.Text.StringBuilder 256
    [void][X]::GetClassName($h, $cn, 256)
    $wt = New-Object System.Text.Text.StringBuilder(0)
    $wt = New-Object System.Text.StringBuilder 256
    [void][X]::GetWindowText($h, $wt, 256)
    $pid2 = 0
    [void][X]::GetWindowThreadProcessId($h, [ref]$pid2)
    $vis = [X]::IsWindowVisible($h)
    $script:found = $true
    Write-Output ("child: class={0} title={1} hwnd={2} pid={3} visible={4}" -f $cn.ToString(), $wt.ToString(), $h, $pid2, $vis)
    return $true
}
# EnumChildWindows needs a real delegate; use direct call approach
$children = New-Object System.Collections.ArrayList
$enumCb = [X+EnumProc]{ param($h, $l)
    $cn = New-Object System.Text.StringBuilder 256
    [void][X]::GetClassName($h, $cn, 256)
    $wt = New-Object System.Text.StringBuilder 256
    [void][X]::GetWindowText($h, $wt, 256)
    $procId = [uint32]0
    [void][X]::GetWindowThreadProcessId($h, [ref]$procId)
    $vis = [X]::IsWindowVisible($h)
    $r = New-Object X+RECT
    [void][X]::GetWindowRect($h, [ref]$r)
    [void]$script:children.Add(("class={0,-60} title={1,-25} hwnd={2,-10} pid={3,-8} vis={4} rect=({5},{6})-({7},{8})" -f $cn.ToString(), $wt.ToString(), $h, $procId, $vis, $r.L, $r.T, $r.R, $r.B))
    return $true
}
[void][X]::EnumChildWindows($tray, $enumCb, [IntPtr]::Zero)
$out += $children
$out += ""
$out += "=== TaskBarTestStringV2 process ==="
$procs = Get-Process TaskBarTestStringV2 -ErrorAction SilentlyContinue
if ($procs) { $out += "RUNNING pid=$($procs.Id)" } else { $out += "NOT RUNNING" }
$out | Out-File -FilePath $log -Encoding UTF8
$out
