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
install -d "$prefix/lib/daisyos/continuity" "$prefix/bin" "$prefix/share/systemd/user"
cp -af "$payload/." "$prefix/lib/daisyos/continuity/"
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
printf 'Installed under %s. Install dependencies: .NET 10 ASP.NET runtime, avahi, wl-clipboard.\n' "$prefix"
printf 'Reload user units with systemctl --user daemon-reload, then start daisyos-continuity.service. Sharing stays off until enabled in Settings.\n'
