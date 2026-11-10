#!/usr/bin/env bash
# Fetch the pinned Linux-hosted Windows x64 cross-compiler.
set -euo pipefail
version=20261104
name="llvm-mingw-$version-ucrt-ubuntu-22.04-x86_64"
folder=$(mkdir -p "$1" && cd "$1" && pwd)
cd "$folder"
archive="$name.tar.xz"
curl -fsSL --retry 3 --connect-timeout 15 --max-time 120 \
  "https://github.com/mstorsjo/llvm-mingw/releases/download/$version/$archive" -o "$archive"
echo "919380403de87dca3c581c2053230cd298af5e891286628ba0613b7fed370926  $archive" | sha256sum --check >&2
tar -xf "$archive"
printf '%s\n' "$folder/$name/bin"
