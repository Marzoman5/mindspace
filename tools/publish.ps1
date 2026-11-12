# Builds a single Mindspace.exe into dist\ (needs the .NET 9 Desktop Runtime, which this PC has).
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'dist\Mindspace.exe'
if (Get-Process Mindspace -ErrorAction SilentlyContinue) {
    Get-Process Mindspace | ForEach-Object { Start-Process $_.Path -ArgumentList 'quit' -Wait }
    Start-Sleep -Milliseconds 900
}
dotnet publish (Join-Path $root 'src\Mindspace') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o (Join-Path $root 'dist') -nologo -v q 2>&1 | Select-String ' error |rror\(s\)' | Select-Object -Unique
if (Test-Path $exe) { "{0}  ({1:N0} KB)" -f $exe, ((Get-Item $exe).Length / 1KB) }
