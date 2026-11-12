# Dev helper: make sure Mindspace is running, send it one or more commands, wait, screenshot.
#   tools\try.ps1 -Cmd 'ask sage what is an API' -Wait 8 -Shot sage
param([string]$Cmd = '', [double]$Wait = 2, [string]$Shot = '', [int]$X = 1080, [int]$Y = 0, [int]$W = 1300, [int]$H = 1500, [switch]$Restart, [string]$OutDir = "$env:TEMP")
$exe = Join-Path $PSScriptRoot '..\src\Mindspace\bin\Release\net9.0-windows\Mindspace.exe'
if ($Restart) { Get-Process Mindspace -ErrorAction SilentlyContinue | ForEach-Object { Start-Process $exe -ArgumentList 'quit' -Wait; Start-Sleep -Milliseconds 800 } }
if (-not (Get-Process Mindspace -ErrorAction SilentlyContinue)) { Start-Process $exe -ArgumentList 'close'; Start-Sleep -Seconds 3 }
foreach ($c in ($Cmd.Split('|') | Where-Object { $_ })) { Start-Process $exe -ArgumentList $c -Wait; Start-Sleep -Milliseconds 400 }
Start-Sleep -Seconds $Wait
if ($Shot) { & (Join-Path $PSScriptRoot 'shot.ps1') -Out (Join-Path $OutDir "$Shot.png") -X $X -Y $Y -W $W -H $H | Out-Null }
Get-Process Mindspace -ErrorAction SilentlyContinue | ForEach-Object { "mem: ws $([int]($_.WorkingSet64/1MB)) MB, private $([int]($_.PrivateMemorySize64/1MB)) MB, cpu $([math]::Round($_.CPU,1))s" }
