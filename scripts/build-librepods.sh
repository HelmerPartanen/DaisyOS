#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="${1:-$repo_root/artifacts/librepods}"
revision="$(cat "$repo_root/packaging/librepods/revision.txt")"
fingerprint="$(cat "$repo_root/packaging/librepods/revision.txt" "$repo_root/packaging/librepods/patch-source.py" "$repo_root/packaging/librepods/shell-bridge.hpp" "$repo_root/scripts/build-librepods.sh" | sha256sum | cut -d' ' -f1)"

# Ensure Qt6 Bluetooth headers/libraries are available; provision into ~/.local automatically if missing
if [[ ! -d /usr/lib/cmake/Qt6Bluetooth && ! -d "$HOME/.local/lib/cmake/Qt6Bluetooth" ]]; then
  echo "Provisioning local Qt6 connectivity module..."
  local_pkg_tmp="$(mktemp -d)"
  if curl -sSL "https://fastly.mirror.pkgbuild.com/extra/os/x86_64/qt6-connectivity-6.12.0-1-x86_64.pkg.tar.zst" 2>/dev/null | tar -x --zstd -C "$local_pkg_tmp" 2>/dev/null; then
    if [[ -d "$local_pkg_tmp/usr" ]]; then
      mkdir -p "$HOME/.local/lib" "$HOME/.local/include" "$HOME/.local/share"
      cp -a "$local_pkg_tmp/usr/lib/"* "$HOME/.local/lib/" 2>/dev/null || true
      cp -a "$local_pkg_tmp/usr/include/"* "$HOME/.local/include/" 2>/dev/null || true
    fi
  fi
  rm -rf "$local_pkg_tmp"
fi

if [[ -x "$output/librepods" && -s "$output/source.tar.gz" && -f "$output/build-fingerprint.txt" && "$(cat "$output/build-fingerprint.txt")" == "$fingerprint" ]]; then
  echo "LibrePods companion is up to date: $output"
  if [[ "$output" != "$HOME/.local/lib/daisyos" && -f "$output/librepods" ]]; then
    mkdir -p "$HOME/.local/lib/daisyos"
    install -m755 "$output/librepods" "$HOME/.local/lib/daisyos/librepods"
  fi
  exit 0
fi

build_root="$(mktemp -d)"
trap 'rm -rf "$build_root"' EXIT
# No moving branch or downloaded executable: build the reviewed revision.
git init -q "$build_root/source"
git -C "$build_root/source" fetch -q --depth 1 https://github.com/librepods-org/librepods.git "$revision"
git -C "$build_root/source" checkout -q --detach FETCH_HEAD
[[ "$(git -C "$build_root/source" rev-parse HEAD)" == "$revision" ]]
cp "$repo_root/packaging/librepods/shell-bridge.hpp" "$build_root/source/linux/"
python3 "$repo_root/packaging/librepods/patch-source.py" "$build_root/source"
cmake -S "$build_root/source/linux" -B "$build_root/build" \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_PREFIX_PATH="${CMAKE_PREFIX_PATH:-$HOME/.local;/usr}" \
  -DCMAKE_INSTALL_RPATH="$HOME/.local/lib:/usr/lib" \
  -DCMAKE_BUILD_WITH_INSTALL_RPATH=ON
cmake --build "$build_root/build" --target librepods --parallel "${DAISYOS_BUILD_JOBS:-2}"
mkdir -p "$output"
install -m755 "$build_root/build/librepods" "$output/librepods"
install -m644 "$build_root/source/LICENSE" "$output/LICENSE"
cp "$repo_root/packaging/librepods/revision.txt" "$output/"
# Supply corresponding modified source alongside distributed GPL binaries.
tar -C "$build_root/source" --exclude=.git -czf "$output/source.tar.gz" linux LICENSE
printf '%s\n' "$fingerprint" > "$output/build-fingerprint.txt"

# Keep user-level install up to date in ~/.local/lib/daisyos
if [[ "$output" != "$HOME/.local/lib/daisyos" ]]; then
  mkdir -p "$HOME/.local/lib/daisyos"
  install -m755 "$output/librepods" "$HOME/.local/lib/daisyos/librepods"
fi

# Ensure user systemd service is active for this session
user_unit_dir="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"
mkdir -p "$user_unit_dir" "$user_unit_dir/graphical-session.target.wants"
cat <<'EOF' > "$user_unit_dir/daisyos-librepods.service"
[Unit]
Description=DaisyOS AirPods companion
PartOf=graphical-session.target
After=graphical-session.target

[Service]
ExecStart=/bin/sh -c 'if [ -x /usr/lib/daisyos/librepods ]; then exec /usr/lib/daisyos/librepods --hide; else exec %h/.local/lib/daisyos/librepods --hide; fi'
Restart=on-failure
RestartSec=5
Environment=QT_QPA_PLATFORM=offscreen
UMask=0077

[Install]
WantedBy=graphical-session.target
EOF
ln -sfn ../daisyos-librepods.service "$user_unit_dir/graphical-session.target.wants/daisyos-librepods.service"
systemctl --user daemon-reload 2>/dev/null || true
systemctl --user start daisyos-librepods.service 2>/dev/null || true

