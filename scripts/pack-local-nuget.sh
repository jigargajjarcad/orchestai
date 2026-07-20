#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="$REPO_ROOT/artifacts/nupkgs"
VERSION="0.1.0-phase2"

rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR"

for PROJECT in Domain Application Infrastructure; do
  dotnet pack "$REPO_ROOT/src/OrchestAI.$PROJECT/OrchestAI.$PROJECT.csproj" \
    -c Release \
    -p:PackageVersion="$VERSION" \
    -o "$OUT_DIR"
done

echo ""
echo "Packed to $OUT_DIR:"
ls "$OUT_DIR"
