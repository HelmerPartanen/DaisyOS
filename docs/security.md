# Security

DaisyOS must treat system actions carefully.

Rules:

- Do not pass unsanitized user input to shell commands.
- Prefer direct process arguments over shell strings.
- Capture stdout and stderr.
- Apply command timeouts.
- Handle missing commands.
- Do not implement disk formatting without explicit approval.
- Do not run installer or partitioning logic on a real machine until VM testing is complete.

Physical control is limited without Secure Boot, disk encryption, locked firmware settings, and signed updates.

