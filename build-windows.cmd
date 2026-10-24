@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo .NET Framework C# compiler not found. Install .NET Framework 4.8 Developer Pack.
  pause
  exit /b 1
)
if not exist release mkdir release
"%CSC%" /nologo /codepage:65001 /define:OPEN_SOURCE_EDITION /target:winexe /platform:x64 /optimize+ /win32manifest:app.manifest /win32icon:assets\app.ico /out:release\QuickReplyLite.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll /reference:System.Xml.dll /reference:System.IO.Compression.dll src\App.cs src\BangkefuReader.cs src\Core.cs src\DesktopShortcut.cs src\Dialogs.cs src\Editor.cs src\Interaction.cs src\Native.cs src\Startup.cs src\Transfer.cs src\TransferUI.cs src\WindowsDesktopShortcut.cs src\WindowsStartup.cs
if errorlevel 1 (
  pause
  exit /b 1
)
copy /y QuickReplyLite.exe.config release\QuickReplyLite.exe.config >nul
echo Built activation-free release\QuickReplyLite.exe
pause
