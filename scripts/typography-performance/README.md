# Shared typography performance and parity

Build the shell and benchmark in Release, then retain JSON before and after a
change:

```sh
dotnet build src/DaisyOS.Shell/DaisyOS.Shell.csproj -c Release
dotnet run --project scripts/typography-performance -c Release > /tmp/typography.json
```

The harness uses the production typography policy with headless Skia and the
bundled/system font fallback configuration. It records 30 warm calls after two
discarded warmups, including current-thread allocations and elapsed time.
Cold font initialization, physical presentation and application readiness are
not measured by these samples.

It then verifies every role at each integer scale from 90% through 150% for nine
font families/fallback requests, including integer-rounding regressions. The independent reference measures the original
multilingual string separately at each role's font size. A nonzero exit or any
mismatch rejects the optimization. Corresponding parity checks also live in
`LanguageTypographyTests`.

Use `-- --all-fonts` with `dotnet run` to extend parity checking to every
installed font family. This can take several minutes and retain native font
caches until the harness exits; it runs after the timed warm-update samples.
Progress goes to stderr and the complete JSON report remains on stdout.
