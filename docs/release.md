# Releases

DaisyOS currently publishes a self-contained Linux x64 shell artifact. ISO images,
Arch packages, repository signing, and automatic host updates are separate future
milestones and are not performed by this release pipeline.

## Version and channels

The project version is stored in `Directory.Build.props`. DaisyOS starts at
`0.1.0` and uses three channels:

- `nightly`: development builds from `master` or `main`; these are CI artifacts, not GitHub Releases.
- `beta`: manually dispatched builds or prerelease version tags such as `v0.1.0-beta.1`.
- `stable`: manually dispatched builds or version tags without a prerelease suffix, such as `v0.1.0`.

Assembly and file versions remain numeric. The informational version shown in
Settings → About includes the channel, UTC build date, and short Git revision.

## Local builds

```bash
scripts/build-debug.sh
scripts/build-release.sh
scripts/build-release.sh --channel beta --version 0.1.0 --output artifacts/candidate
```

Environment defaults are supported for automation:

```text
DAISYOS_VERSION
DAISYOS_RELEASE_CHANNEL
DAISYOS_RELEASE_OUTPUT
DAISYOS_BUILD_DATE
DAISYOS_SOURCE_REVISION
```

Explicit command-line arguments take precedence over environment values. Release
builds run formatting, restore, Release compilation, tests, and a self-contained
`linux-x64` publish before creating:

```text
DaisyOS-Shell-<version>-<channel>-linux-x64.tar.gz
DaisyOS-Shell-<version>-<channel>-linux-x64.tar.gz.sha256
```

The unpacked publish tree remains at `publish/linux-x64` for
`scripts/copy-shell-to-iso.sh` compatibility. Before packaging, the release script
removes older DaisyOS shell archives from the selected artifact directory so stale
files cannot be uploaded with a new release.

Verify an artifact from its containing directory:

```bash
sha256sum --check DaisyOS-Shell-0.1.0-nightly-linux-x64.tar.gz.sha256
```

## Release checklist

- Move user-visible entries from the changelog's Unreleased section into the release version.
- Confirm `scripts/build-debug.sh` and `scripts/build-release.sh` succeed locally.
- Confirm the archive contains an executable `DaisyOS.Shell` and the checksum verifies.
- Confirm Settings → About shows the expected informational version.
- For beta, create a prerelease tag such as `v0.1.0-beta.1`; for stable, create `v0.1.0`.
- Confirm the GitHub workflow publishes both the archive and checksum.
- Smoke-test the downloaded artifact with mock services before announcing it.

## Failed releases

Do not reuse or silently move a published tag. Fix the issue on a new commit and
publish a new prerelease identifier or patch version. A failed manual workflow can
be rerun with the same inputs if it never published a GitHub Release. If a bad
release was published, mark it clearly, remove it from recommendations, and issue
a corrected version; retain logs and checksums for diagnosis.

Signing, a package repository, ISO publication, automatic updates, and rollback
are intentionally not claimed by this pipeline.
