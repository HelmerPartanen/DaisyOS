# Rust UI migration verification

This validates the first migration slice. It does not certify the full desktop
or authorize deleting the existing framework implementation.

## Passed

- `scripts/build-rust-ui.sh`: both native executables build from the locked root
  workspace. Fonts and existing SVGs are embedded with Slint's resource option.
- `cargo clippy --workspace --all-targets --locked -- -D warnings`: no warnings.
- `cargo test --workspace --locked`: 12 tests, including percentage/immediate
  arithmetic, domain recovery, memory/mode preservation, G10 boundaries,
  compatible appearance enums/fields, timezone coordinates, rounded-mask bounds,
  real pointer-to-keyboard calculator focus/Enter behavior, and switch Space.
- `scripts/test-calculator-parity.py`: **1,213 exact** state comparisons with the
  actual C# CalculatorViewModel. Inputs include arithmetic, scientific inverse/
  hyperbolic functions, memory, angles, errors and randomized command sequences.
  Matching arithmetic operation order resolved a discovered trig discrepancy;
  the test was not weakened to hide it with numeric tolerance.
- `scripts/test-slint-ui.py`: **12 real Wayland captures** across light/dark,
  standard/scientific and 100/150/200% renderer scale; two gallery captures;
  live appearance-file theme/language change; opaque high-contrast background.
- KWin advertised ext_background_effect_v1 capability 1; the app bound that
  manager on its existing display and submitted a background-effect object and
  wl_region. Logs confirmed region submission and clean effect destruction;
  no compositor protocol error was reported. Startup polling stops on success.
- Existing Avalonia calculator references were rendered in standard/scientific,
  light/dark, using the existing Release assemblies/headless backend and current
  generated accent values. These review references are **not** a fresh complete
  source build or pixel-perfect acceptance certification.
- Source inventory, theme and RESX extraction drift checks passed. Existing
  native Rust services still report their own original workspace roots and
  retain their special profiles/dependency locks.

## Artifacts

Local generated files are under `artifacts/rust-ui/`:

- `acceptance/results.json`: case list and explicit limitations.
- `acceptance/calculator-*.png`, `gallery-*.png`: native client captures.
- `acceptance/*.protocol.txt`: filtered blur protocol evidence; input/clipboard
  traffic is not retained in these evidence files.
- `reference/calculator-*.png`: Avalonia review references.
- `resource-observation.json`: one dev-build diagnostic sample, not an A/B or
  release performance comparison. At that observation the calculator used
  46,756 KiB RSS / 38,923 KiB PSS, six threads and 17 file descriptors. Idle CPU
  ticks did not advance during an 800ms sample. Virtual size is not RAM saved.

Native client captures exclude compositor blur pixels and server decorations.
Renderer-scale coverage is not proof of multi-monitor/fractional-scale surface
parity. Do not infer a finished compositor integration from a PNG or a protocol
request alone.

## Required before cutover

- Matched native before/after captures over the same wallpaper and KWin frame,
  including blur onset, resize/theme transactions, rounded/multiple masks and
  hiding/remapping. Verify compositor pixels, not just protocol requests.
- Full native activation/single-instance behavior and existing fullscreen/window
  controls. Current Calculator is a standalone migration slice; the shell still
  routes to the functioning Avalonia Calculator.
- IME/preedit/composition, accessibility client behavior, keyboard traversal,
  typography at user-selected text sizes, and exact easing/reveal parity.
- Full component library and regional/layer-shell integration before shell/dock/
  launcher/notification/lockscreen/widget migration. Continuity remains unchanged.
- Release build measurements, animation/scrolling responsiveness under load,
  packaging and fresh-session/ISO tests. No release RAM improvement is claimed.

See the ordered [migration ledger](RUST_SLINT_MIGRATION.md) for the application
and platform backlog. The runtime integration deliberately uses the pinned
[Slint winit access API](https://docs.rs/slint/1.18.1/slint/winit_030/trait.WinitWindowAccessor.html),
which is behind an unstable feature, and the standard
[background-effect protocol](https://gitlab.freedesktop.org/wayland/wayland-protocols/-/blob/main/staging/ext-background-effect/ext-background-effect-v1.xml).
Slint's public winit window becomes available only when its event loop creates
it; the adapter handles that lazy lifetime instead of assuming `show()` exposes
an immediate native handle.
