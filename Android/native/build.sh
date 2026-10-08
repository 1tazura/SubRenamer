#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
output="${1:?output directory required}"
platform="${2:-host}"
revision=fc710a3ebd58a3c15d0ef24322f748e38a3d3a90
source="${SMB2_SOURCE:-$here/.build/source}"
if ! test -d "$source/.git"; then
  mkdir -p "$source"
  git -C "$source" init -q
  git -C "$source" remote add origin https://github.com/sahlberg/libsmb2.git
fi
if test "$(git -C "$source" rev-parse HEAD 2>/dev/null || true)" != "$revision"; then
  git -C "$source" fetch --depth 1 origin "$revision"
  git -C "$source" checkout --detach "$revision"
fi
mkdir -p "$output"
output="$(cd "$output" && pwd)"
args=(-DSMB2_SOURCE="$source" -DCMAKE_BUILD_TYPE=Release -DCMAKE_POLICY_VERSION_MINIMUM=3.5)
if test "$platform" = android; then
  : "${ANDROID_NDK_HOME:?Set ANDROID_NDK_HOME to an installed Android NDK}"
  args+=(-DCMAKE_TOOLCHAIN_FILE="$ANDROID_NDK_HOME/build/cmake/android.toolchain.cmake"
         -DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-23
         '-DCMAKE_SHARED_LINKER_FLAGS=-Wl,-z,max-page-size=16384')
fi
cmake -S "$here" -B "$output/build" -G Ninja "${args[@]}"
cmake --build "$output/build" --target subrenamersmb -j 4
cp "$output/build/libsubrenamersmb.so" "$output/build/smb2/lib/libsmb2.so" "$output/"
cp "$source/LICENCE-LGPL-2.1.txt" "$output/"
# Provide the corresponding upstream source alongside distributed native binaries.
git -C "$source" archive --format=tar.gz -o "$output/libsmb2-source.tar.gz" "$revision"
