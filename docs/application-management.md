# Application installation and removal

Settings, launcher, App Store and App Installer share `ApplicationManagementService`.
Removal means removing the installation through its actual backend. “Hide from
launcher” is a separate action. Previously hidden apps can be restored or reviewed
in Settings → Installed apps.

| Format or installation | Backend |
| --- | --- |
| Arch `.pkg.tar`, `.pkg.tar.zst`, `.pkg.tar.xz`, `.pkg.tar.gz` | libalpm, with administrator authentication |
| Flatpak `.flatpakref`, `.flatpak`, App Store apps | Flatpak; exact installation scope and reference |
| AppImage | Existing managed AppImage integration |
| DEB / RPM | One rootless Podman/Distrobox environment per package and base image |
| Manually installed app | Explicit review of its dedicated installation directory |

DEB defaults to Ubuntu and RPM to Fedora. App Installer offers alternate supported
base images. This handles ordinary desktop applications and dependencies; it does
not make distribution-specific kernel modules, drivers, boot services or arbitrary
vendor installers compatible. Distrobox integrates with the user's desktop and home;
it is a compatibility environment, not a security sandbox.

Installation hashes local files during review and checks them again before use.
The native helper stages privileged inputs and checks the complete package-change
fingerprint while holding Pacman's transaction lock. Dependency checks stay enabled.
The root-owned essential-component manifest protects the OS, session, graphics stack,
package manager and recovery/management tools and their dependencies. System Monitor
remains part of the essential shell diagnostics.

Saved data is retained by default. An unchecked deletion option appears for recorded
container homes and user Flatpak data when no other installation shares it. Personal
documents and the original downloaded installer are retained. Unmanaged folders in
the user's home move to Trash. Administrator-owned unmanaged folders are restricted
to dedicated, unowned directories under `/opt`, `/usr/local/lib`, or `/usr/local/share`;
these are permanently removed after review and authentication. Package-owned or
writable/shared system folders are refused.

Successful removal cleans up matching desktop shortcuts, including renamed copies,
and informs the shell to refresh launchers and pins. Failed removal keeps them.
An alternate Flatpak installation keeps its generic launchers and pins. Ownership
records survive failed shortcut cleanup so cleanup can be retried.

## Optional DaisyOS apps

Files, Mail, Notes, Calculator, App Store, Disk Manager and UI Kit have separate
package ownership. Notes, Mail, Calculator, App Store and UI Kit compile their UI
into separate application assemblies. Shared file/disk services remain in the shell
for system features. Settings and App Installer are protected management tools.

The Arch split package recipe installs optional apps independently. ISO staging
creates matching archives from the actual payload, and a first-boot service registers
those exact files in Pacman's database before the display manager starts. It never
registers fake ownership on the build host.

Development installs record only each optional app's own build output and integration
files. Uninstalling those leaves source code and saved data intact. Integration and
rebuild scripts respect `removed-optional-applications` so removed apps do not reappear.
Installing a real app package through DaisyOS clears that removal state.

## Validation

Run without root; package-helper tests use an isolated database and never commit:

```sh
cmake -S src/native/daisy_package_helper -B artifacts/package-helper -DCMAKE_BUILD_TYPE=Release
cmake --build artifacts/package-helper
python3 scripts/test-application-package-helper.py artifacts/package-helper/daisy-package-helper
python3 scripts/test-managed-application-packages.py
dotnet test tests/DaisyOS.Applications.Tests -c Release --disable-build-servers -m:1
dotnet test tests/DaisyOS.AppStore.Tests -c Release --disable-build-servers -m:1
dotnet test tests/DaisyOS.Tests -c Release --disable-build-servers -m:1 --filter 'FullyQualifiedName~AppInstallerViewModelTests|FullyQualifiedName~LinuxAppStoreServiceTests|FullyQualifiedName~AppImage'
dotnet build DaisyOS.sln -c Release --disable-build-servers -m:1
```

Before deploying privileged operations, stage the helper and review the artifacts:

```sh
scripts/install-application-management.sh --root artifacts/application-management-root
```

Installation into `/` requires administrator authentication. The installed helper,
essential manifest and PolicyKit action must be root-owned. Real Flatpak, container,
and full ISO boot/install tests must run on a disposable system with the corresponding
backends available; mocked transaction tests do not establish binary compatibility.

Metadata implementation references:
[libflatpak API](https://docs.flatpak.org/en/latest/libflatpak-api-reference.html),
[Distrobox export](https://distrobox.it/usage/distrobox-export/).

The fixture checks also include
`python3 scripts/test-development-application-ownership.py` to verify that only
compiled optional-app output is registered and that removed-app state survives rebuilds.

## Verification on this checkout

- Release solution build: passed.
- 180 targeted automated checks: 24 transaction/ownership tests, 46 App Store/UI
  tests, 57 installer/AppImage/backend regressions and 53 optional-app UI checks.
- Native helper fixture: dependency cascades, essential dependency protection,
  unmanaged-folder ownership and launcher review, content-change detection,
  authentication gate and read-only planning passed.
- ISO archive and development ownership fixtures passed. Full ISO boot was not run.
- Installed helper, essential manifest and PolicyKit action were verified root-owned.
  Read-only checks against the laptop found 84 native application packages, blocked
  removal of Bash, allowed a Firefox removal review, and inspected an actual Arch
  archive with its content fingerprint. No installed applications were removed.
- The running development shell was refreshed in Release configuration. All seven
  optional app payloads have development ownership records.
- Flatpak and compatibility prerequisite plans resolve on the host. Those backends
  remain installed on demand; real Flatpak/container installation and removal need
  testing on a disposable system. App-specific compatibility is not guaranteed.

Detailed command outputs are in `artifacts/application-management-validation/`.
