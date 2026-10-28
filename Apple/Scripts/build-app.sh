#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
BUILD_DIR="$PROJECT_DIR/.build"
OUTPUT_ARCHIVE="${1:-$PROJECT_DIR/dist/SHD-macOS-arm64.zip}"
STAGE_DIR="$(mktemp -d "${TMPDIR:-/tmp}/shd-app.XXXXXX")"
STAGE_APP="$STAGE_DIR/SHD.app"
VERIFY_DIR="$STAGE_DIR/verify"

cleanup() {
    rm -rf "$STAGE_DIR"
}
trap cleanup EXIT

if [[ -e "$OUTPUT_ARCHIVE" ]]; then
    echo "Выходной файл уже существует: $OUTPUT_ARCHIVE" >&2
    echo "Удалите его или укажите другой путь." >&2
    exit 1
fi

cd "$PROJECT_DIR"
swift build -c release

mkdir -p "$STAGE_APP/Contents/MacOS" "$STAGE_APP/Contents/Resources"
cp "$BUILD_DIR/release/SHD" "$STAGE_APP/Contents/MacOS/SHD"
chmod +x "$STAGE_APP/Contents/MacOS/SHD"
cp "$PROJECT_DIR/Resources/Info.plist" "$STAGE_APP/Contents/Info.plist"
printf 'APPL????' > "$STAGE_APP/Contents/PkgInfo"

if [[ -f "$PROJECT_DIR/Resources/AppIcon.icns" ]]; then
    cp "$PROJECT_DIR/Resources/AppIcon.icns" "$STAGE_APP/Contents/Resources/AppIcon.icns"
fi

xattr -cr "$STAGE_APP"
codesign --force --deep --sign - "$STAGE_APP"
codesign --verify --deep --strict --verbose=2 "$STAGE_APP"

mkdir -p "$(dirname "$OUTPUT_ARCHIVE")"
ditto -c -k --sequesterRsrc --keepParent "$STAGE_APP" "$OUTPUT_ARCHIVE"
mkdir -p "$VERIFY_DIR"
ditto -x -k "$OUTPUT_ARCHIVE" "$VERIFY_DIR"
codesign --verify --deep --strict --verbose=2 "$VERIFY_DIR/SHD.app"
echo "Собран архив приложения: $OUTPUT_ARCHIVE"
