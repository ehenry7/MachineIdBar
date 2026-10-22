param([string]$OutPng = "taskbar_shot.png")
# Captures the taskbar strip and dumps the z-order of Shell_TrayWnd children.
Add-Type -AssemblyName System.Drawing

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public class W2 {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
'@

# --- capture taskbar strip (virtual coords, this PS is DPI-unaware) ---
$tray = [W2]::FindWindow("Shell_TrayWnd", $null)
$tr = New-Object W2+RECT
[void][W2]::GetWindowRect($tray, [ref]$tr)
$w = $tr.R - $tr.L; $h = $tr.B - $tr.T
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($tr.L, $tr.T, 0, 0, $bmp.Size)
$bmp.Save((Join-Path $PWD $OutPng))
$g.Dispose(); $bmp.Dispose()
Write-Output "saved $OutPng ($w x $h at ($($tr.L),$($tr.T)))"

# --- dump sibling z-order: GW_CHILD is the TOPMOST child, walk GW_HWNDNEXT ---
$sb = New-Object System.Text.StringBuilder 256
$cur = [W2]::GetWindow($tray, 5)  # GW_CHILD = topmost
$idx = 0
while ($cur -ne [IntPtr]::Zero -and $idx -lt 40) {
    $sb.Clear() | Out-Null
    [void][W2]::GetClassName($cur, $sb, 256)
    $cls = $sb.ToString()
    $sb.Clear() | Out-Null
    [void][W2]::GetWindowText($cur, $sb, 256)
    $title = $sb.ToString()
    $r = New-Object W2+RECT
    [void][W2]::GetWindowRect($cur, [ref]$r)
    $vis = [W2]::IsWindowVisible($cur)
    Write-Output ("z[{0,-2}] hwnd={1,-9} vis={2,-5} rect=({3},{4})-({5},{6})  class={7}  title={8}" -f $idx, $cur, $vis, $r.L, $r.T, $r.R, $r.B, $cls, $title)
    $cur = [W2]::GetWindow($cur, 2)  # GW_HWNDNEXT
    $idx++
}
