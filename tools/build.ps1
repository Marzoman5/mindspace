# Stops a running Mindspace (so the exe isn't locked) and builds Release.
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'src\Mindspace\bin\Release\net9.0-windows\Mindspace.exe'
if (Get-Process Mindspace -ErrorAction SilentlyContinue) {
    Start-Process $exe -ArgumentList 'quit' -Wait
    Start-Sleep -Milliseconds 900
    Get-Process Mindspace -ErrorAction SilentlyContinue | Stop-Process -Force
}
dotnet build (Join-Path $root 'src\Mindspace') -c Release -nologo -v q 2>&1 | Select-String ' error |rror\(s\)|warning CS' | Select-Object -Unique -First 12
