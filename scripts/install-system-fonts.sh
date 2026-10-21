#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
case "${1:-}" in
  --user)
    font_dir="${XDG_DATA_HOME:-$HOME/.local/share}/fonts/DaisyOS/AtkinsonHyperlegible"
    config_dir="${XDG_CONFIG_HOME:-$HOME/.config}/fontconfig/conf.d"
    ;;
  --root)
    [[ $# == 2 && -n "$2" ]] || { echo 'Usage: install-system-fonts.sh --root PATH | --user' >&2; exit 2; }
    font_dir="$2/usr/share/fonts/daisyos/AtkinsonHyperlegible"
    config_dir="$2/etc/fonts/conf.d"
    ;;
  *) echo 'Usage: install-system-fonts.sh --root PATH | --user' >&2; exit 2 ;;
esac

# Skip unchanged files so development-session startup does not invalidate caches.
changed=false
install -d "$font_dir" "$config_dir"
for source_file in "$repo_root/src/DaisyOS.Shell/Assets/fonts/AtkinsonHyperlegible/"*.ttf \
                   "$repo_root/src/DaisyOS.Shell/Assets/fonts/AtkinsonHyperlegible/OFL.txt"; do
  destination="$font_dir/$(basename "$source_file")"
  if ! cmp -s "$source_file" "$destination"; then
    install -m644 "$source_file" "$destination"
    changed=true
  fi
done
inter_dir="${font_dir%/*}/Inter"
install -d "$inter_dir"
for inter_source in "$repo_root/src/DaisyOS.Shell/Assets/fonts/Inter/"*.ttf \
                    "$repo_root/src/DaisyOS.Shell/Assets/fonts/Inter-OFL.txt"; do
  destination="$inter_dir/$(basename "$inter_source")"
  if ! cmp -s "$inter_source" "$destination"; then
    install -m644 "$inter_source" "$destination"
    changed=true
  fi
done
config_source="$repo_root/packaging/fonts/60-daisyos-fonts.conf"
if ! cmp -s "$config_source" "$config_dir/60-daisyos-fonts.conf"; then
  install -m644 "$config_source" "$config_dir/60-daisyos-fonts.conf"
  changed=true
fi
if [[ "$1" == --root ]]; then
  install -d "$2/etc/xdg"
  for font_key in font menuFont toolBarFont activeFont; do
    kwriteconfig6 --file "$2/etc/xdg/kdeglobals" --group General --key "$font_key" 'Inter,10,-1,5,50,0,0,0,0,0'
  done
  kwriteconfig6 --file "$2/etc/xdg/kdeglobals" --group General --key smallestReadableFont 'Inter,8,-1,5,50,0,0,0,0,0'
  for gtk_version in 3.0 4.0; do
    install -d "$2/etc/gtk-$gtk_version"
    kwriteconfig6 --file "$2/etc/gtk-$gtk_version/settings.ini" --group Settings --key gtk-font-name 'Inter 10'
  done

  install -Dm644 "$repo_root/packaging/fonts/90-daisyos.gschema.override" \
    "$2/usr/share/glib-2.0/schemas/90-daisyos.gschema.override"
fi
if [[ "$1" == --user && "$changed" == true ]]; then
  fc-cache -f "${font_dir%/*}"
fi
