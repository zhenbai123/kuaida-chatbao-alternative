$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
& (Join-Path $PSScriptRoot 'restore-excel-reader.ps1')
New-Item -ItemType Directory -Force 'artifacts' | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = Get-ChildItem 'src/*.cs' | Sort-Object Name | ForEach-Object { $_.FullName }
$references = @('System.Data.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll','System.Xml.dll','System.IO.Compression.dll') | ForEach-Object { '/reference:' + $_ }
$references += '/reference:vendor/ExcelDataReader/net462/ExcelDataReader.dll'
$harness = (Resolve-Path 'scripts/Screenshots.cs').Path
& $compiler /nologo /codepage:65001 /target:exe /platform:x64 /optimize+ /main:QuickReplyLite.ScreenshotProgram /win32manifest:app.manifest /win32icon:assets/app.ico /out:artifacts/Screenshots.exe $references $sources $harness
if ($LASTEXITCODE -ne 0) { throw 'Screenshot harness compilation failed.' }
Copy-Item 'QuickReplyLite.exe.config' 'artifacts/Screenshots.exe.config' -Force
Copy-Item vendor/ExcelDataReader/net462/ExcelDataReader.dll artifacts -Force
& ./artifacts/Screenshots.exe (Join-Path (Get-Location) 'docs/screenshots')
if ($LASTEXITCODE -ne 0) { throw 'Screenshot harness failed.' }
foreach ($name in @('main','editor','batch-replace','import','import-missing','composition')) {
    if (-not (Test-Path "docs/screenshots/$name.png")) { throw "Missing screenshot: $name" }
}
