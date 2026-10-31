#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# User install only. Does not enable sharing, change firewall rules, or start services.
prefix="${1:-$HOME/.local}"
payload="${2:-$repo_root/artifacts/continuity/publish}"
if [[ ! -x "$payload/DaisyOS.Continuity" ]]; then
  printf 'Build the payload first: scripts/build-continuity.sh\n' >&2
  exit 1
fi
if [[ ! -x "$payload/daisyos-continuity-media" || ! -x "$payload/daisyos-portal-capture" ]]; then
  echo "A native helper is missing. Run scripts/build-continuity.sh before installing." >&2
  exit 1
fi
was_active=false
if [[ "$(realpath -m "$prefix")" == "$(realpath -m "$HOME/.local")" ]] && systemctl --user is-active --quiet daisyos-continuity.service; then
  was_active=true
  systemctl --user stop daisyos-continuity.service
fi
# Never overwrite mapped .NET assemblies. Publish a complete immutable release,
# then switch the directory link while the managed service is stopped.
python3 - "$prefix" "$payload" <<'PYINSTALL'
import os, pathlib, shutil, sys, uuid
prefix, payload = pathlib.Path(sys.argv[1]).resolve(), pathlib.Path(sys.argv[2]).resolve()
base = prefix/'lib/daisyos'
releases = base/'continuity-releases'
releases.mkdir(parents=True, exist_ok=True)
release = releases/uuid.uuid4().hex
shutil.copytree(payload, release)
current = base/'continuity'
if current.exists() and not current.is_symlink(): current.rename(releases/('legacy-'+uuid.uuid4().hex))
link = base/('continuity-next-'+uuid.uuid4().hex)
link.symlink_to(release, target_is_directory=True)
os.replace(link, current)
(prefix/'bin').mkdir(parents=True, exist_ok=True)
(prefix/'share/systemd/user').mkdir(parents=True, exist_ok=True)
PYINSTALL
python3 - "$prefix" "$repo_root/packaging/continuity/daisyos-continuity.service" <<'PY'
import pathlib, shlex, sys
prefix = pathlib.Path(sys.argv[1]).resolve()
# systemd quoted values escape backslash, quote and specifier percent.
def quote(value):
    return '"' + str(value).replace('\\', '\\\\').replace('"', '\\"').replace('%', '%%') + '"'
unit = pathlib.Path(sys.argv[2]).read_text().replace('/usr/lib/daisyos/apps/Continuity/DaisyOS.Continuity',
    quote(prefix / 'lib/daisyos/continuity/DaisyOS.Continuity'))
(prefix / 'share/systemd/user/daisyos-continuity.service').write_text(unit)
launcher = prefix / 'bin/daisyos-continuity'
launcher.write_text('#!/bin/sh\nexec ' + shlex.quote(str(prefix / 'lib/daisyos/continuity/DaisyOS.Continuity')) + ' "$@"\n')
launcher.chmod(0o755)
PY
if [[ "$was_active" == true ]]; then
  systemctl --user daemon-reload
  # A running shell can start the old release again while files are staged.
  # Restart after the atomic switch to ensure the new payload is the one running.
  systemctl --user restart daisyos-continuity.service
fi
printf 'Installed under %s. Install dependencies: .NET 10 ASP.NET runtime, avahi, wl-clipboard; media also needs libei, GStreamer/PipeWire plugins and libpulse (pactl/pacat).\n' "$prefix"
printf 'Reload user units with systemctl --user daemon-reload, then start daisyos-continuity.service. Sharing stays off until enabled in Settings.\n'
