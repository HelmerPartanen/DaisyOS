# DaisyOS Installer Architecture

## Current safety boundary

`DaisyOS.Installer` is a complete interaction prototype backed exclusively by
`MockInstallerBackend`. It cannot enumerate disks and contains no calls to
partitioning, formatting, mounting, package installation, user-management, or
bootloader tools. Its two displayed disks are immutable example records.

The view model additionally refuses to start any backend that reports disk-write
capability. This deliberate double lock means replacing the mock backend cannot
silently turn the prototype into a destructive installer.

The prototype covers:

- VM-only acknowledgement;
- language, keyboard, and timezone selection;
- account and device-name validation;
- example disk selection;
- encryption and recovery choices clearly labelled as planned;
- a full review page;
- exact typed confirmation;
- cancellable mock progress and a calm failure state;
- completion that explicitly states nothing was changed.

Run it with:

```bash
dotnet run --project src/DaisyOS.Installer
```

## Proposed partition layout

No partitioning code should be added until this plan receives a separate review
and passes disposable-VM tests.

For a blank UEFI disk, the proposed automatic layout is:

| Partition | Suggested size | Format | Purpose |
|---|---:|---|---|
| EFI System Partition | 1 GiB | FAT32 | Signed boot files and firmware entry |
| DaisyOS system | Remaining space minus recovery | Btrfs | Root system and snapshots |
| Recovery | 4 GiB | Read-only image/filesystem | Offline repair and reinstall tools |

Swap should initially use a Btrfs-compatible swap file sized from memory and
hibernation requirements. Existing partitions must never be resized without a
separate manual-partitioning flow and an explicit backup warning.

Before writing anything, a real installer must show the stable device path,
model, serial suffix, capacity, connection type, current partitions, mount
state, and whether the disk contains another operating system. The user must
select the device themselves; the installer must never guess.

Encryption should use LUKS2 around the system partition, with recovery-key
handling designed before implementation. The installer must explain plainly
that losing both the password and recovery key means losing access to the data.

## Bootloader plan

The first UEFI-only implementation should use systemd-boot because DaisyOS
already uses systemd and the design is straightforward to recover. The real
backend must:

1. verify that the live environment booted in UEFI mode;
2. mount the selected EFI System Partition at the target boot path;
3. install boot files only inside the mounted target;
4. generate entries using filesystem UUIDs, never transient `/dev/sdX` names;
5. create a normal DaisyOS entry and a recovery entry;
6. preserve unrelated firmware entries;
7. verify the generated configuration before changing boot order;
8. restore the previous firmware order if final verification fails.

Legacy BIOS, dual boot, Secure Boot enrollment, TPM-assisted unlock, custom
partitioning, and recovery-media creation require separate designs and test
matrices. They must not be improvised inside the automatic installation path.

## Gate for real installation work

Real disk operations remain blocked until all of the following exist:

- reviewed command/API boundary with an unprivileged UI and minimal privileged
  helper;
- structured disk discovery with stable identifiers;
- dry-run installation plans that can be exported for review;
- explicit tests for mounted disks, removable drives, existing operating
  systems, interrupted installs, low space, power loss, and cancellation;
- QEMU snapshots proving failure never writes to an unselected device;
- recovery boot and rollback verification;
- translated, accessible warnings and keyboard-only confirmation;
- logs that omit passwords, encryption keys, and other secrets.
