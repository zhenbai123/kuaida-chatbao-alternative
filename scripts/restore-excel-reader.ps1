$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$vendor = Join-Path $root 'vendor/ExcelDataReader'
$expected = @{
    'net462' = '03050036badc7651f357a57cdc150451fc021cdb56e31918f6a92d8d6629b844'
    'net8.0' = '6a9ad35cc806ed39ac7cd5c83ba470d980f2d665cbf6e7ec413189f04898e84d'
}
$ready = $true
foreach ($target in $expected.Keys) {
    $file = Join-Path $vendor "$target/ExcelDataReader.dll"
    if (-not (Test-Path $file)) { $ready = $false }
    elseif ((Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected[$target]) { $ready = $false }
}
if ($ready) { Write-Host 'ExcelDataReader 3.9.0 already verified.'; return }
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('kuaida-excel-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $archive = Join-Path $temporary 'ExcelDataReader.zip'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/exceldatareader/3.9.0/exceldatareader.3.9.0.nupkg' -OutFile $archive -TimeoutSec 120
    if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'a59df6b86a01cf67e45a6680f288a6ccc91b5a473232f76f00cb5104daa1543b') { throw 'ExcelDataReader package checksum mismatch.' }
    $unpacked = Join-Path $temporary 'unpacked'
    Expand-Archive -LiteralPath $archive -DestinationPath $unpacked
    foreach ($target in $expected.Keys) {
        $file = Join-Path $unpacked "lib/$target/ExcelDataReader.dll"
        if ((Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected[$target]) { throw "ExcelDataReader $target DLL checksum mismatch." }
    }
    foreach ($target in $expected.Keys) {
        New-Item -ItemType Directory -Force (Join-Path $vendor $target) | Out-Null
        Copy-Item (Join-Path $unpacked "lib/$target/ExcelDataReader.dll") (Join-Path $vendor "$target/ExcelDataReader.dll") -Force
    }
    Write-Host 'Restored and verified ExcelDataReader 3.9.0.'
} finally { Remove-Item -LiteralPath $temporary -Recurse -Force }
