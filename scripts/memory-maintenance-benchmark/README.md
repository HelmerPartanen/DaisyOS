# Memory maintenance policy benchmark

Run before changing the service, then rebuild and run after:

```sh
dotnet run --project scripts/memory-maintenance-benchmark -c Release > /tmp/before.json
dotnet run --project scripts/memory-maintenance-benchmark -c Release -- --verify > /tmp/after.json
```

Each scenario uses two warmups and 15 measurements. The benchmark links the
production service and calls its timer body directly to avoid multi-minute
waits. Only cache dependencies are stubbed; GC, finalizer waiting and native
heap trimming execute normally. The live fixture holds 100,000 byte arrays of
128–255 bytes. Timers are disposed after every sample. The constructor probe
supports the original two-parameter service for before measurements.

`gaming` simulates the queued hidden-shell request; `returned-visible` simulates
a request whose four-second delay expires after returning to the desktop;
`locked` checks that hidden maintenance still runs outside games;
`pressure-during-game` exercises the non-aggressive path as well. The verification
checks collection counts and cache-reclaim calls. Timing is descriptive: this
harness does not measure game frames, UI pauses, full cache eviction cost, or
retained memory in a real session.

Run in a quiet session for timing comparisons. Retain raw JSON and record build,
SDK and machine context. The dated validation document records the local run.
