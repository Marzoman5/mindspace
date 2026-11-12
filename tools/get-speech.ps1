# One-time voice setup. Downloads the open-source whisper.cpp speech engine (MIT licence) and one speech model
# from their official release pages, checks each file against a pinned SHA-256, and puts them in
# %LocalAppData%\Mindspace\speech. Mindspace itself never downloads anything; this script is the only place that does.
#   tools\get-speech.ps1            fast model (base.en, 141 MB)
#   tools\get-speech.ps1 -Accurate  also fetches the more accurate model (small.en, 465 MB)
param([switch]$Accurate)
$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:LOCALAPPDATA 'Mindspace\speech'
$tmp = Join-Path $env:TEMP ("mindspace-speech-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $dest, $tmp | Out-Null

function Get-Checked($url, $file, $sha256) {
    if ((Test-Path $file) -and (Get-FileHash $file -Algorithm SHA256).Hash -eq $sha256) { return }
    Write-Host "Downloading $url"
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest $url -OutFile $file -UseBasicParsing
    $got = (Get-FileHash $file -Algorithm SHA256).Hash
    if ($got -ne $sha256) { Remove-Item $file; throw "Checksum mismatch for $url (got $got). Nothing was installed from it." }
}

try {
    # Engine: whisper.cpp build b5454 (the Windows binaries for v1.9.5), from the project's own GitHub releases.
    $zip = Join-Path $tmp 'whisper-bin-x64.zip'
    Get-Checked 'https://github.com/ggml-org/whisper.cpp/releases/download/b5454/whisper-bin-x64.zip' $zip '6BA69E3482D7826214F90A6A9C84CA07782AEC1E1D0C6A7C30C994FD5D816CCB'
    Expand-Archive $zip -DestinationPath (Join-Path $tmp 'x') -Force
    $bin = (Get-ChildItem (Join-Path $tmp 'x') -Recurse -Filter 'whisper-cli.exe' | Select-Object -First 1).DirectoryName
    foreach ($f in 'whisper-cli.exe', 'whisper.dll', 'ggml.dll', 'ggml-base.dll', 'ggml-cpu-x64.dll', 'ggml-cpu-haswell.dll', 'ggml-cpu-alderlake.dll') {
        Copy-Item (Join-Path $bin $f) $dest -Force
    }
    # Models: from the whisper.cpp author's Hugging Face repository.
    Get-Checked 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin' (Join-Path $dest 'ggml-base.en.bin') 'A03779C86DF3323075F5E796CB2CE5029F00EC8869EEE3FDFB897AFE36C6D002'
    if ($Accurate) {
        Get-Checked 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin' (Join-Path $dest 'ggml-small.en.bin') 'C6138D6D58ECC8322097E0F987C32F1BE8BB0A18532A3F88F734D1BBF9C41E5D'
    }
    "Voice is ready: $dest"
    "Hold Alt + Shift + D and speak. Restart Mindspace if it was already running."
}
finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
