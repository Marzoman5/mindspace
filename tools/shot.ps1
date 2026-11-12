# Captures the primary screen (or a region of it) to a PNG. Used only to verify the UI during development.
param([string]$Out = "$env:TEMP\mindspace-shot.png", [int]$X = 0, [int]$Y = 0, [int]$W = 0, [int]$H = 0)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Dpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i); }
"@
[Dpi]::SetProcessDPIAware() | Out-Null
if ($W -eq 0) { $W = [Dpi]::GetSystemMetrics(0) - $X }
if ($H -eq 0) { $H = [Dpi]::GetSystemMetrics(1) - $Y }
$bmp = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($X, $Y, 0, 0, $bmp.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
"$Out ${W}x${H}"
