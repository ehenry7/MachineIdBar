# Prints the V2 window content with PrintWindow (works even if occluded).
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class PW {
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
}
'@
Add-Type -AssemblyName System.Drawing

$tray = [PW]::FindWindow("Shell_TrayWnd", $null)
$found = [IntPtr]::Zero
$enumCb = [PW+EnumProc]{
    param($h, $l)
    $sb = New-Object System.Text.StringBuilder 256
    [void][PW]::GetWindowText($h, $sb, 256)
    if ($sb.ToString() -eq "TaskBarTestStringV2") { $script:found = $h; return $false }
    return $true
}
[void][PW]::EnumChildWindows($tray, $enumCb, [IntPtr]::Zero)
if ($found -eq [IntPtr]::Zero) { Write-Output "V2 not found"; exit 1 }

Write-Output ("style   = 0x{0:X8}" -f [PW]::GetWindowLong($found, -16))
Write-Output ("exStyle = 0x{0:X8}" -f [PW]::GetWindowLong($found, -20))

$bmp = New-Object System.Drawing.Bitmap(500, 70)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[void][PW]::PrintWindow($found, $hdc, 0)
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save((Join-Path $PWD "v2_printwindow.png"))
Write-Output "saved v2_printwindow.png"

# count blue pixels
$blue = 0; $dark = 0; $other = 0
for ($x = 0; $x -lt $bmp.Width; $x++) {
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        $c = $bmp.GetPixel($x, $y)
        if ($c.B -gt 180 -and $c.G -gt 60 -and $c.G -lt 150 -and $c.R -lt 80) { $blue++ }
        elseif ($c.R -lt 20 -and $c.G -lt 20 -and $c.B -lt 20) { $dark++ }
        else { $other++ }
    }
}
Write-Output ("blue={0} dark={1} other={2}" -f $blue, $dark, $other)
$bmp.Dispose()
