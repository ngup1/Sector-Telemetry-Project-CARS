#!/usr/bin/env bash
# Builds the packaged desktop app into dist/: a self-contained executable plus its wwwroot folder, zipped.
# Usage: ./publish.sh [runtime]   (default win-x64; e.g. osx-arm64 for Apple Silicon)
set -euo pipefail
rid="${1:-win-x64}"
out="dist/Sector-Telemetry-$rid"
rm -rf "$out" "$out.zip"
dotnet publish -c Release -r "$rid" --self-contained -p:PublishSingleFile=true -o "$out"
rm -f "$out"/*.staticwebassets.endpoints.json
(cd dist && zip -qr "Sector-Telemetry-$rid.zip" "Sector-Telemetry-$rid")
echo "Built $out.zip"
