# Hover feedback — 2026-10-08

The automatic theme-color policy supplied 160 ms background transitions to Files row containers while their presenters also animated pointer-state fills. This compounded feedback and left visible fading highlights on previously hovered rows.

Added `MotionDurationHover` at 50 ms, zero when Reduce motion is enabled. Shared button presenters/list containers, explicit Files/Settings background transitions, Files sidebar fills and pinned locations use the shorter timing. Existing general theme, image, toggle and spatial-motion durations retain their separate resources. Interactive surfaces with explicit hover transitions use those shorter transitions for background changes; the automatic theme policy preserves explicit transitions.

Files row container backgrounds stay transparent for hover and selection. Their presenter is the sole animated feedback layer, keeping selection visible without a second fading hover fill.

Fifteen focused GUI tests passed: Files light/dark pointer movement clears the previous row after the short transition and retains selection; caption Close remains immediate; existing theme synchronization and toggle behavior pass. Tests sample render ticks instead of draining animations through screenshots. Whitespace checks passed. This is automated behavior evidence, not a physical-display latency benchmark. Reopen existing system-app windows to load the rebuilt styles.

Files, Notes and Settings rebuilt with zero warnings/errors. The shell reloaded, and the strict live compositor check passed all nine requirements. Existing app processes were retained.
