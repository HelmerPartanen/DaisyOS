# AI Agent Guidelines

Always keep the project safe and incremental.

- Update `ROADMAP.MD` when finishing tasks.
- Mark completed tasks with `[x]`.
- Mark active tasks with `[-]`.
- Mark blocked tasks with `[!]`.
- Add notes when behavior is uncertain.
- Do not implement destructive disk logic without explicit approval.
- Do not remove safety checks.
- Do not hardcode private user paths.
- Prefer scripts with clear variables.
- Keep shell scripts safe with `set -euo pipefail`.
- Avoid unnecessary dependencies.
- Keep UI code organized using MVVM.
- Keep system command wrappers separate from UI code.
- Prefer mock services for development.
- Never use fractional font sizes
