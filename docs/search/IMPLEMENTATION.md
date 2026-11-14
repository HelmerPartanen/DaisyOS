# Daisy Search implementation and verification

Daisy Search is a process-owned, offline platform service. The launcher consumes
it; application discovery, file indexing, settings writes and compositor actions
have independent owners. This report records implemented behavior and verification
limits, rather than claiming that every optional desktop capability is available.

## Architecture and files

- `src/DaisyOS.Core/Search/`: typed envelopes and payloads, provider/indexer
  contracts, sessions/coordinator, ranking, query filters, typed settings registry,
  application/settings-page/action/calculator providers, bounded diagnostics.
- `src/DaisyOS.System/Search/`: SQLite/FTS5 storage, Linux descriptor-based file
  access, background indexing, filesystem monitoring, reconciliation, persistent
  file usage history, file/recent providers and service lifecycle.
- `src/DaisyOS.Shell/Services/DaisySearchService.cs`: shell-owned composition,
  catalog refresh, actual settings delegates, optional hardware probes, result
  invalidation and shutdown.
- `src/DaisyOS.Shell/Services/SearchKWinProvider.cs`: existing KWin bridge adapter.
- Launcher ViewModels and `Views/Components/Launcher/LauncherView.axaml*`: mixed
  virtualized results, controls, contextual actions, keyboard activation,
  clipboard boundary, empty/error feedback and background refresh.
- `App.Search.cs`, `App.Applications.cs`, `SearchStatusReport.cs` and the existing
  source-generated JSON context: line-framed diagnostics/control IPC. Standalone
  Settings requests the desktop's status and never constructs a second indexer.
- `Apps/System/Settings/SettingsView.Search.cs` and navigation changes: index
  status, counts, progress, database size, reconciliation time, watcher/errors,
  provider timings, average/P95 latency, pause/resume and reconciliation controls.
- Existing `SettingsView.SearchCatalog`: reused for deep links, without creating
  another settings metadata catalog.
- `AppEntry`, `DaisyLaunchClient`, `LinuxAppLauncherService`,
  `services/DaisyLaunch/main.cpp`: desktop-action metadata, validated action
  routing and fallback XDG discovery fixes.
- `UserDirectoryPaths`: XDG Public directory support.
- `DaisyOS.sln`, `tests/DaisyOS.Search.Tests/`, core/headless/catalog tests and
  native launcher test script: regression and integration coverage.
- `scripts/search-performance/` and `scripts/search-index-performance/`:
  repeatable benchmarks, including an actual 10,000-file provider workload.

Dependency: `Microsoft.Data.Sqlite` **10.0.15**, supplying SQLite and FTS5. The
initial 10.0.0 restore reported a vulnerable native dependency; it was replaced
before validation. No separate search daemon was introduced. Linux interop is
isolated in the System project; `AllowUnsafeBlocks` supports generated P/Invoke
marshalling.

## Delivered behavior by blueprint phase

1. **Core architecture.** Independent sessions cancel superseded work. Generation
   checks also protect queued UI updates. Providers have deadlines, bounded shared
   concurrency and per-provider execution slots. Extended providers cannot occupy
   every worker needed for immediate results. Exceptions and non-cooperative
   timeouts are isolated; abandoned work retains its execution slot until exit.
   Results publish incrementally, deduplicate equivalent entities across providers,
   and sort deterministically. Diagnostics retain bounded timing samples, not queries.
2. **Applications.** Existing KService/KIO and DaisyLaunch remain authoritative for
   XDG/Flatpak discovery and launch semantics. Desktop actions now reach the C#
   catalog and contextual results, with IDs validated against the catalog and native
   broker. Fallback discovery respects nested desktop IDs, hidden-entry precedence,
   desktop visibility, XDG directory ordering, terminal apps, generic names and
   keywords. Launch activation-token behavior is retained for primary app actions.
3. **Settings.** One metadata/delegate registry supports Boolean, choice and numeric
   settings, unique IDs, validation, serialized writes, actual-state readback,
   external updates and failure rollback. Inline controls cover theme, accent,
   contrast, reduced motion, panel borders, taskbar auto-hide, do-not-disturb,
   and available Bluetooth/Wi-Fi/audio mute integrations. Other catalog entries
   open the existing Settings page at the relevant section/setting. No duplicated
   configuration state or fake hardware controls.
4. **Persistent indexing.** SQLite schema/version checks, WAL, foreign keys,
   parameterized/prepared commands, transactional batches and synchronized FTS5
   triggers. Startup integrity checks quarantine corrupt databases and rebuild.
   Previously indexed data can be searched while reconciliation runs. Only names
   and metadata are indexed; file contents are not read.
5. **Monitoring.** Linux `FileSystemWatcher` supplies recursive inotify-backed
   monitoring. A bounded queue coalesces events. Creates, deletes, renames and
   populated directory moves update the index. Watch errors recreate watchers and
   request reconciliation; queue overflow requests reconciliation. A periodic full
   reconciliation repairs missed events, including changes while stopped or paused.
   Scanning streams directory entries and writes batches of 250, with a depth cap
   of 128 and small yields between batches. Pause is acknowledged by the worker
   after current mutations stop, rather than being reported prematurely.
6. **Retrieval/ranking.** Unicode normalization, punctuation/case handling, exact,
   prefix, word-prefix, acronym, alias, metadata and bounded one-edit/transposition
   matching. Indexed exact/prefix filename lookups precede bounded FTS candidates;
   the entire posting list is not ranked. File candidates are permission-checked
   through Linux descriptors without enumerating directories during queries.
7. **System integration.** Open-window search reuses the compositor-owned snapshot.
   Activation and restoration of already-maximized windows use the existing bridge;
   operations without per-window capability evidence are not offered. KWin 6.8.2's
   `/KWin` and `/Scripting` public D-Bus interfaces were inspected read-only. Optional
   hardware adapters probe independently with deadlines and hide unavailable inline
   controls. No X11 global enumeration or keyboard injection.
8. **Usage/actions/calculator.** Existing local app usage boosts are retained and
   capped. Successful file opens persist private local recent history; type `recent`
   or `recent report` to retrieve it. Recent/file duplicates collapse into one result.
   Arithmetic uses a bounded parser for `+ - * / % ^` and parentheses, with finite
   result checks. Predefined actions open Settings/display settings/terminal, lock
   the session or invoke the screenshot tool. No raw query execution; destructive
   or session-ending actions were not added without a confirmation flow.
9. **Presentation/performance.** Existing browse tiles, paging and icons are
   preserved. Searches use a virtualized mixed-result list. Up/Down select, Enter
   activates, Tab reaches controls, Space operates a focused toggle and Alt+Enter
   opens contextual actions. File/index/window changes refresh active searches,
   coalescing invalidation and preserving unchanged rows and pending controls.
   Existing results stay visible while queries run; updates preserve containers.
   Index progress lives in Settings rather than animated launcher loading bars.

## Configuration and privacy

Database: `$XDG_DATA_HOME/daisyos/search/search.db`, defaulting to
`~/.local/share/daisyos/search/search.db`. Its directory is mode 0700 and database
mode 0600. Queries and paths are not logged or transmitted by the search service.

Default roots use XDG Desktop, Documents, Downloads, Pictures, Videos, Music and
Public locations; Home itself is not automatically indexed. Hidden paths and
symlinks are skipped. Every path component is opened using `openat` with
`O_NOFOLLOW`; directory enumeration uses held descriptors. Inaccessible or replaced
candidates are rejected before presentation/action dispatch. Indexing refuses root.

Optional configuration files under `$XDG_CONFIG_HOME/daisyos/search/` (default
`~/.config/daisyos/search/`):

- `roots.txt`: one absolute root per line. When present, replaces the defaults;
  an empty file selects no roots.
- `exclusions.txt`: one absolute excluded file/subtree per line.

Blank lines and lines starting with `#` are ignored. Restart the shell to load
configuration changes. Pause/resume and reconciliation are available under
**Settings → Daisy Search**; pause lasts for the current service lifetime.

## Validation

Completed checks:

| Check | Result |
| --- | --- |
| Core tests, including cancellation and provider starvation | 73 passed |
| Search/index integration tests | 13 passed |
| Existing application catalog/launch and XDG directory tests | 14 passed |
| Launcher/settings headless regression selection | 128 passed |
| Final launcher, settings-shortcut and diagnostics IPC selection | 20 passed (17 overlap the broader selection; 3 add focused keyboard/IPC coverage) |
| Native DaisyLaunch build | Passed |
| Native launcher private-bus suite | Passed, including desktop-action catalog metadata |
| Rust broker private-bus suite | Passed |
| Whitespace validation (`git diff --check`) | Passed; repository line-ending conversion notice only |

No tests were skipped in these selections. The headless checks also built the
shell. Existing compiler/analyzer warnings remain, including unrelated LocalSend
trimming/AOT warnings. Interrupted builds and a shared-output build collision were
rerun; the counts above refer to successful runs. The pause-state regression found
in validation was fixed and the complete integration selection rerun successfully.
The native scripts use private D-Bus sessions and disposable catalogs, not the
user's desktop session.

```sh
dotnet test tests/DaisyOS.Core.Tests --no-restore -m:2
dotnet test tests/DaisyOS.Search.Tests --no-restore -m:2
dotnet test tests/DaisyOS.Tests --no-restore -m:2 \
  --filter 'FullyQualifiedName~DaisyLaunchCatalogTests|FullyQualifiedName~LinuxAppLauncherServiceTests|FullyQualifiedName~UserDirectoryPathsTests'
dotnet test tests/DaisyOS.ShellDemo.Tests --no-restore -m:2 \
  --filter 'FullyQualifiedName~Launcher|FullyQualifiedName~SearchResultTracksQuery|FullyQualifiedName~LaunchPipelineTests|FullyQualifiedName~Settings'
bash scripts/build-daisy-launch.sh
python3 scripts/test-daisy-launch.py
python3 scripts/test-launch-broker.py
```

Benchmarks:

```sh
dotnet run --project scripts/search-performance -c Release
dotnet run --project scripts/search-index-performance -c Release
dotnet run --project scripts/search-index-performance -c Release -- --end-to-end
```

The million-record benchmark uses synthetic database metadata. Its retrieval time
excludes candidate filesystem permission checks. The end-to-end benchmark creates
10,000 actual files, runs the indexer, and includes the file provider's checks.
`ColdRetrievalMs` means the first query after seeding, including JIT on the first
size; it is not a controlled cold OS-cache measurement. `ManagedBytes` is a heap
snapshot, not peak memory/RSS. Timing targets are engineering goals, not universal
hardware guarantees.

## Final measured performance

Application/settings benchmark: 250 warm samples per dataset. File retrieval:
100 samples per size across a common name prefix, a complete word, an exact
filename, a filtered query and a missing query. These are measurements on this
development machine, not guarantees for other hardware.

| Dataset | P95 milliseconds |
| --- | ---: |
| Application search, 1,000 apps | 0.983 |
| Application search, 10,000 apps | 1.883 |
| Settings search, 1,000 settings | 0.583 |
| First snapshot, 10,000 apps + 1,000 settings | 1.056 |
| Aggregation, 10,000 apps + 1,000 settings | 2.527 |
| Indexed retrieval, 10,000 records | 1.299 |
| Indexed retrieval, 100,000 records | 1.326 |
| Indexed retrieval, 500,000 records | 0.879 |
| Indexed retrieval, 1,000,000 records | 0.708 |
| File provider with permission checks, 10,000 actual files | 6.954 |

The actual-file workload indexed 10,101 entries (files, subdirectories and root)
in 1,281 ms. The synthetic million-record workload seeded and measured its four
sizes in 64.1 seconds. Its main database occupied approximately 639 MiB; this is
not a measured million-file filesystem scan. The final provider heap snapshot was
about 1.01 MiB; it is not a peak-memory measurement.

First-snapshot timings include empty snapshots for missing queries, rather than
measuring only first nonempty results.

Raw results, including per-query retrieval timings:

- `benchmark-applications-2026-11-14.jsonl`
- `benchmark-index-2026-11-14.jsonl`
- `benchmark-provider-2026-11-14.jsonl`

A filtered-query performance regression exposed by the million-record workload
was corrected: filtered raw text skips filename equality/prefix prechecks, and
FTS drives candidate joins. Per-query timing is now retained in the benchmark to
make that case visible. A keyboard regression check also verifies that Enter
activates the focused result and opens the Settings page for numeric settings.

## Practical limits and deployment

- Live Bluetooth/Wi-Fi/audio writes, screenshot permissions and window activation
  need acceptance in the user's target session; automated tests do not claim those
  hardware/session operations were performed.
- Inline controls cover Boolean and choice values. Numeric/color/file/string editing
  and other settings remain available through deep links to their existing pages.
- Explicit file filters are supported; arbitrary natural-language command parsing,
  package-manager search and AI are not enabled. File contents are not indexed.
- Fuzzy file retrieval considers a bounded prefix candidate set, not the whole
  database. Very unusual prefix distributions or network-filesystem stalls can
  exceed a latency target; provider deadlines protect immediate results.
- A watch count is the number of configured root watchers, not the kernel's total
  number of recursive inotify watches. Reconciliation remains necessary on network
  filesystems and after watch exhaustion. Index state is degraded on incomplete scans.
- Startup repairs corrupt databases; a runtime database failure is reported as
  unavailable and can require a service/session restart.
- Rebuild/publish the shell and the DaisyLaunch helper using the existing project
  workflow, then restart the relevant session/service. Building does not replace
  the already-running desktop or broker. Older helpers simply omit desktop actions.
- The full solution suite and a live end-to-end Wayland/hardware acceptance pass
  are separate from the focused checks listed here.


## Launcher settings interaction policy

- One inline editor replaces its equivalent Settings-page result. Theme is a single
  selector, including dark mode; there is no separate dark-theme switch.
- Only Boolean and choice settings appear inline. Numeric settings use the
  existing Settings catalog and navigation; the launcher has no sliders or Apply buttons.
- Page metadata explicitly identifies equivalent inline settings. Broader tasks
  remain separate: Wi-Fi networks and Bluetooth devices are distinct from radio switches.
- When an inline read fails, its registered page action replaces the unavailable
  editor. Successful reads restore editing. Rejected writes retain confirmed values
  and an error message, and writes have a bounded waiting period.
- Dropdowns, their items, switches and scrollbars retain native pointer handling.
  Search scrolling bypasses application paging. Up/Down and Page Up/Down bring the
  selected result into view; Enter operates switches or opens a choice menu.
- Tests cover pointer-opened dropdowns, keyboard commits, wheel scrolling, long-list
  keyboard navigation, numeric page links, equivalent-result suppression, fallback
  restoration, read failures/recovery and existing write-error behavior.

Native launcher choice menus use the system flyout layer-shell surface, shared
with the power menu, and register their material frame with that surface's
`KWinBlur`. Windowed hosts retain ordinary Avalonia dropdowns. Native placement
converts selector corners with `PointToScreen` / `PointToClient`, without manual
render-scale division, then clamps the card and flips it above the selector when
needed. The standard popup closes after its opening notification completes;
mouse and keyboard selection operate the overlay list, with focus restored on
close. Geometry and interaction tests cover 100%, 125%, 150%, 200%, and mixed
source/overlay scales. Headless checks verify geometry and event routing; actual
compositor blur uses the existing power-menu pipeline.
