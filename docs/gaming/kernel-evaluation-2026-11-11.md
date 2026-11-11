# Gaming kernel evaluation

## Repository milestone

The stock Arch `linux` kernel remains the DaisyOS default and recovery choice.
An opt-in overlay at `os/packages/gaming-kernel-zen.txt` adds the official
Arch `linux-zen` package and matching headers without replacing the stock
kernel. This is an upstream Zen kernel candidate, not a DaisyOS-tuned kernel
and not a claim that it improves game performance.

The live ISO continues to use `linux`; it is not changed by this experiment.
There is no supported installed-system kernel updater or boot-entry manager in
this repository, so candidate installation is manual and its boot entry must be
verified on the target installation before rebooting. Keep the existing stock
kernel and a known-good boot entry until the candidate has passed acceptance.

## CachyOS source review

The current CachyOS kernel project describes a fork that combines upstream
stable with a changing collection of topic branches, scheduler options, config
changes and build options. Its current overview includes BORE and EEVDF
variants, PREEMPT_DYNAMIC, selectable timer frequencies, compiler/LTO/PGO
choices and other subsystem patches. The PKGBUILD applies scheduler patches
conditionally and changes kernel config/build settings. Those differences are
bundled, so adopting a CachyOS package would not isolate a single scheduler or
configuration effect. No CachyOS patch or CPU-specific build is enabled in
DaisyOS pending a pinned source revision, per-difference review, and repeatable
hardware evidence. Review references: [CachyOS kernel project](https://github.com/CachyOS/linux-cachyos),
[current CachyOS kernel PKGBUILD](https://github.com/CachyOS/linux-cachyos/blob/master/linux-cachyos/PKGBUILD),
and [CachyOS kernel configuration guide](https://wiki.cachyos.org/features/kernel_manager/).

## Candidate install and recovery

On a supported installed Arch-based DaisyOS system, install the overlay's
packages while retaining `linux`:

```bash
sudo pacman -Syu linux-zen linux-zen-headers
```

Confirm the generated initramfs and the bootloader entry for both kernels. For
the NVIDIA gaming profile, the stock prebuilt `nvidia-open` package targets the
stock kernel. Before testing Zen, replace it with the DKMS variant and build
against the Zen headers as part of one transaction:

```bash
sudo pacman -Syu linux-zen linux-zen-headers nvidia-open-dkms
```

Review proposed package removals and ensure `linux`, `nvidia-utils`, and
`lib32-nvidia-utils` remain installed. Do not select the Zen entry until
`dkms status` shows the NVIDIA module built for its exact release and the
initramfs exists. If boot, display, suspend, or graphics fails, select the
stock `linux` entry from the bootloader and remove `linux-zen` only after
returning to the known-good system. Secure Boot signing is installation
specific and must be checked before either kernel is selected.

## Same-machine capture matrix

Use one host, the same display mode, firmware settings, graphics stack, power
profile, game/scene, and DaisyOS Release build. Keep AC power and ambient
conditions fixed. Do at least three clean boots per profile in alternating
order (stock, Zen, Zen, stock, stock, Zen) to reduce drift. Each invocation
records kernel package/release/config, command line, NVIDIA module/DKMS status,
a five-minute desktop resource timeline, and seven-sample shell responsiveness
measurements:

```bash
python3 scripts/capture-gaming-kernel-run.py stock --output artifacts/kernel-matrix/stock-01.json
python3 scripts/capture-gaming-kernel-run.py zen --output artifacts/kernel-matrix/zen-01.json
```

Repeat with unique output names until there are three captures per kernel.
Capture the same real-game workload separately for FPS, 1%/0.1% lows, frame
time variance, input latency, audio glitches, power and temperature; this script
does not invent game performance measurements. Compare shell responsiveness
reports with `scripts/benchmark-os-performance.py compare`; summarize session
and game metrics by profile with medians and spread. Reject incomplete runs,
kernel/module mismatches, thermal throttling or materially different
environment metadata.

## Current evidence and gate

The repository now has an opt-in candidate and a capture protocol. No stock vs
Zen physical-machine captures, boot/recovery trial, or NVIDIA DKMS acceptance
are available yet. Therefore no kernel difference is approved as DaisyOS's
default, and the 700 MiB/latency target is not claimed as met by this phase.
Arch package references: [linux-zen](https://archlinux.org/packages/extra/x86_64/linux-zen/),
[linux-zen-headers](https://archlinux.org/packages/extra/x86_64/linux-zen-headers/),
and [nvidia-open-dkms](https://archlinux.org/packages/extra/x86_64/nvidia-open-dkms/).
