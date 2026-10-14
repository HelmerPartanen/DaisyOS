# ISO Build

ISO generation uses Arch `archiso`.

Current status:

- The profile targets the current Arch `bios.syslinux` and
  `uefi.systemd-boot` modes and has repository validation coverage.
- The next profile starts KWin Wayland with XWayland so Settings, Files, and
  third-party applications receive compositor-owned focus, stacking, movement,
  resizing, effects, and decorations. A software-composited nested KWin/Xorg
  path and the original supervised Xorg shell remain guarded fallbacks.
- Wallpaper rendering is active in the desktop surface again, and the
  interactive QEMU runner defaults to UEFI plus standard VGA and
  aspect-preserving scaling.
- The first 1.8 GiB compositor rebuild was rejected: its old full-screen GPU
  vibrancy surface left most of an automated framebuffer black. The corrected
  payload removes that surface, renders through normal Avalonia composition,
  and has passed injected-payload UEFI/KWin desktop, Settings, Files, native
  decoration, transparency, and stability checks. It still requires a fresh
  production ISO and the complete gate.
- `mkarchiso` and `qemu-system-x86_64` are available on the development host.
- On 2026-07-16, the accepted 1.5 GiB Xorg-fallback alpha passed the complete
  source-bound BIOS, UEFI, service, rendering, stability, recovery, Restart,
  and Shut down gate under QEMU software emulation. The compositor-enabled
  successor described above is implemented and repository-validated but still
  requires its own rebuilt-image acceptance run.

Build and boot flow:

```bash
scripts/publish-shell.sh
scripts/copy-shell-to-iso.sh
sudo scripts/build-iso.sh
scripts/run-qemu.sh
```

UEFI is the interactive runner's default. Pass `--firmware bios` to exercise
the Syslinux fallback. Both paths intentionally use standard VGA: OVMF plus
`virtio-vga` under TCG previously corrupted the software-rendering path, and
dynamic `virtio-vga` resizing could give the fullscreen BIOS guest an incorrect
scale. GTK zoom-to-fit preserves the standard-VGA guest aspect ratio.

`mkarchiso` needs temporary administrator access to create and mount the
isolated image filesystem. The build wrapper refuses to run when the published
self-contained shell has not been staged, and it returns generated work and ISO
files to the invoking user when run through `sudo`.

The development profile uses Zstandard for the SquashFS payload so repeated VM
validation remains practical. The boot initramfs retains the Arch live hooks
and XZ compression; changing root-filesystem compression does not change its
mount behavior.

Production publishing emits a composite ReadyToRun image. This precompiles the
shell before it enters the VM, avoiding long first-launch JIT work on emulated
CPUs and making startup behavior representative of the release build.

`scripts/run-qemu.sh` uses KVM when the current user can access `/dev/kvm` and
falls back to non-destructive software emulation otherwise. Software emulation
is substantially slower, especially while generating the live locale on first
boot.

The generated ISO is intentionally a live preview, not an installer. It does
not partition, format, or write to a virtual disk. Any compositor-profile
change must pass a newly rebuilt BIOS/UEFI image before it becomes a new
accepted alpha artifact.

For repeatable headless checks of both firmware paths, run:

```bash
scripts/smoke-test-iso.sh --firmware bios
scripts/smoke-test-iso.sh --firmware uefi
scripts/test-iso-power.sh --action all
```

For a candidate that may be marked as a bootable live-ISO alpha, run the single
acceptance gate instead:

```bash
scripts/validate-live-iso-alpha.sh out/DaisyOS-YYYY.MM.DD-x86_64.iso
```

The gate rejects stale images and development-mode publishes. It fingerprints
the current shell source, verifies the production shell hash inside the ISO,
runs the complete repository suite, boots the image through BIOS and UEFI,
checks the live user and runtime services, and activates Restart and Shut down
through the real shell. Passing evidence is written to
`artifacts/live-iso-alpha/report.md`.

The alpha label applies only to the non-installing live environment. It does
not certify the mock-only installer, public package publication, secure update
installation, rollback, or broad physical-hardware compatibility.

The firmware smoke tests wait for `graphical.target`, reject kernel panics and
supervised-shell crashes, require a colorful frame with substantial full-frame
non-black coverage, and repeat that visual assertion after recovery and the
stability window. This prevents a narrow wallpaper fragment from being accepted
as a usable desktop. Evidence is
stored under `artifacts/iso-smoke/` and `artifacts/iso-smoke-uefi/`.

The power test opens the real desktop power menu in separate diskless VMs,
activates Restart and Shut down through keyboard focus, and requires each guest
to exit. Its screenshots and result markers are stored under
`artifacts/iso-power-reboot/` and `artifacts/iso-power-shutdown/`.

All boot and installer testing must happen in a VM before any real machine testing.
