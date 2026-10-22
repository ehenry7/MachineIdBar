Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public class Z2 {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
'@
$SWP_NOSIZE = 0x0001; $SWP_NOMOVE = 0x0002; $SWP_SHOWWINDOW = 0x0040
$tray = [Z2]::FindWindow("Shell_TrayWnd", $null)
$found = [IntPtr]::Zero
$enumCb = [Z2+EnumProc]{
    param($h, $l)
    $sb = New-Object System.Text.StringBuilder 256
    [void][Z2]::GetWindowText($h, $sb, 256)
    if ($sb.ToString() -eq "TaskBarTestStringV2") { $script:found = $h; return $false }
    return $true
}
[void][Z2]::EnumChildWindows($tray, $enumCb, [IntPtr]::Zero)
if ($found -eq [IntPtr]::Zero) { Write-Output "V2 window not found via enumeration"; exit 1 }
[void][Z2]::SetWindowPos($found, [IntPtr]::Zero, 0, 0, 0, 0, $SWP_NOSIZE -bor $SWP_NOMOVE -bor $SWP_SHOWWINDOW)
$r = New-Object Z2+RECT
[void][Z2]::GetWindowRect($found, [ref]$r)
Write-Output ("V2 hwnd={0} rect=({1},{2})-({3},{4}) size={5}x{6} (virtual px)" -f $found, $r.L, $r.T, $r.R, $r.B, ($r.R-$r.L), ($r.B-$r.T))
