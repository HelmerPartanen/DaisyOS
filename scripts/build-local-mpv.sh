#!/usr/bin/env bash
# Optional development dependency; no root/system-package changes.
set -euo pipefail
version=0.41.0
prefix="${XDG_DATA_HOME:-$HOME/.local/share}/DaisyOS/mpv-gpu"
cache="${XDG_CACHE_HOME:-$HOME/.cache}/DaisyOS/mpv-build-$version"
mkdir -p "$cache/source"
if [[ ! -f "$cache/source/meson.build" ]]; then
  curl -fL "https://github.com/mpv-player/mpv/archive/refs/tags/v$version.tar.gz" -o "$cache/source.tar.gz"
  tar -xf "$cache/source.tar.gz" -C "$cache/source" --strip-components=1
fi
printf '%s  %s\n' ee21092a5ee427353392360929dc64645c54479aefdb5babc5cfbb5fad626209 "$cache/source.tar.gz" | sha256sum -c -
if [[ ! -f "$prefix/lib/pkgconfig/ffnvcodec.pc" ]]; then
  mkdir -p "$cache/nv-headers"
  curl -fL https://github.com/FFmpeg/nv-codec-headers/archive/refs/tags/n13.0.19.0.tar.gz -o "$cache/nv-headers.tar.gz"
  printf '%s  %s\n' 86d15d1a7c0ac73a0eafdfc57bebfeba7da8264595bf531cf4d8db1c22940116 "$cache/nv-headers.tar.gz" | sha256sum -c -
  tar -xf "$cache/nv-headers.tar.gz" -C "$cache/nv-headers" --strip-components=1
  make -C "$cache/nv-headers" PREFIX="$prefix" install
fi
export PKG_CONFIG_PATH="$prefix/lib/pkgconfig${PKG_CONFIG_PATH:+:$PKG_CONFIG_PATH}"
meson setup "$cache/build" "$cache/source" --prefix="$prefix" --buildtype=release \
  -Dgpl=false -Dlibmpv=true -Dlua=disabled -Djavascript=disabled -Dsixel=disabled \
  -Dx11=disabled -Dwayland=auto -Dlibarchive=disabled -Dlibbluray=disabled \
  -Ddvdnav=disabled -Dvapoursynth=disabled -Duchardet=disabled -Drubberband=disabled \
  -Dmanpage-build=disabled -Dhtml-build=disabled -Dcuda-hwaccel=enabled -Dcuda-interop=enabled
meson compile -C "$cache/build" -j 4
meson install -C "$cache/build"
mkdir -p "$prefix/share/source"
cp "$cache/source.tar.gz" "$prefix/share/source/"
cp "$cache/source/Copyright" "$prefix/share/source/mpv-Copyright"
