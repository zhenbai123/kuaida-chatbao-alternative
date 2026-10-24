param([string]$Version = '1.7.1')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$stage = 'artifacts/Windows'
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item 'release/QuickReplyLite.exe','release/QuickReplyLite.exe.config','使用说明.txt','LICENSE.txt' $stage -Force
$zip = "artifacts/Kuaida-v$Version-Windows-x64.zip"
Compress-Archive -Path "$stage/QuickReplyLite.exe","$stage/QuickReplyLite.exe.config","$stage/使用说明.txt","$stage/LICENSE.txt" -DestinationPath $zip -Force
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  Kuaida-v$Version-Windows-x64.zip" | Set-Content 'artifacts/SHA256SUMS.txt' -Encoding ascii
Write-Host "Created $zip"
