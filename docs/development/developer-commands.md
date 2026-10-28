# NESTED KWIN
scripts/publish-shell.sh --fast
scripts/run-kwin-prototype.sh


# PRODUCTION / ISO PUBLISH
scripts/publish-shell.sh

# SHELL
DAISYOS_SHELL_SERVICES=real dotnet run --project src/DaisyOS.Shell

# NATIVE DEVELOPMENT SESSION
# Rebuilds DaisyOS, then replaces only the layer-shell process. KWin and open apps stay running.
daisyos-native-development restart
# Inspect the current RAM reserve and protected app-memory boundary.
daisyos-native-development memory status

# RELIABILITY / 8 GB ACCEPTANCE
sudo scripts/install-memory-resilience.sh --disk-swap-gib 4
daisyos-native-development memory status
sudo -E scripts/daisyos-reliability.py capture --output artifacts/reliability --label before
# Default 8-hour rotation; use --duration 86400 for release acceptance.
sudo -E scripts/daisyos-reliability.py run --manifest tests/reliability/everyday-v1.json \
  --duration 28800 --interval 30 --checkpoint-results tests/reliability/checkpoints.example.json \
  --output artifacts/reliability --label integrated-before
scripts/daisyos-reliability.py summarize artifacts/reliability/RUN
scripts/daisyos-reliability.py compare artifacts/reliability/BEFORE/summary.json \
  artifacts/reliability/AFTER/summary.json --output artifacts/reliability/comparison.json

# KWIN WINDOW DECORATION
# One-time installation; the plugin is only activated by Native Development.
sudo scripts/install-kwin-window-decoration.sh
# Re-read the exported DaisyOS light/dark decoration palette without restarting applications.
daisyos-native-development decoration reload
# Inspect, temporarily opt out, or re-enable the Native Development titlebar.
daisyos-native-development decoration status
daisyos-native-development decoration disable
daisyos-native-development decoration enable

# Benchmarks
python3 scripts/benchmark-os-performance.py run --build --output artifacts/performance/after.json

python3 scripts/benchmark-os-performance.py compare artifacts/performance/baseline.json artifacts/performance/after.json

# AIRPODS / LIBREPODS (one-time laptop setup)
# Dependencies and hardware verification: docs/airpods.md
scripts/build-librepods.sh
sudo scripts/install-airpods.sh --install
systemctl --user daemon-reload
systemctl --user start daisyos-librepods.service

# CONTINUITY (opt-in native session MVP)
dotnet test tests/DaisyOS.Continuity.Tests/DaisyOS.Continuity.Tests.csproj
scripts/build-continuity.sh
scripts/install-continuity.sh
systemctl --user daemon-reload
systemctl --user start daisyos-continuity.service
# Review full fingerprint pairing and limitations before enabling sharing:
# docs/continuity/OPERATIONS.md
# Isolated smoke test; does not install/enable a real user service:
dbus-run-session -- python3 scripts/test-continuity-daemon.py
