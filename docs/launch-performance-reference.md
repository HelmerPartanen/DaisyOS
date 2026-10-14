# Launch performance reference machine

Reference captured on 2026-10-13 from `artifacts/performance/with-chrome.json`. Keep the raw report with comparisons; runtime or hardware changes require a qualified comparison.

| Property | Reference |
| --- | --- |
| CPU | Intel(R) Core(TM) i7-3770K CPU @ 3.50GHz |
| Logical CPUs | 8 |
| RAM | 16306576 kB |
| Kernel | 7.2.9-arch1-1 |
| .NET SDK | 10.0.112 |
| KWin | kwin 6.7.5 |
| CPU governor | schedutil |
| Power profile | balanced |
| GPU PCI ID | NVIDIA 10DE:1EB1 |
| Fixture storage | /tmp, tmpfs; not a physical-disk throughput test |

The dispatch fixture uses a private bus, an offscreen native daemon and a windowless `/bin/true` desktop entry. This isolates launch overhead; it does not establish physical presentation timing or systemd-backed production launch latency. The startup suite uses real visible app windows and reports render completion/local Chrome paint callbacks, not physical presentation.

The saved baseline used light theme and a video wallpaper. Later optimization runs used dark theme and a static wallpaper. Their app startup differences are qualified; they are not a controlled improvement claim. Dispatch routes are interleaved on the same running system and use identical fixture entries.
