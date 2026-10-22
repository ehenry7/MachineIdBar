param([int]$X0 = 1432, [int]$Y0 = 8, [int]$X1 = 1676, [int]$Y1 = 40)
# Analyzes pixel colors in a region of taskbar_shot.png. Default region =
# our bar's rect relative to the taskbar screenshot origin.
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap((Join-Path $PWD "taskbar_before.png"))
$histo = @{}
$sampleBlue = 0; $sampleOther = 0
for ($x = $X0; $x -lt $X1; $x += 1) {
    for ($y = $Y0; $y -lt $Y1; $y += 1) {
        $c = $bmp.GetPixel($x, $y)
        $key = "{0:X2}{1:X2}{2:X2}" -f $c.R, $c.G, $c.B
        if ($histo.ContainsKey($key)) { $histo[$key]++ } else { $histo[$key] = 1 }
    }
}
Write-Output "=== top colors in bar region ($X0,$Y0)-($X1,$Y1) ==="
$histo.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 12 | ForEach-Object {
    Write-Output ("{0}  count={1}" -f $_.Key, $_.Value)
}

# Also scan the whole strip for any Windows-blue-ish (#0078D4 = R0 G120 B212) pixels
$blueCount = 0
for ($x = 0; $x -lt $bmp.Width; $x++) {
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        $c = $bmp.GetPixel($x, $y)
        if ($c.B -gt 180 -and $c.G -gt 60 -and $c.G -lt 150 -and $c.R -lt 80) { $blueCount++ }
    }
}
Write-Output "blue-ish pixels in whole taskbar strip: $blueCount"
$bmp.Dispose()
