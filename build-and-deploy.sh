#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIGURATION="${1:-Release}"

DEFAULT_DEPLOY_DIR="/c/Users/ToxesFoxes/AppData/Roaming/r2modmanPlus-local/REPO/profiles/REPO v4/BepInEx/plugins/ToxesFoxes-Mimics"
DEPLOY_DIR="${DEPLOY_DIR:-$DEFAULT_DEPLOY_DIR}"

cd "$ROOT_DIR"

echo "[1/3] Building project ($CONFIGURATION)..."
dotnet build TFS_Mimics.csproj -c "$CONFIGURATION"

OUT_DIR="$ROOT_DIR/bin/$CONFIGURATION"
DLL_PATH="$OUT_DIR/TFS_Mimics.dll"
if [[ ! -f "$DLL_PATH" ]]; then
  DLL_PATH="$OUT_DIR/TFS_Mimics.dll"
fi

if [[ ! -f "$DLL_PATH" ]]; then
  echo "Build finished, but no DLL found in: $OUT_DIR"
  exit 1
fi

echo "[2/3] Ensuring deploy folder exists..."
mkdir -p "$DEPLOY_DIR"

echo "[3/3] Deploying $(basename "$DLL_PATH") to: $DEPLOY_DIR"
cp "$DLL_PATH" "$DEPLOY_DIR/"

echo "Done."
