# Security Policy

## Supported versions

Security fixes target the latest release on the `main` branch. Older releases may lack current Hook and Windows compatibility fixes.

## Reporting a vulnerability

Please use GitHub's private vulnerability reporting for this repository when it is enabled. Include the affected version, operating system, reproduction steps, impact, and any relevant log excerpt with secrets removed.

Please avoid posting an undisclosed vulnerability in a public issue. Do not include API tokens, private keys, certificate passwords, personal instruction-library files, or task state files in a report.

The plugin stores its runtime data locally under `%CODEX_HOME%\\instruction-switcher` and connects to Codex's loopback debugging endpoint for focus tracking. Reports involving those boundaries should describe the exact host and Codex versions.
