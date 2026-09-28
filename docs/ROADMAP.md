# Roadmap

A working list of ideas for where TS4MM could go next. Nothing here is a promise or a schedule -
it is a backlog we pull from, grouped by area. Items move to [CHANGELOG.md](../CHANGELOG.md) once
shipped; this file gets trimmed as that happens. Suggestions and votes are welcome via
[issues](https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/issues).

Legend: ✅ shipped (kept here briefly for context) · 🔧 in progress · nothing = not started.

## Mod management

- ✅ Favorite mods (star toggle + filter).
- Bulk tagging: apply/remove a tag across the current selection or filtered view, instead of one
  mod at a time.
- Drag-and-drop install: drop a downloaded `.zip`/`.7z` mod archive onto the window and have it
  extracted into the Mods folder (with the same download-safety scan already applied to watched
  Downloads).
- Load-order-style "always enable together" grouping for mods that depend on each other, beyond
  what folder-based collections already give.
- Mod dependency hints: detect known requirement patterns (e.g. a CAS mod referencing a mesh
  pack) and surface them the way missing-library detection already does for S4CL/XML Injector.
- Bulk "why is this here" report: export not just the mod list but the full note/tag/reason set in
  one document, for people who maintain a curated pack across machines.
- Rename-safe move: relocate a mod between collection subfolders from inside the app instead of
  Explorer, updating notes/tags/profile references in one step.

## Conflict resolution

- Conflict resolution history: remember which proposal was applied for a given conflict group so
  re-running the resolver after adding mods doesn't ask about already-decided conflicts again.
- Severity-weighted "safe resolutions" preview before applying, showing exactly which files change.
- Export a conflict report (already possible for the mod list) as a shareable document for asking
  for help in the community.

## Performance & architecture

- ✅ Async mod scan at startup: the very first scan now runs on a background thread
  (`MainViewModel.RescanModsForStartupAsync`), so the main window appears immediately instead of
  waiting for `ModScanner.Scan()`/`ConflictDetector.FindConflicts()` to finish. Scoped
  deliberately to just that one call site - every other rescan (Refresh button, undo, switching
  Mods folders, ...) stays synchronous, where a brief block during an explicit user action is an
  acceptable, well-understood tradeoff not worth threading through all 8 call sites for.
- Incremental rescans: re-scan only the parts of the Mods folder that changed (via file-system
  watcher events already used for download detection) instead of a full walk every time.
- Virtualize the package-contents resource list for very large `.package` files instead of
  materializing the full resource table up front.

## Safety & reliability

- Mod archive virus/heuristic scanning: optional integration with a scanning API (e.g. VirusTotal)
  for freshly downloaded script mods, alongside the existing dangerous-pattern scan.
- ✅ Settings/profile backup-and-restore as a single portable file.
- Verify-after-write: re-read a file immediately after a toggle/move/merge operation to catch
  silent write failures (locked file, out of disk space, antivirus quarantine) before they're
  mistaken for a successful change.
- Crash reporting opt-in: let users send `error.log` contents (with a review/redact step) instead
  of only being told the local log path.

## App-level features

- ✅ "What's new" dialog shown once after an update, sourced from CHANGELOG.md.
- ✅ Keyboard shortcuts reference (F1 / title-bar button).
- ✅ Update channel toggle (stable / pre-release), live without a restart.
- ✅ Accent color customization (the paint brush icon in the title bar): eight presets plus "Windows
  default", applied live via `Wpf.Ui`'s `ApplicationAccentColorManager` and included in the portable
  settings backup.
- A lightweight in-app notification/toast area for background events ("update check finished",
  "12 mods updated") instead of only the status bar text.

## Localization & accessibility

- Additional UI languages beyond German/English - French, Spanish and Portuguese cover a large
  share of the Sims 4 modding community. The existing `L.T`/`L.F` + per-language JSON dictionary
  architecture already supports adding a language file without touching call sites.
- Screen-reader pass: audit `AutomationProperties.Name` coverage on icon-only buttons (several
  exist in the mod grid and title bar) and custom controls.
- High-contrast theme support, verified against Windows' built-in high-contrast modes rather than
  only the app's own dark/light themes.

## Testing & CI

- ViewModel-level tests for `Sims4ModManager.App` - `tests/Sims4ModManager.Core.Tests` covers Core
  thoroughly, but the App project's view models (filtering, favorite/note state, command wiring)
  currently have no automated coverage of their own.
- Code coverage reporting in CI, surfaced as a badge on the README.
- A smoke test in the release workflow that actually launches the packed installer output, beyond
  the current build-and-unit-test gate.

## Documentation & community

- A short video/GIF walkthrough embedded in the README for the features hardest to describe in
  text (conflict resolution, the merge tool).
- A `docs/FAQ.md` seeded from recurring issue-tracker questions once there's a track record to
  draw from.
