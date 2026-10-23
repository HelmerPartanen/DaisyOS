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
