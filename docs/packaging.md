# Arch Packaging

DaisyOS now has an installable Arch package definition in `packaging/arch`.
The package is intentionally separate from the live ISO copy workflow so both
paths remain testable while the desktop session is still under development.

## Package contents

`daisyos-shell` installs:

- the self-contained shell under `/usr/lib/daisyos`;
- `/usr/bin/daisyos-shell`, the stable command used by sessions and users;
- a desktop entry under `/usr/share/applications`;
- a systemd user service under `/usr/lib/systemd/user`;
- administrator defaults at `/etc/daisyos/settings.json`;
- vendor defaults at `/usr/share/daisyos/defaults/settings.json`;
- the scalable DaisyOS icon.

Settings precedence is:

1. `$XDG_CONFIG_HOME/DaisyOS/settings.json` or `~/.config/DaisyOS/settings.json`;
2. `/etc/daisyos/settings.json` for administrator-selected defaults;
3. `/usr/share/daisyos/defaults/settings.json` for package defaults;
4. defaults compiled into the shell.

Once the user changes a setting, DaisyOS writes their complete configuration to
the user path. Package upgrades therefore do not overwrite personal settings.
Pacman preserves administrator changes to `/etc/daisyos/settings.json` through
its backup-file handling.

## Building locally

The `PKGBUILD` consumes a tagged source archive. Before building a release,
update `pkgver`, `pkgrel`, and every checksum:

```bash
cd packaging/arch
updpkgsums
makepkg --cleanbuild --syncdeps
```

`SKIP` checksums are acceptable only while developing the packaging definition.
They are a release blocker. A stable package must pin the source and auxiliary
files to reviewed SHA-256 values.

The repository does not currently declare a redistribution license. The
`PKGBUILD` therefore uses Arch's `custom` marker and does not install invented
license terms. Selecting a license, adding its text at the repository root, and
updating the package metadata are mandatory before distributing a package.

Repository-only validation does not install anything:

```bash
scripts/check-packaging.sh
```

The check enforces package/project version alignment, the owned runtime and
configuration paths, Pacman backup handling for administrator settings, safe
user-service restart limits, and the rule that installation never enables the
shell or changes accounts. It also requires signatures for both packages and
the repository database.

Generate a shareable readiness report without publishing anything:

```bash
scripts/check-packaging.sh --report artifacts/packaging/arch-readiness.md
```

The stricter `--release` mode fails while any known publication blocker remains.
This is intentional: repository validation can prove policy and layout, but it
cannot invent a project license, approve release checksums, or choose the real
repository host.

For a disposable package build, use a clean Arch VM or container. Do not run
`makepkg` as root.

## Starting the shell

The package does not enable itself. Replacing a working desktop automatically
could strand the user at login. During development, start it explicitly:

```bash
systemctl --user enable --now daisyos-shell.service
```

The service belongs to `graphical-session.target`, restarts only on failure, and
limits rapid restart loops. The future login-manager session package will own
session selection and enablement. Package installation must not create a user or
change auto-login policy; those are image/installer responsibilities.

To disable the prototype service:

```bash
systemctl --user disable --now daisyos-shell.service
```

## Package profiles

The package manifests under `os/packages` are ordered by capability:

- `minimal.txt`: shell, graphics, networking, audio, and fonts;
- `desktop.txt`: minimal plus KWin, XWayland, portals, power, and policy UI;
- `desktop-gaming.txt`: desktop plus multilib graphics and gaming tools.

Every larger profile must contain the complete smaller profile. The packaging
check enforces this and rejects duplicate entries. The gaming profile requires
Arch's `multilib` repository; it must not be used with the current minimal ISO
until that repository is enabled and validated.

## Repository and signing plan

`packaging/arch/daisyos-repo.conf` is deliberately pointed at an invalid example
host. It documents the final Pacman configuration without encouraging anyone to
trust an unfinished repository.

The publication sequence is:

1. Build packages in a clean, reproducible Arch environment.
2. Sign each package with a dedicated offline-controlled DaisyOS packaging key.
3. Generate the repository database with `repo-add --sign`.
4. Publish packages, signatures, and the signed database over HTTPS.
5. Ship the public key in a separately reviewed keyring package.
6. Require package and database signatures in Pacman; never use `TrustAll`,
   `DatabaseOptional`, or disabled checks.
7. Rotate or revoke compromised keys through a signed keyring update and publish
   a short, plain-language security notice.

CI release jobs should produce unsigned candidates. Signing should occur in a
protected release environment with short-lived access to signing hardware or an
isolated signing service. Developer and nightly keys must not be trusted by
stable installations.
