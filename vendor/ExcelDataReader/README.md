# ExcelDataReader 3.9.0 (MIT)

Upstream: https://github.com/ExcelDataReader/ExcelDataReader

Pinned upstream commit: f3343f5524518923bdeb4801e5003c0417fd35f3

Windows builds automatically run `scripts/restore-excel-reader.ps1`. The script downloads the official NuGet package and verifies its SHA-256 and both DLL hashes before use. The DLLs are build outputs excluded from Git; the packaged application contains the net462 DLL and this library's MIT license. The application does not download anything when run.

Core tests use the matching pinned NuGet PackageReference for .NET 8.

Package URL: https://api.nuget.org/v3-flatcontainer/exceldatareader/3.9.0/exceldatareader.3.9.0.nupkg

Package SHA-256: a59df6b86a01cf67e45a6680f288a6ccc91b5a473232f76f00cb5104daa1543b

net462 DLL SHA-256: 03050036badc7651f357a57cdc150451fc021cdb56e31918f6a92d8d6629b844

net8.0 DLL SHA-256: 6a9ad35cc806ed39ac7cd5c83ba470d980f2d665cbf6e7ec413189f04898e84d
