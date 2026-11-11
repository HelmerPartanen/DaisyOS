#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "$0")/.." && pwd)"
source_root="$repo_root"
if [[ "${1:-}" == --repo && $# -eq 2 ]]; then source_root="$2"; else
  [[ $# -eq 0 ]] || { echo 'Usage: install-user-gamingd.sh [--repo CHECKOUT]' >&2; exit 2; }
fi
source_root="$(cd "$source_root" && pwd)"
crate="$source_root/services/DaisyGaming/rust"
binary_source="$crate/target/release/daisy-gamingd"
binary_target="${XDG_DATA_HOME:-$HOME/.local/share}/DaisyOS/bin/daisy-gamingd"
unit_dir="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"
dbus_dir="${XDG_DATA_HOME:-$HOME/.local/share}/dbus-1/services"
unit_target="$unit_dir/daisyos-gamingd.service"
dbus_target="$dbus_dir/org.daisyos.Gaming1.service"

if [[ ! -x "$binary_source" ]] || find "$crate" -path '*/target' -prune -o -type f \( -name '*.rs' -o -name Cargo.toml -o -name Cargo.lock \) -newer "$binary_source" -print -quit | grep -q .; then
  "$source_root/scripts/build-daisy-gamingd.sh"
fi

changed=false
install -d "$unit_dir" "$dbus_dir" "$(dirname "$binary_target")"
if [[ ! -x "$binary_target" ]] || ! cmp -s "$binary_source" "$binary_target"; then
  install -m755 "$binary_source" "$binary_target"
  changed=true
fi
temporary_unit="$(mktemp "$unit_target.tmp.XXXXXX")"
sed "s|^ExecStart=.*|ExecStart=$binary_target|" "$source_root/packaging/gaming/daisyos-gamingd.service" > "$temporary_unit"
if ! cmp -s "$temporary_unit" "$unit_target"; then install -m644 "$temporary_unit" "$unit_target"; changed=true; fi
rm -f "$temporary_unit"
temporary_dbus="$(mktemp "$dbus_target.tmp.XXXXXX")"
sed "s|^Exec=.*|Exec=$binary_target|" "$source_root/packaging/gaming/org.daisyos.Gaming1.service" > "$temporary_dbus"
if ! cmp -s "$temporary_dbus" "$dbus_target"; then install -m644 "$temporary_dbus" "$dbus_target"; changed=true; fi
rm -f "$temporary_dbus"

if [[ "$changed" == true ]]; then
  systemctl --user daemon-reload
  if systemctl --user is-active --quiet daisyos-gamingd.service; then
    systemctl --user restart daisyos-gamingd.service
  fi
fi
