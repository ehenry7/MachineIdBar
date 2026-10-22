# Verifies that TaskBarTestStringV2.exe is embedded in the taskbar.
$log = "C:\Users\h00569653\Desktop\MachineIdBar\taskbar_v2_verify.txt"
$out = @()

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class W {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string w);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

$tray0 = [W]::FindWindow("Shell_TrayWnd", $null)
$hwnd = [W]::FindWindowEx($tray0, [IntPtr]::Zero, $null, "TaskBarTestStringV2")
if ($hwnd -eq [IntPtr]::Zero) {
    $out += "RESULT: WINDOW NOT FOUND (as child of Shell_TrayWnd)"
    $out += ""
} else {
    $out += "hwnd        = $hwnd"
    $out += "visible     = $([W]::IsWindowVisible($hwnd))"

    $parent = [W]::GetParent($hwnd)
    $sb = New-Object System.Text.StringBuilder 256
    [void][W]::GetClassName($parent, $sb, 256)
    $out += "parent hwnd = $parent (class: $($sb.ToString()))"

    $r = New-Object W+RECT
    [void][W]::GetWindowRect($hwnd, [ref]$r)
    $out += "win rect    = L=$($r.L) T=$($r.T) R=$($r.R) B=$($r.B)  ($($r.R - $r.L) x $($r.B - $r.T))"

    $tray = [W]::FindWindow("Shell_TrayWnd", $null)
    [void][W]::GetWindowRect($tray, [ref]$r)
    $out += "taskbar rect= L=$($r.L) T=$($r.T) R=$($r.R) B=$($r.B)  ($($r.R - $r.L) x $($r.B - $r.T))"

    $notify = [W]::FindWindowEx($tray, [IntPtr]::Zero, "TrayNotifyWnd", $null)
    if ($notify -ne [IntPtr]::Zero) {
        [void][W]::GetWindowRect($notify, [ref]$r)
        $out += "notify rect = L=$($r.L) T=$($r.T) R=$($r.R) B=$($r.B)"
    }
    $bridge = [W]::FindWindowEx($tray, [IntPtr]::Zero, "Windows.UI.Composition.DesktopWindowContentBridge", $null)
    $out += "Win11 taskbar (XAML bridge present) = $($bridge -ne [IntPtr]::Zero)"

    if ($parent -eq $tray) { $out += "RESULT: EMBEDDED (parent is Shell_TrayWnd)" }
    else { $out += "RESULT: NOT EMBEDDED (parent class $($sb.ToString()))" }
}
$out | Out-File -FilePath $log -Encoding UTF8
$out | Write-Output
