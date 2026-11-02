# Builds the mod and packs a Thunderstore/r2modman zip into dist\.
# Needs the .NET 8 SDK (https://dot.net). Game libraries come from NuGet, no game install needed.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$version = (Get-Content Thunderstore\manifest.json | ConvertFrom-Json).version_number
$out = "src\CalamityMusic\bin\Release\netstandard2.1"
$stage = "dist\stage"

dotnet build src\CalamityMusic\CalamityMusic.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force "$stage\plugins\CalamityMusic" | Out-Null
Copy-Item Thunderstore\manifest.json, Thunderstore\icon.png, README.md, CHANGELOG.md $stage
Copy-Item "$out\CalamityMusic.dll", "$out\NLayer.dll", "$out\NVorbis.dll", Thunderstore\icon.png "$stage\plugins\CalamityMusic"

$zip = "dist\CalamityMusic-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Write-Host "Packed $zip"
