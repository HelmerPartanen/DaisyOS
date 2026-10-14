# App startup times

Median startup times; lower is better. Dates use Europe/Helsinki. Seven measured launches per app, two discarded warmups, warmed OS caches.

## 2026-10-13 15:55:11 — baseline

| App | Startup time |
| --- | --- |
| Settings | 2.63 s |
| Notes | 2.54 s |
| Files | 2.62 s |

[Full report](artifacts/performance/baseline.json)

## 2026-10-13 16:00:34 — repeatability

| App | Startup time |
| --- | --- |
| Settings | 2.62 s |
| Notes | 2.53 s |
| Files | 2.62 s |

[Full report](artifacts/performance/repeatability.json)

## 2026-10-13 16:30:40 — Chrome baseline

| App | Startup time |
| --- | --- |
| Google Chrome | 1.33 s |

[Full report](artifacts/performance/chrome-baseline.json)

The first two runs used unchanged code; small differences reflect measurement variation. Chrome was measured separately afterward. DaisyOS apps measure initial-content render completion; Chrome measures the local page’s first-contentful-paint callback with a fresh temporary profile. Neither measures physical screen presentation.

## 2026-10-13 16:31:34 — full benchmark with Chrome

| App | Startup time |
| --- | --- |
| Settings | 2.57 s |
| Notes | 2.45 s |
| Files | 2.67 s |
| Google Chrome | 1.31 s |

[Full report](artifacts/performance/with-chrome.json)

## 2026-10-13 20:09:57 — launch pipeline implementation

| App | Startup time |
| --- | --- |
| Settings | 2.49 s |
| Notes | 2.50 s |
| Files | 2.62 s |
| Google Chrome | 1.33 s |

[Full report](artifacts/performance/launch-optimization-verified.json)
OS score: **516.5**.

The saved baseline used a different theme and wallpaper. These startup differences are qualified and cannot be attributed solely to launch optimization. The startup suite launches binaries directly; service dispatch is measured separately below.

### 2026-10-13 20:08:55 — launch dispatch (30 samples)

| Route | Median | P95 |
| --- | --- | --- |
| Original direct launcher | 21.39 ms | 27.63 ms |
| DaisyLaunch service | 2.28 ms | 2.62 ms |
| KIO fallback | 159.60 ms | 167.33 ms |

Interleaved routes on the same machine, using a windowless `/bin/true` fixture and the real C# client. Dispatch acceptance is not app readiness or first presentation. The private-bus fixture does not include production systemd unit startup.

[Dispatch report](artifacts/performance/launch-dispatch-verified.json)

## Future benchmarks

Chrome is included by default:

```bash
python3 scripts/benchmark-os-performance.py run --build --output artifacts/performance/after.json
python3 scripts/benchmark-os-performance.py compare artifacts/performance/launch-optimization-verified.json artifacts/performance/after.json
```

Append a dated table like those above, using each app’s median milliseconds divided by 1000. Retain the raw report. Compare Chrome against a full report containing Chrome with the same browser version. Chrome timings are tracked separately from the existing OS score. The log is maintained manually.

## 2026-10-13 20:54:35 — launch route diagnosis

| Settings launch route | Startup time |
| --- | --- |
| Direct Release binary | 2.51 s |
| Desktop shortcut (GIO) | 2.44 s |
| Persistent KIO service | 2.43 s |

Three samples per route, interleaved, with identical isolated data and the same Release binary. This measures initial content rendered; it does not measure pointer input or physical presentation. No additional multi-second backend delay was observed. [Route report](artifacts/performance/gui-launch-routes-verified.json).

Live launchers were using Debug builds, while the standard benchmark uses Release. Diagnostic single runs before correcting the launchers:

| App | Debug | Release |
| --- | --- | --- |
| Settings | 2.82 s | 2.55 s |
| Notes | 3.32 s | 2.57 s |
| Files | 2.93 s | 2.58 s |

These single runs identify a configuration mismatch; they are not an OS score comparison. Installed app launchers and managed desktop shortcuts now use Release. Native Development retains a Debug shell and supports explicit Debug app builds via `DAISYOS_APP_CONFIGURATION`. Settings, Notes and Files now use the built-in taskbar route.
