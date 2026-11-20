#!/usr/bin/env bash
set -euo pipefail

rid="${1:?Usage: bash scripts/package-desktop.sh <rid> <output-directory>}"
output="${2:?Missing output directory}"
case "$rid" in
  linux-x64|linux-arm64|osx-arm64|osx-x64) ;;
  *) echo "Unsupported runtime: $rid" >&2; exit 2 ;;
esac

root="$(cd -- "$(dirname -- "$0")/.." && pwd)"
mkdir -p -- "$output"
output="$(cd -- "$output" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf -- "$work"' EXIT
export HAPPY_NEW_ISLAND_PROFILE="$work/smoke-profile"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

name=HappyNewIsland
version="$(dotnet msbuild "$root/desktop/HappyNewIsland.csproj" -getProperty:Version -nologo)"
if [[ "$rid" == osx-* ]]; then
  bundle="$work/Happy New Island.app"
  app="$bundle/Contents/MacOS"
  mkdir -p -- "$app" "$bundle/Contents/Resources"
  cp -- "$root/assets/app-icon.icns" "$bundle/Contents/Resources/AppIcon.icns"
  sed "s/@VERSION@/$version/g" "$root/desktop/Info.plist" > "$bundle/Contents/Info.plist"
else
  app="$work/$name"
fi

dotnet publish "$root/desktop/HappyNewIsland.csproj" -c Release -r "$rid" \
  --self-contained true -p:DebugType=None -p:DebugSymbols=false -o "$app"
cp -- "$root/THIRD-PARTY-NOTICES.md" "$app/"
cp -R "$root/licenses" "$app/licenses"

if [[ "$rid" == osx-* ]]; then
  plutil -lint "$bundle/Contents/Info.plist"
  codesign --force --deep --sign - "$bundle"
  codesign --verify --deep --strict "$bundle"
  perl -e 'alarm shift; exec @ARGV' 45 "$app/$name" --smoke-test
  arch="${rid#osx-}"
  ditto -c -k --sequesterRsrc --keepParent "$bundle" "$output/$name-macOS-$arch.zip"
else
  timeout 45s xvfb-run -a "$app/$name" --smoke-test
  tar -czf "$output/$name-$rid.tar.gz" -C "$work" "$name"
fi
