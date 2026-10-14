# Lock screen polish and OS update audit — 2026-10-07

The clock uses 50% opacity (previously 40%). The main lock-screen content adapts to screen height and available width, with a measured clock width, wrapped date and user name, consistent password controls, and separate footer space. Short screens scroll the credential content without covering power controls. Scrolling clips clock blur regions to the viewport. Appearance changes recompute sizing. PAM authentication and the locked-window closing guard remain intact.

Five focused rendered checks pass: the existing animated clock glyph blur test and four layout cases at 1280×720, 1920×1080, 640×480 with 150% Noto Sans, and 360×640 with 125% DejaVu Serif. Cases include Finnish, a long user name, password errors and Caps Lock. Screenshots were inspected under `/tmp/daisy-lock-polish`. These checks do not constitute physical-monitor or live PAM acceptance.

The host is EndeavourOS/Arch. Installed and running kernel is 7.2.9-arch1-1, already the latest stable kernel listed at https://www.kernel.org/ and in the official Arch repositories on the audit date. A release candidate is not installed. ISO package lists already track unpinned linux and linux-firmware packages.

A preliminary check found 126 updates; the full repository refresh resolved 162 packages. Administrator authentication succeeded for `pkexec /usr/bin/pacman -Syu --noconfirm`. The full command exited successfully (0), including all 21 post-transaction hooks and boot-image rebuilding. It upgraded 153 packages and installed 9 dependencies/replacements. No errors or new .pacnew warnings appeared in the transaction log. The current repository upgrade list is empty. Updates include Qt 6.12, KWin package rebuild 6.7.5-3, GTK/GNOME 51, media libraries, SOF firmware and Xorg. No reboot was performed.

`flatpak update --noninteractive -y` completed successfully. Freedesktop runtimes, graphics/video codecs, NVIDIA runtime extensions, Breeze theme and Spotify were updated. A subsequent `flatpak remote-ls --updates` returned no entries. Foreign/AUR application packages and BIOS firmware were not changed.

The repository retains a corrupt Git HEAD object and the previously recorded complete-solution designer tool prerequisite; task-start backups are at `/tmp/daisy-lock-polish-before`. Unrelated working-tree changes are preserved.

Native standalone builds and shell restart completed; KWin and open applications remained running. Strict compositor preflight returned zero failures and zero warnings. The custom reflection compositor remains at its separate user-owned installation; its executable starts with `--version` after the Qt update and its shared dependencies resolve. A new physical compositor session has not been started.

Final checks: `pacman -Qu` and Flatpak remote updates are empty; both system and user failed-service lists are empty. Native compositor preflight passes after the update. A reboot is recommended at a convenient time to start processes with the updated libraries; none was forced.

## Visual balance follow-up

User feedback from the physical desktop superseded the initial headless visual assessment. The default password pill is now 272px instead of 360px, growing with larger control/font metrics up to the available credential width. The avatar is 48px with a circular radius on all three material layers. Credential spacing is 8px and bottom clearance scales with screen height (48–160px), lifting the sign-in group away from the footer. Redundant password instructions stay hidden until the hint button is clicked; errors and Caps Lock remain available. Clock opacity is retained at 50%.

Follow-up validation: all five focused layout/glyph-blur checks pass. Default layouts and error states were rendered at the four existing font/screen configurations and inspected. Material corner radii use the actual half-height, avoiding oversized-radius distortion in the shared frame painter. The desktop was confirmed unlocked before requesting deployment.

Follow-up native builds and shell reload completed successfully; strict compositor preflight has zero failures/warnings. The production lock screen was not opened automatically.
