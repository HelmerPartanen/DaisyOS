# System Monitor acceptance checks

This harness compiles the actual monitor C# and XAML against an existing Debug
shell assembly. It reuses the shell's themes and assets instead of rebuilding the
running desktop. Build `DaisyOS.Core` and `DaisyOS.System` first when those sources
change; the harness references their Debug outputs directly.

On a memory-constrained desktop, run builds and checks inside a bounded scope:

```sh
systemd-run --user --scope -p MemoryMax=512M -p MemorySwapMax=0 -p CPUQuota=50% \
  -- env DOTNET_GCHeapHardLimit=0x10000000 \
  dotnet build scripts/system-monitor-ui-acceptance/SystemMonitorUiAcceptance.csproj \
  -m:1 -p:RunAnalyzers=false -p:UseSharedCompilation=false

systemd-run --user --scope -p MemoryMax=384M -p MemorySwapMax=0 -p CPUQuota=50% \
  -- env DOTNET_GCHeapHardLimit=0x08000000 \
  dotnet scripts/system-monitor-ui-acceptance/bin/Debug/net10.0/SystemMonitorUiAcceptance.dll \
  /tmp/system-monitor-previews
```

Checks cover live Linux counters, hardware CPU count under a process quota,
process metadata, PID identity validation, shell/init protection, explicit signal
selection, terminating a disposable child process, sorting/filtering, stable
selection, pause state, compositor window close routing, tooltip capitalization,
and rendering at 920/1100 pixels in both themes. Screenshots are written to the
provided directory. This is headless validation; real compositor interaction
still needs a desktop-session smoke test.
