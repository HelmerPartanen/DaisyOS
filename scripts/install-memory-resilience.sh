#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
target_root="/"
disk_swap_gib=""
install_package=1
activate=1

usage() {
  cat <<'EOF'
Usage: scripts/install-memory-resilience.sh [--root PATH] [--disk-swap-gib N] [--no-disk-swap] [--no-package] [--no-activate]

Installs DaisyOS zram, systemd-oomd, sysctl, health-check, and optional
low-priority disk swap configuration. The default real-system fallback is 4 GiB.
Staging roots never create or activate a swap file.
EOF
}

while (($#)); do
  case "$1" in
    --root) [[ $# -ge 2 ]] || { echo "--root requires a path" >&2; exit 2; }; target_root="$2"; shift 2 ;;
    --disk-swap-gib) [[ $# -ge 2 ]] || { echo "--disk-swap-gib requires a size" >&2; exit 2; }; disk_swap_gib="$2"; shift 2 ;;
    --no-disk-swap) disk_swap_gib=0; shift ;;
    --no-package) install_package=0; shift ;;
    --no-activate) activate=0; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

[[ "$target_root" == /* ]] || { echo "--root must be an absolute path" >&2; exit 2; }
if [[ -n "$disk_swap_gib" ]]; then
  [[ "$disk_swap_gib" =~ ^[0-9]+$ ]] || { echo "Disk swap size must be a whole number of GiB." >&2; exit 2; }
  (( disk_swap_gib <= 32 )) || { echo "Disk swap fallback is capped at 32 GiB." >&2; exit 2; }
fi
if [[ "$target_root" == / ]]; then
  (( EUID == 0 )) || { echo "Run this installer as root." >&2; exit 1; }
  disk_swap_gib="${disk_swap_gib:-4}"
else
  mkdir -p "$target_root"
  target_root="$(cd "$target_root" && pwd -P)"
  disk_swap_gib=0
  install_package=0
  activate=0
fi
[[ "$disk_swap_gib" =~ ^[0-9]+$ ]] || { echo "Disk swap size must be a whole number of GiB." >&2; exit 2; }

if (( install_package )) && [[ ! -x /usr/lib/systemd/system-generators/zram-generator ]] \
  && ! systemctl cat systemd-zram-setup@.service >/dev/null 2>&1; then
  if command -v pacman >/dev/null 2>&1; then
    pacman -S --needed --noconfirm zram-generator
  else
    echo "Install systemd zram-generator before activating DaisyOS memory resilience." >&2
    exit 1
  fi
fi

install -Dm644 "$repo_root/scripts/memory/zram-generator.conf" \
  "$target_root/etc/systemd/zram-generator.conf.d/90-daisyos.conf"
install -Dm644 "$repo_root/scripts/memory/10-daisyos-oomd.conf" "$target_root/etc/systemd/oomd.conf.d/10-daisyos-oomd.conf"
install -Dm644 "$repo_root/scripts/memory/90-daisyos-memory.conf" "$target_root/etc/sysctl.d/90-daisyos-memory.conf"
install -Dm755 "$repo_root/scripts/memory/daisyos-memory-health" "$target_root/usr/local/lib/daisyos/daisyos-memory-health"
install -Dm644 "$repo_root/scripts/memory/daisyos-memory-health.service" "$target_root/etc/systemd/system/daisyos-memory-health.service"

create_disk_swap() {
  local swap_dir=/var/lib/daisyos
  local swap_file="$swap_dir/swapfile"
  local staged_swap="$swap_dir/swapfile.new"
  local filesystem available_kib required_kib
  filesystem="$(findmnt -no FSTYPE --target "$swap_dir" 2>/dev/null || findmnt -no FSTYPE --target /)"
  available_kib="$(df --output=avail -k /var/lib 2>/dev/null | awk 'NR == 2 { print $1 }')"
  required_kib=$((disk_swap_gib * 1024 * 1024))
  [[ "$available_kib" =~ ^[0-9]+$ ]] || { echo "Could not measure free space for disk swap." >&2; return 1; }
  (( available_kib >= required_kib + 1024 * 1024 )) || {
    echo "Refusing disk swap: keep at least 1 GiB free after allocation." >&2
    return 1
  }
  install -d -m700 "$swap_dir"
  if [[ -e "$swap_file" ]]; then
    [[ "$(blkid -p -s TYPE -o value "$swap_file" 2>/dev/null || true)" == swap ]] || {
      echo "Refusing to overwrite non-swap file: $swap_file" >&2
      return 1
    }
  elif [[ "$filesystem" == btrfs ]]; then
    command -v btrfs >/dev/null || { echo "Btrfs tooling is required for a safe swap file." >&2; return 1; }
    [[ ! -e "$staged_swap" ]] || { echo "Remove stale staging file before retrying: $staged_swap" >&2; return 1; }
    btrfs filesystem mkswapfile --size "${disk_swap_gib}G" "$staged_swap"
    mv "$staged_swap" "$swap_file"
  elif [[ "$filesystem" == ext4 || "$filesystem" == xfs ]]; then
    [[ ! -e "$staged_swap" ]] || { echo "Remove stale staging file before retrying: $staged_swap" >&2; return 1; }
    fallocate -l "${disk_swap_gib}G" "$staged_swap"
    chmod 600 "$staged_swap"
    mkswap "$staged_swap" >/dev/null
    mv "$staged_swap" "$swap_file"
  else
    echo "Unsupported swap-file filesystem: $filesystem" >&2
    return 1
  fi
  chmod 600 "$swap_file"
  install -Dm644 "$repo_root/scripts/memory/var-lib-daisyos-swapfile.swap" \
    /etc/systemd/system/var-lib-daisyos-swapfile.swap
}

if [[ "$target_root" == / ]] && (( disk_swap_gib > 0 )); then
  create_disk_swap
fi

if [[ "$target_root" == / ]] && (( activate )); then
  systemctl daemon-reload
  systemctl enable --now systemd-oomd.service
  systemctl enable var-lib-daisyos-swapfile.swap >/dev/null 2>&1 || true
  if (( disk_swap_gib > 0 )); then
    systemctl start var-lib-daisyos-swapfile.swap
  fi
  systemctl restart systemd-zram-setup@zram0.service
  sysctl --system >/dev/null
  systemctl enable --now daisyos-memory-health.service
  /usr/local/lib/daisyos/daisyos-memory-health
fi

echo "Installed DaisyOS memory resilience under $target_root."
