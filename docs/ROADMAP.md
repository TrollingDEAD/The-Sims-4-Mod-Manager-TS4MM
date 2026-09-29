# Roadmap

A working list of ideas for where TS4MM could go next. Nothing here is a promise or a schedule -
it is a backlog we pull from, grouped by area. Items move to [CHANGELOG.md](../CHANGELOG.md) once
shipped; this file gets trimmed as that happens. Suggestions and votes are welcome via
[issues](https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/issues).

Legend: ✅ shipped (kept here briefly for context) · 🔧 in progress · nothing = not started.

## Known issues

- ✅ Disabling a mod had a large delay before the UI reflected it, and re-enabling a previously
  disabled mod didn't work at all - confirmed fixed by actually driving the app (Windows UI
  Automation + screenshots) against a real 2266-mod library, not just by reading the code - see
  [CHANGELOG.md](../CHANGELOG.md).
- ✅ Accent color picker presets were mismatched: picking a new color left some controls ("Play",
  "Fix all safe ones", the tab badges) stuck on the previous accent color indefinitely. Confirmed
  and fixed with actual pixel-level before/after comparison of a running instance, not a guess -
  see [CHANGELOG.md](../CHANGELOG.md).
- ✅ The "Export list..." button's icon (and two others: the mod-list "replaces game" indicator,
  and the "Als Sammelordner" button) rendered as a blank/broken glyph - see
  [CHANGELOG.md](../CHANGELOG.md).
- 🔧 General UI consistency pass: audit borders, padding, and control sizing (dropdowns, buttons,
  placement) across all screens and window sizes via a systematic screenshot review, to catch
  design inconsistencies that have accumulated across features. (Every `SymbolIcon` usage was
  already swept for the missing-glyph bug above - no further icons need checking there, just new
  ones added later.) Covered so far by actually driving the app and screenshotting it: Overview
  (✅ found and fixed the "Affected: N" dropdowns being inconsistently sized per card - see
  [CHANGELOG.md](../CHANGELOG.md)), Mods (including the checkbox-column fixes above), Catalog,
  Diagnose - otherwise looked visually consistent. Not yet covered: Library (Tray), Saves, Storage,
  History, Game, Updates, the Conflicts/Solutions/Resources sub-tabs, and the Setup/Merge/Sort
  dialog windows.

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
- ✅ Full in-app CurseForge mod browser (the "CurseForge" tab's new "Browse" sub-tab): search/browse
  the Sims 4 catalog by keyword, category and sort order, install with one click, with required and
  optional dependencies installed automatically and the result sorted into its category/creator
  folder the same way a manual "Sortieren" pass would - install + dependencies + sort all undo as a
  single History entry. Verified against the live API (not just the fingerprint-based update
  checker's mocked tests): a real install pulled in 12 real dependencies, created the right
  category/creator folders, and one Undo cleanly reversed all 38 affected files. See
  [CHANGELOG.md](../CHANGELOG.md).
- Status indicators in the mod list: small icons per row for update-available, broken, conflicting,
  and other warnings, with mod names still aligned consistently across rows so the list stays
  clean regardless of how many indicators a given mod has. The existing conflict-warning icon's
  name-alignment bug (✅ fixed - see [CHANGELOG.md](../CHANGELOG.md)) is the pattern to reuse for
  each new icon: reserve its slot with `Visibility="Hidden"`, not `Collapsed`, so the name column
  never shifts depending on which icons a given row happens to show.
- Automatic background CurseForge update check (e.g. once on startup, like the silent app-update
  check already does): today `UpdatesViewModel.CheckAsync` only runs from the "Check now" button,
  so the Updates tab's new notification badge only ever reflects the last manual check. Needs an
  API-key precondition (skip silently without one) and rate-limit-friendly pacing since it's a
  real network call, unlike the other two tabs' badges which piggyback on the existing rescan.
- Deeper mod optimization tooling: go beyond the current diagnostics to automatically detect and
  fix more problem classes, plus performance-focused operations for large mod lists - texture
  scaling/downscaling, debloating unused resources, and script merging.

## Conflict resolution

- Conflict resolution history: remember which proposal was applied for a given conflict group so
  re-running the resolver after adding mods doesn't ask about already-decided conflicts again.
- Severity-weighted "safe resolutions" preview before applying, showing exactly which files change.
- Export a conflict report (already possible for the mod list) as a shareable document for asking
  for help in the community.
- Persistent "ignore this conflict" dismissal for overlaps a user has reviewed and accepted (e.g. an
  intentional override), so it stops resurfacing in the count on every rescan without disabling
  either file.

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
- Apply the startup scan's background-thread pattern (`Task.Run` + `MainViewModel.ApplyScanResult`)
  to the manual Refresh button too, now that it's been proven live (the UI stayed interactive
  mid-scan): show a small inline "Scanning…" indicator instead of blocking, rather than the current
  brief freeze. Deliberately deferred rather than shipped alongside the startup version, to keep that
  change reviewable on its own.

## Safety & reliability

- Mod archive virus/heuristic scanning: optional integration with a scanning API (e.g. VirusTotal)
  for freshly downloaded script mods, alongside the existing dangerous-pattern scan.
- ✅ Settings/profile backup-and-restore as a single portable file.
- Verify-after-write: re-read a file immediately after a toggle/move/merge operation to catch
  silent write failures (locked file, out of disk space, antivirus quarantine) before they're
  mistaken for a successful change.
- Crash reporting opt-in: let users send `error.log` contents (with a review/redact step) instead
  of only being told the local log path.
- Backup integrity check: after a scheduled automatic backup runs (see the toast it now reports
  through), periodically verify a recent one actually restores cleanly instead of only confirming the
  copy succeeded, so a silently corrupt backup is caught before it's needed.

## App-level features

- ✅ "What's new" dialog shown once after an update, sourced from CHANGELOG.md.
- ✅ Keyboard shortcuts reference (F1 / title-bar button).
- ✅ Update channel toggle (stable / pre-release), live without a restart.
- ✅ Accent color customization (the paint brush icon in the title bar): eight presets plus "Windows
  default", applied live via `Wpf.Ui`'s `ApplicationAccentColorManager` and included in the portable
  settings backup.
- ✅ A lightweight in-app toast area (bottom-right) for background events that would otherwise only
  show up as status bar text easily overwritten before anyone reads it: a silent app-update check
  finding a new version, and a scheduled save/Tray backup running at startup.
- A custom accent color beyond the eight presets (a small hex/RGB picker), for the paint brush menu
  added above.
- A toast history: since toasts auto-dismiss after a few seconds, a small bell icon with the last
  handful of notifications so one missed while away from the keyboard isn't lost for good.
- Minimize-to-tray and an optional "launch with Windows" setting, for people who leave the app
  running alongside the game rather than closing it after "Spielen".
- ✅ Tab badge indicators: a small red-dot/count badge on the "Updates", "Diagnose" and
  "Übersicht" tabs, the way a phone app icon badges its notification count - see
  [CHANGELOG.md](../CHANGELOG.md). The Updates badge only reflects the last manual check
  (`UpdatesViewModel.CheckAsync`, a "Check now" button) - there's no automatic background scan to
  drive it yet, unlike the other two, which refresh with every mod rescan. Making the CurseForge
  check itself run automatically (e.g. once on startup, like the silent app-update check) is a
  separate, bigger piece of work: network calls, an API key precondition, and rate limits to
  respect - not done here.

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
