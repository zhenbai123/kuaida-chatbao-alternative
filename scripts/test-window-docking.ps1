$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
New-Item -ItemType Directory -Force 'artifacts' | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = Get-ChildItem 'src/*.cs' | Sort-Object Name | ForEach-Object { $_.FullName }
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll','System.Xml.dll','System.IO.Compression.dll') | ForEach-Object { '/reference:' + $_ }
$harness = (Resolve-Path 'scripts/WindowDockingSmoke.cs').Path
& $compiler /nologo /codepage:65001 /target:exe /platform:x64 /optimize+ /main:QuickReplyLite.WindowDockingSmoke /win32manifest:app.manifest /win32icon:assets/app.ico /out:artifacts/WindowDockingSmoke.exe $references $sources $harness
if ($LASTEXITCODE -ne 0) { throw 'Windows docking integration harness compilation failed.' }
Copy-Item 'QuickReplyLite.exe.config' 'artifacts/WindowDockingSmoke.exe.config' -Force
& ./artifacts/WindowDockingSmoke.exe (Join-Path (Get-Location) 'docs/screenshots')
if ($LASTEXITCODE -ne 0) { throw 'Windows docking integration checks failed.' }
