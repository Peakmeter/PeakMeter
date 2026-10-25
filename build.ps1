# Builds PeakMeter and packs it as dist\PeakMeter-v<version>-win-x64.zip, ready to share.
# Needs the .NET 10 SDK.  Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root 'src\PeakMeter\PeakMeter.csproj'
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$out = Join-Path $root 'release'
$dist = Join-Path $root 'dist'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish $proj -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true -p:DebugType=none -o $out -nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

Copy-Item (Join-Path $root 'LICENSE') $out
Copy-Item (Join-Path $root 'LIESMICH.txt') $out
Copy-Item (Join-Path $root 'installer\install.ps1') $out
Copy-Item (Join-Path $root 'installer\PeakMeter installieren.cmd') $out

New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist "PeakMeter-v$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
"$hash  $(Split-Path $zip -Leaf)" | Set-Content (Join-Path $dist "PeakMeter-v$version-win-x64.sha256.txt")
Write-Host "Fertig: $zip"
Write-Host "SHA-256: $hash"
