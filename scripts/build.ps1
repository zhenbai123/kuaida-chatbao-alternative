param([string]$OutputDirectory = 'release')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
& (Join-Path $PSScriptRoot 'restore-excel-reader.ps1')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) { throw '.NET Framework C# compiler not found. Install .NET Framework 4.8 Developer Pack.' }
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$sources = Get-ChildItem 'src/*.cs' | Sort-Object Name | ForEach-Object { $_.FullName }
$references = @('System.Data.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll','System.Xml.dll','System.IO.Compression.dll') | ForEach-Object { '/reference:' + $_ }
$references += '/reference:vendor/ExcelDataReader/net462/ExcelDataReader.dll'
& $compiler /nologo /codepage:65001 /define:OPEN_SOURCE_EDITION /target:winexe /platform:x64 /optimize+ /win32manifest:app.manifest /win32icon:assets/app.ico "/out:$OutputDirectory/QuickReplyLite.exe" $references $sources
if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $LASTEXITCODE" }
Copy-Item 'QuickReplyLite.exe.config' $OutputDirectory -Force
Write-Host "Built $OutputDirectory/QuickReplyLite.exe"

Copy-Item vendor/ExcelDataReader/net462/ExcelDataReader.dll $OutputDirectory -Force
