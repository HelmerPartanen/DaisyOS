#!/bin/bash
set -euo pipefail

# Complete deterministic live-media defaults before systemd-firstboot runs.
# Welcome can still collect the user's preferred timezone and keyboard later.
ln -sfn /usr/share/zoneinfo/UTC /etc/localtime
locale-gen >/dev/null

# Live-media account only. Installed systems create their user through the
# reviewed installer flow instead of inheriting this account.
getent group gamemode >/dev/null || groupadd --system gamemode
if ! id -u daisyos &>/dev/null; then
  echo "Creating DaisyOS live user..."
  useradd -m -g users -G wheel,audio,video,network,storage,tty,gamemode -s /bin/bash daisyos
  passwd -d daisyos
fi

# Passwordless sudo is limited to the disposable live environment.
echo "daisyos ALL=(ALL:ALL) NOPASSWD: ALL" > /etc/sudoers.d/daisyos-live
chmod 440 /etc/sudoers.d/daisyos-live

mkdir -p /home/daisyos/.config/DaisyOS
chown -R daisyos:users /home/daisyos
printf 'daisyos-live\n' > /etc/hostname

systemctl enable --now bluetooth.service 2>/dev/null || true

echo "DaisyOS live environment is ready."
