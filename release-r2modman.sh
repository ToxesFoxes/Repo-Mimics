#!/usr/bin/env bash
# Build a Thunderstore-ready release zip for r2modman.
# Usage: ./release-r2modman.sh [Debug|Release]
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIGURATION="${1:-Release}"
R2DIR="$ROOT_DIR/r2modman"

# Read version from manifest.json
VERSION=$(grep '"version_number"' "$R2DIR/manifest.json" | sed 's/.*"\([0-9]*\.[0-9]*\.[0-9]*\)".*/\1/')
if [[ -z "$VERSION" ]]; then
  echo "ERROR: could not read version from r2modman/manifest.json"
  exit 1
fi

OUT_ZIP="$ROOT_DIR/Release-r2modman-$VERSION.zip"

# ── 1. Build ──────────────────────────────────────────────────────────────────
echo "[1/4] Building v$VERSION ($CONFIGURATION)..."
dotnet build "$ROOT_DIR/TFS_Mimics.csproj" -c "$CONFIGURATION"

DLL="$ROOT_DIR/bin/$CONFIGURATION/TFS_Mimics.dll"
if [[ ! -f "$DLL" ]]; then
  echo "ERROR: DLL not found: $DLL"
  exit 1
fi

# ── 2. Copy DLL ───────────────────────────────────────────────────────────────
echo "[2/4] Copying TFS_Mimics.dll -> r2modman/..."
cp "$DLL" "$R2DIR/TFS_Mimics.dll"

# ── 3. Copy README.md, strip Thunderstore badge line ─────────────────────────
echo "[3/4] Copying README.md (stripping Thunderstore badge)..."
grep -v 'img.shields.io/badge/Thunderstore' "$ROOT_DIR/README.md" > "$R2DIR/README.md"

# ── 4. Pack zip ──────────────────────────────────────────────────────────────
echo "[4/4] Packing Release-r2modman-$VERSION.zip..."
rm -f "$OUT_ZIP"

# Convert paths to Windows style for PowerShell
WIN_ZIP=$(cygpath -w "$OUT_ZIP" 2>/dev/null || echo "$OUT_ZIP" | sed 's|/c/|C:\\|;s|/|\\|g')
WIN_R2DIR=$(cygpath -w "$R2DIR" 2>/dev/null || echo "$R2DIR" | sed 's|/c/|C:\\|;s|/|\\|g')

powershell.exe -NoProfile -Command "
  \$files = @('TFS_Mimics.dll','manifest.json','icon.png','README.md') | ForEach-Object { Join-Path '$WIN_R2DIR' \$_ }
  Compress-Archive -Path \$files -DestinationPath '$WIN_ZIP' -Force
"

echo ""
echo "Done: $OUT_ZIP"
ls -lh "$OUT_ZIP"
