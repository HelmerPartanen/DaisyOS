# Input height follow-up — 2026-10-07

Standard single-line inputs, dropdowns and search surfaces now use 40px at normal typography. InputHeightStandard and both SearchHeight resources grow with the measured standard control height plus 8px. Existing action buttons keep their standard height. The shared search component centers its inner text and icons in the larger outer interaction surface; the launcher consumes that shared resource directly.

Local app text-field/dropdown overrides, the Settings theme picker, generated picker bindings and file-picker search use the same input contract. Multiline editors and embedded lock-screen password controls keep their container-managed sizing.

Focused checks cover rendered normal input/search/dropdown heights, 90–150% typography across four font families, Finnish Settings and confirmations, accent hover colors and Quick Settings geometry. Historical assertions equating input and button heights were updated to the new input contract. Git HEAD remains corrupt; the complete solution designer prerequisite remains as previously documented.

Final focused GUI result: 17 passed, zero failed.
Native builds and shell reload succeeded. Strict compositor preflight: zero failures and zero warnings. Existing standalone apps must be reopened to load their revised controls.
