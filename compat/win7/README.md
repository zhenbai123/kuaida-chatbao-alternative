# Windows 7 portable compatibility build

The Windows 7 edition targets CLR 2.0 / .NET Framework 3.5.1, which is included in a standard Windows 7 installation. It does not require .NET Framework 4.8/4.8.1. The executable and both dependencies are AnyCPU for 32-bit and 64-bit Windows 7. A customized installation that removed or disabled Windows' built-in .NET 3.5.1 component must restore that Windows component first.

`build.py` generates compatibility sources from the shared application; it never rewrites `src/`. The compatibility build substitutes a WinForms message pump and worker for TPL/async, legacy DPI and durable flush helpers, and a streaming ZIP adapter. Clipboard/input stays on the STA UI thread. The existing cancellation and foreground-target checks remain in place. No private keys or activation logic are included.

The ExcelDataReader 3.6.0 net20 build and SharpZipLib 0.86.0 are isolated to this edition. Existing import size, path, checksum, missing-media warnings and duplicate checks remain in the shared library. Dependencies come from pinned NuGet packages with SHA-256 checks. The unmodified SharpZipLib source archive, GPL and linking exception accompany the user distribution; the main independent application remains MIT.

Build on a Windows developer machine with Python and the .NET Framework C# 5 compiler:

1. Run `powershell -ExecutionPolicy Bypass -File compat/win7/restore.ps1`.
2. Run `python compat/win7/build.py --compiler C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe --refs tools/win7-deps/net35/build/.NETFramework/v3.5 --excel tools/win7-deps/excel360/lib/net20/ExcelDataReader.dll --zip tools/win7-deps/sharpziplib086/lib/20/ICSharpCode.SharpZipLib.dll --verify-build`.
3. The normal application output is in `release/win7/`. Verification helpers in that directory are excluded from the user package.

The CI job compiles against Microsoft .NET 3.5 reference assemblies, audits all production assembly references and CLR headers, and runs core checks plus native UI/docking checks in both process architectures. If CLR2 is unavailable on a hosted runner, test harnesses use CLR4; the production app keeps a CLR2-only configuration. This is not a claim that the hosted runner runs Windows 7.
