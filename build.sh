#!/usr/bin/env bash
# Builds the mod and packs a Thunderstore/r2modman zip into dist/.
# Needs the .NET 8 SDK (https://dot.net). Game libraries come from NuGet, no game install needed.
set -euo pipefail
cd "$(dirname "$0")"

VERSION=$(python3 -c "import json;print(json.load(open('Thunderstore/manifest.json'))['version_number'])" 2>/dev/null || grep -o '"version_number": *"[^"]*"' Thunderstore/manifest.json | cut -d'"' -f4)
OUT=src/CalamityMusic/bin/Release/netstandard2.1
STAGE=dist/stage

dotnet build src/CalamityMusic/CalamityMusic.csproj -c Release

rm -rf "$STAGE"
mkdir -p "$STAGE/plugins/CalamityMusic"
cp Thunderstore/manifest.json Thunderstore/icon.png README.md CHANGELOG.md "$STAGE/"
cp "$OUT/CalamityMusic.dll" "$OUT/NLayer.dll" "$OUT/NVorbis.dll" Thunderstore/icon.png "$STAGE/plugins/CalamityMusic/"

ZIP="dist/CalamityMusic-$VERSION.zip"
rm -f "$ZIP"
(cd "$STAGE" && zip -qr "../$(basename "$ZIP")" .)
echo "Packed $ZIP"
