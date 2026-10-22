# Verifies TaskBarTestStringV3: PrintWindow capture + on-screen capture.
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class PW3 {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
'@
Add-Type -AssemblyName System.Drawing

$tray = [PW3]::FindWindow("Shell_TrayWnd", $null)
if ($tray -eq [IntPtr]::Zero) { Write-Output "taskbar not found"; exit 1 }

$found = [IntPtr]::Zero
$enumCb = [PW3+EnumProc]{
    param($h, $l)
    $sb = New-Object System.Text.StringBuilder 256
    [void][PW3]::GetWindowText($h, $sb, 256)
    if ($sb.ToString() -eq "TaskBarTestStringV3") { $script:found = $h; return $false }
    return $true
}
[void][PW3]::EnumChildWindows($tray, $enumCb, [IntPtr]::Zero)
if ($found -eq [IntPtr]::Zero) { Write-Output "V3 window not found under taskbar"; exit 1 }

$rc = New-Object PW3+RECT
[void][PW3]::GetWindowRect($found, [ref]$rc)
$w = $rc.R - $rc.L; $h = $rc.B - $rc.T
Write-Output ("V3 hwnd={0} rect L={1} T={2} R={3} B={4} size={5}x{6}" -f $found, $rc.L, $rc.T, $rc.R, $rc.B, $w, $h)

# --- 1. PrintWindow capture (what the window itself paints) ---
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[void][PW3]::PrintWindow($found, $hdc, 0)
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save((Join-Path $PWD "v3_printwindow.png"))
$white = 0; $other = 0
for ($x = 0; $x -lt $bmp.Width; $x++) {
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        $c = $bmp.GetPixel($x, $y)
        if ($c.R -gt 200 -and $c.G -gt 200 -and $c.B -gt 200) { $white++ } else { $other++ }
    }
}
Write-Output ("PrintWindow: white={0} other={1}" -f $white, $other)
$bmp.Dispose()

# --- 2. Screen capture at the V3 rect (what is actually composited) ---
Add-Type -AssemblyName System.Windows.Forms
$pad = 4
$sx = [Math]::Max(0, $rc.L - $pad); $sy = [Math]::Max(0, $rc.T - $pad)
$sw = $w + 2*$pad
$sh = $h + 2*$pad
$bmp2 = New-Object System.Drawing.Bitmap($sw, $sh)
$g2 = [System.Drawing.Graphics]::FromImage($bmp2)
$g2.CopyFromScreen($sx, $sy, 0, 0, (New-Object System.Drawing.Size($sw, $sh)))
$g2.Dispose()
$bmp2.Save((Join-Path $PWD "v3_screen.png"))
$white2 = 0; $total = 0
for ($x = 0; $x -lt $bmp2.Width; $x++) {
    for ($y = 0; $y -lt $bmp2.Height; $y++) {
        $c = $bmp2.GetPixel($x, $y)
        $total++
        if ($c.R -gt 220 -and $c.G -gt 220 -and $c.B -gt 220) { $white2++ }
    }
}
Write-Output ("Screen capture region ({0},{1}) {2}x{3}: white-ish={4} of {5}" -f $sx, $sy, $sw, $sh, $white2, $total)
$bmp2.Dispose()
Write-Output "saved v3_printwindow.png + v3_screen.png"
