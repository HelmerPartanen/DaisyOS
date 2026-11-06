# Native UI migration workbench

Read [the audit, status and remaining cutover gates](RUST_SLINT_MIGRATION.md).
The default session still uses the existing Avalonia shell and applications.

```sh
bash scripts/build-rust-ui.sh
./target/debug/daisy-calculator
./target/debug/daisy-ui-gallery

# Standard/scientific state can also be exercised without starting a display.
./target/debug/daisy-calculator --evaluate 2 + 3 '*' 4 =
```

Rust 1.92+ and a working Wayland session are required. The workspace pins Slint
1.18.1 and disables its default framework/backend bundle. It uses winit, the
software renderer, the existing font/icon assets and native accessibility.
`--release` is supported by the build script for subsequent performance work;
the current verification results describe the dev build, not release timings.

Useful verification commands:

```sh
cargo test --workspace --locked
cargo clippy --workspace --all-targets --locked -- -D warnings
python3 scripts/test-calculator-parity.py
python3 scripts/test-slint-ui.py
python3 scripts/audit-avalonia-ui.py --check
python3 scripts/generate-slint-tokens.py --check
python3 scripts/generate-slint-locales.py --check
```

The parity harness requires .NET 10 and compiles the actual C# calculator view
model as a logic oracle. The UI smoke test requires the target KWin compositor;
it captures only the test app clients, submits compositor blur requests and
uses temporary appearance preferences. It never modifies user preferences or
replaces a running service. Images, protocol evidence and results are written
under `artifacts/rust-ui/acceptance`.

`scripts/calculator-visual-reference` can capture existing Avalonia Calculator
views using the already-built Release shell assemblies and headless backend.
These are review references, not a proof that the full shell's native frame and
compositor pixels are identical. Run it from the repository root:

```sh
dotnet run --project scripts/calculator-visual-reference/CalculatorVisualReference.csproj \
  -c Release -- artifacts/rust-ui/reference
```

Do not promote the Rust executables to default launch routes before the ledger's
visual, activation, fullscreen, accessibility and platform gates are satisfied.
