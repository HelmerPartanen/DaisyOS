# Update System

The update system is not implemented yet.

Early versions may wrap `pacman` with careful command execution. Long-term work should consider signed packages, snapshots, rollback, and image-based updates.

Automatic destructive updates must not be implemented until reviewed.


## Manual updates on an Arch-based host

Refresh and upgrade the entire official repository system together with `sudo pacman -Syu`. This includes available kernel, firmware, graphics, compositor and system-service updates. Avoid refreshing repositories and then selectively installing packages without the full upgrade. Inspect package-manager errors and any `.pacnew` configuration files before treating the update as complete.

Update installed Flatpak apps and runtimes with `flatpak update`. AUR and other foreign packages need a separate source and compatibility review. Kernel release candidates and motherboard firmware flashing are not part of routine desktop package updates.

After a successful kernel upgrade, compare `uname -r` with `pacman -Q linux` and reboot when convenient to load the installed kernel. Do not reboot automatically during a desktop session. The ISO package list uses unpinned `linux` and `linux-firmware` entries, so a fresh image resolves current repository versions.
