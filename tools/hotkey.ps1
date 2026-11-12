# Dev helper: presses a key chord the way a keyboard would, to test global hotkeys.
#   tools\hotkey.ps1 -Keys '18 16 69'   (decimal virtual-key codes: Alt Shift E)
param([string]$Keys)
Add-Type -Name K -Namespace N -MemberDefinition '[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);'
$codes = @($Keys.Split(' ') | Where-Object { $_ } | ForEach-Object { [byte]$_ })
foreach ($k in $codes) { [N.K]::keybd_event($k, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 30 }
[array]::Reverse($codes)
foreach ($k in $codes) { [N.K]::keybd_event($k, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 30 }
