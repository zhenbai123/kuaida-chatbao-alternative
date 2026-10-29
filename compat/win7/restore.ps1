$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$cache = Join-Path $root 'tools/win7-deps'
New-Item -ItemType Directory -Force $cache | Out-Null
$items = @(
    @{ Name='net35'; Url='https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net35/1.0.3/microsoft.netframework.referenceassemblies.net35.1.0.3.nupkg'; Hash='b16156111a88670d91a757fbd465fcb4856e034b1a4d523ae2c41a470f3578f9' },
    @{ Name='excel360'; Url='https://api.nuget.org/v3-flatcontainer/exceldatareader/3.6.0/exceldatareader.3.6.0.nupkg'; Hash='1db7c885125a4d3f98ca769793335a593e69c2f5598a4fc9dbd541d719603456' },
    @{ Name='sharpziplib086'; Url='https://api.nuget.org/v3-flatcontainer/sharpziplib/0.86.0/sharpziplib.0.86.0.nupkg'; Hash='75045aa2148e25cb895bcca67cf39083a98c808a5bf7fd58d84e08f582787b9e' },
    @{ Name='sharpziplib-source'; Url='https://downloads.sourceforge.net/project/sharpdevelop/SharpZipLib/0.86/SharpZipLib_0860_SourceSamples.zip'; Hash='d0ac6da32f8f2abd24a3588c6d2fceff46292b1cc4c3037440f7ce4e55162096' }
)
foreach ($item in $items) {
    $archive = Join-Path $cache ($item.Name + '.zip')
    if (-not (Test-Path $archive)) { Invoke-WebRequest -UseBasicParsing -Uri $item.Url -OutFile $archive -TimeoutSec 120 }
    if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.Hash) { throw "Dependency checksum mismatch: $($item.Name)" }
    if ($item.Name -ne 'sharpziplib-source') { Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $cache $item.Name) -Force }
}
