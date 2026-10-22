# Builds TaskBarTestStringV3.exe and launches it detached.
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$dir = "C:\Users\h00569653\Desktop\MachineIdBar"
Set-Location $dir
& $csc /nologo /target:exe /out:TaskBarTestStringV3.exe TaskBarTestStringV3.cs
if ($LASTEXITCODE -ne 0) { Write-Output "BUILD FAILED"; exit 1 }
Write-Output "build OK"
Start-Process -FilePath "$dir\TaskBarTestStringV3.exe" -ArgumentList '--left' -WorkingDirectory $dir -WindowStyle Hidden
Start-Sleep -Seconds 3
Get-Process -Name "TaskBarTestStringV3" -ErrorAction SilentlyContinue | Format-Table Id, ProcessName
