#!/usr/bin/env bash
# Usage:
#   ./bump-version.sh 1.2.3        — set explicit version
#   ./bump-version.sh patch        — bump X.Y.Z -> X.Y.(Z+1)
#   ./bump-version.sh minor        — bump X.Y.Z -> X.(Y+1).0
#   ./bump-version.sh major        — bump X.Y.Z -> (X+1).0.0
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MANIFEST="$ROOT_DIR/r2modman/manifest.json"

# ── read current version ──────────────────────────────────────────────────────
CURRENT=$(grep '"version_number"' "$MANIFEST" | sed 's/.*"\([0-9]*\.[0-9]*\.[0-9]*\)".*/\1/')
if [[ -z "$CURRENT" ]]; then
  echo "ERROR: could not read current version from $MANIFEST"
  exit 1
fi

IFS='.' read -r MAJOR MINOR PATCH <<< "$CURRENT"

# ── compute new version ───────────────────────────────────────────────────────
ARG="${1:-}"
case "$ARG" in
  patch) PATCH=$((PATCH + 1)) ;;
  minor) MINOR=$((MINOR + 1)); PATCH=0 ;;
  major) MAJOR=$((MAJOR + 1)); MINOR=0; PATCH=0 ;;
  [0-9]*.[0-9]*.[0-9]*)
    IFS='.' read -r MAJOR MINOR PATCH <<< "$ARG"
    ;;
  *)
    echo "Usage: $0 <major|minor|patch|X.Y.Z>"
    echo "Current version: $CURRENT"
    exit 1
    ;;
esac

NEW="$MAJOR.$MINOR.$PATCH"

if [[ "$NEW" == "$CURRENT" ]]; then
  echo "Version is already $CURRENT — nothing to do."
  exit 0
fi

echo "Bumping version: $CURRENT -> $NEW"

# ── helper: in-place sed (portable for Git Bash on Windows) ──────────────────
replace() {
  local file="$1" old="$2" new="$3"
  if ! grep -qF "$old" "$file"; then
    echo "  WARN: pattern not found in $file  →  $old"
    return
  fi
  # Use a temp file so we don't need GNU sed -i on every platform
  local tmp
  tmp=$(mktemp)
  sed "s|${old}|${new}|g" "$file" > "$tmp" && mv "$tmp" "$file"
  echo "  updated: $file"
}

# ── apply replacements ────────────────────────────────────────────────────────
replace "$ROOT_DIR/src/Plugin.cs" \
  "\"TFS_Mimics\", \"TFS_Mimics\", \"$CURRENT\"" \
  "\"TFS_Mimics\", \"TFS_Mimics\", \"$NEW\""

replace "$MANIFEST" \
  "\"version_number\": \"$CURRENT\"" \
  "\"version_number\": \"$NEW\""

# mm_v2_manifest.json has separate major/minor/patch fields
MM2="$ROOT_DIR/r2modman/mm_v2_manifest.json"
tmp=$(mktemp)
sed \
  -e "s|\"major\": [0-9]*|\"major\": $MAJOR|" \
  -e "s|\"minor\": [0-9]*|\"minor\": $MINOR|" \
  -e "s|\"patch\": [0-9]*|\"patch\": $PATCH|" \
  "$MM2" > "$tmp" && mv "$tmp" "$MM2"
echo "  updated: $MM2"

echo "Done. New version: $NEW"
