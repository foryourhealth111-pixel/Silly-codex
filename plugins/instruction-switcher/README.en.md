# Instruction Switcher

> Make each Codex task's working rules visible, switchable, and recoverable.

Instruction Switcher uses `SessionStart` and `UserPromptSubmit` Hooks to keep an independent instruction selection for every task. A Windows companion provides the visible controls.

English documentation: [Root English README](../../README.md)  |  Chinese: [README.md](README.md)

<div align="center">

<a href="../../docs/assets/instruction-switcher-overview-en.png"><img src="../../docs/assets/instruction-switcher-overview-en.png" alt="Instruction Switcher task panel overview" width="418"></a>

<br>
<sub>Native-size synthetic demo data rendered by the real Windows companion in English.</sub>

</div>

## Installation

Install Node.js 20 or newer, then run in PowerShell:

```powershell
npx --yes github:foryourhealth111-pixel/Silly-codex
```

The installer registers or upgrades the `silly-codex` marketplace and installs `instruction-switcher@silly-codex`. It has no third-party runtime dependency and no npm lifecycle script.

Manual installation remains available:

```powershell
codex plugin marketplace add foryourhealth111-pixel/Silly-codex --ref main
codex plugin add instruction-switcher@silly-codex
```

Review and trust `SessionStart` and `UserPromptSubmit` in Codex plugin settings. Restart Codex or open a new task to start the companion.

To migrate from `instruction-switcher@personal`, fully exit Codex and run:

```powershell
npx --yes github:foryourhealth111-pixel/Silly-codex --replace-personal
```

The migration keeps `%CODEX_HOME%\instruction-switcher` data. Product documentation is available in the [root English README](../../README.md).

## Companion Window

When a Codex task starts or resumes, the `SessionStart` Hook launches the companion near the lower-right corner. It can follow the selected sidebar task, accept an explicit manual target, and keep separate task state files.

Use the expanded panel, a roughly `58 x 58` floating button, or the system tray. Press `Esc` to collapse and click the floating button to expand. Light/dark theme and Chinese/English language preferences are saved in `runtime/window-position.json`.

The companion reads the Codex loopback debugging endpoint to follow the active task. When detection is unavailable, it falls back to a read-only preview or an explicitly selected task. The Hook remains the source of truth for instruction injection.

## Control Commands

```text
/choose status
/choose list
/choose set review,tdd
/choose on concise
/choose off review
/choose clear
```

Control commands are handled by the Hook and blocked from the model context. Ordinary messages receive the enabled Markdown bodies in task order.

## Library And Presets

The first run creates an editable local library:

```text
%CODEX_HOME%\\instruction-switcher\\config.json
%CODEX_HOME%\\instruction-switcher\\instructions\\
%CODEX_HOME%\\instruction-switcher\\sessions\\
```

If `CODEX_HOME` is unset, `%USERPROFILE%\\.codex\\instruction-switcher` is used. `INSTRUCTION_SWITCHER_HOME` can point to an isolated data root.

Presets replace the current task selection as a group. Manual toggles or reordering mark the selection as custom. The `Settings` window edits Markdown, creates and updates presets, imports and exports packages, backs up or restores the whole library, and contains language, theme, and data-folder controls.

New installations receive six editable default instructions and no bundled presets. Existing names, bodies, presets, and task state remain unchanged when the interface language changes. Chinese and English installations share the same default bodies and use localized display names.

## Environment And Limits

- Windows 10/11 and an interactive desktop are required for the full WinForms companion.
- Node.js 20+ is required for Hooks; CI validates Node 22.
- Remote, locked, or headless desktops can hide the companion. `/choose` still works.
- The companion starts only on Windows; the Hook and local library remain Node-based.
- All runtime data stays local. See the repository [PRIVACY.md](../../PRIVACY.md).

## Development

```powershell
powershell -NoProfile -File plugins/instruction-switcher/scripts/build-companion.ps1
node --test plugins/instruction-switcher/tests/*.test.mjs
powershell -NoProfile -File plugins/instruction-switcher/tests/companion-lifecycle.test.ps1
powershell -NoProfile -File plugins/instruction-switcher/tests/window-presentation.test.ps1
powershell -NoProfile -File plugins/instruction-switcher/tests/library-package.test.ps1
powershell -NoProfile -File plugins/instruction-switcher/tests/theme-transition-layer.test.ps1
```

The plugin is released under [Apache License 2.0](LICENSE). Author: `foryourhealth111-pixel`.
