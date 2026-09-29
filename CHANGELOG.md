# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/).

## [1.3.0] - 2026-09-29

### Added

- A "Browse" sub-tab under "CurseForge" (renamed from "Updates", which is now its sibling
  sub-tab): search and browse the Sims 4 CurseForge catalog by keyword, category and sort order
  (Featured/Popularity/Last updated/Name), with a tile grid matching the Catalog tab's visual
  style. Installing a mod automatically resolves and installs its required and optional
  dependencies too, then sorts the result into its category/creator folder the same way a manual
  "Sortieren" pass would - the whole install (mod + dependencies + sort) undoes as a single History
  entry. Mods that only allow downloads on the CurseForge website are detected up front and the
  install is cancelled with an explanation, before anything is downloaded. Shares the same API key
  as the Updates sub-tab. Verified against the live CurseForge API: a real install correctly found
  and installed 12 real dependencies, created the expected category/creator folders, and Undo
  cleanly reversed all 38 affected files in one step.
- Tab notification badges: a small red-dot/count badge on the "Updates", "Diagnose" and
  "Übersicht" tab icons when there's something to look at (updates available, new errors since
  the last game version, unresolved health findings) - the same idea as a phone app icon's
  notification badge. The Updates badge reflects the last manual "Check now" run; the other two
  update automatically with every mod rescan.

### Fixed

- In the mod list, a mod's name started further right than its neighbors' whenever it showed the
  conflict-warning triangle - the icon was `Collapsed` (removes it from layout) rather than
  `Hidden` (keeps its space reserved) when there was no conflict, so the name text shifted left to
  fill the gap. Every row's name now starts at the same position regardless of whether the icon is
  showing; the same Hidden-not-Collapsed approach should be reused for any further per-mod status
  icons added to that slot later (update available, broken, ...).
- The "Affected: N" expander on each finding in the Overview tab's Problems list was a different
  width depending on how many action buttons that particular finding happened to show (0, 1 or 2,
  of varying widths) - it was nested inside the same row as those buttons, so it only got whatever
  space they left over. Moved it to its own row below, so it always spans the full card width.
- Disabling a mod could take a noticeable moment to register, and toggling a mod you'd just
  toggled (or clicking a different mod's checkbox right after) often silently did nothing.
  Found and confirmed by actually driving the app (Windows UI Automation + screenshots) against a
  real 2266-mod library rather than reading the code alone - three compounding causes, all fixed:
  - The mod list's checkbox column was a `DataGridCheckBoxColumn`, which routes every click through
    the grid's cell "current/edit transaction" machinery: a row that wasn't already the current
    cell needed one click just to focus it and a second to actually toggle it - the "not working"
    symptom for anyone scanning down a list unchecking mods one at a time. Replaced it with a plain
    `CheckBox` in a template column (matching the star/favorite column next to it), which responds
    to every click immediately regardless of which row was current before.
  - That same edit-transaction state could still be open when the toggle's rescan applied its
    result, throwing an unhandled `InvalidOperationException` ("'Refresh' ist während einer
    AddNew- oder EditItem-Transaktion nicht zulässig") - reliably reproducible by toggling a second
    mod before the first one's rescan had finished. The toggle handler now explicitly commits any
    pending grid edit first.
  - The toggle handler also rebuilt the whole mod list (a full folder rescan + conflict detection)
    synchronously from inside the grid's own edit-commit callback, blocking the UI thread and
    replacing every row's view-model object on every single click. The rescan now runs on a
    background thread (the same pattern already used for the startup scan) and updates existing
    rows in place instead of clearing and re-adding all of them.
- The unchecked checkbox for a disabled mod rendered as a flat, borderless gray square instead of a
  normal checkbox outline, hard to even recognize as a checkbox - a side effect of
  `DataGridCheckBoxColumn`'s own styling combined with the dimmed-row opacity. Fixed by the same
  template-column change above, which uses the app's normal Fluent-styled checkbox.
- Picking a new accent color left some controls ("Play", "Fix all safe ones", the tab notification
  badges) showing the *previous* accent color indefinitely, while others (toggle switches,
  checkboxes) updated immediately - confirmed with actual before/after pixel sampling of a running
  instance. `ThemeService.Apply` applied the accent color *after* re-applying the theme; some
  control styles only pick up the accent while their theme resources are being (re-)applied, so
  anything styled that way kept resolving the color that was current before this call. Swapped the
  order (accent first, then theme) - every accent-colored control now updates together. While
  investigating, also enlarged the picker's swatches (28px, 2px apart) and gave them more breathing
  room, since a mis-click there was a real (if secondary) risk on top of the above.
- Three icons rendered as a blank/broken glyph instead of a symbol: the "Export list..." button,
  the mod list's "replaces game content" indicator, and the "Als Sammelordner" button. All three
  referenced a Wpf.Ui icon variant whose codepoint falls outside the Unicode range the Segoe
  Fluent Icons font actually covers, so nothing showed up regardless of theme or DPI. Swapped each
  for an equivalent icon with a codepoint the font supports.

## [1.2.0] - 2026-09-29

### Changed

- The mod scan at startup now runs on a background thread instead of blocking the main window's
  construction, so the app becomes visible and responsive almost immediately - the splash screen
  no longer sits frozen for the scan's duration on large mod collections; the main window's own
  "Checking..." placeholder covers the (typically brief) time until it finishes. Every other
  rescan (the Refresh button, undo, switching Mods folders, ...) is unchanged.

### Added

- A real app icon (`src/Sims4ModManager.App/Resources/app.ico`, 10 sizes from 16px to 256px): the
  exe, all windows, and the release installer now show it instead of a generic placeholder.
- A startup splash screen. Building the main window scans the whole Mods folder synchronously and
  can take a while on a large collection, during which the app previously showed nothing at all -
  easy to mistake for a failed launch. The splash appears immediately and stays visible (with a
  spinner and a crisp logo, sized to fit its text at any DPI) until the main window is ready.
- Favorite mods: a star toggle in the mod list and the details panel, plus a "Favorites" entry in
  the mod filter dropdown. Stored alongside notes/tags, so it survives rescans and renames.
- A "What's new" summary shown once, the first time the app runs after updating to a new version -
  pulled directly from this changelog, so it never goes stale.
- A keyboard shortcuts reference (F1, or the "?" button in the title bar).
- An update channel toggle (the rocket icon next to the version number): opt into pre-release
  (beta) app builds instead of only stable releases. Takes effect immediately, no restart needed.
- Portable settings backup (the sync icon in the title bar): export settings, mod notes/tags/
  favorites and profiles to a single file, and import them on another PC - useful when moving to
  a new machine. Deliberately excludes anything machine-specific (Mods folder, window placement,
  the CurseForge API key).
- Accent color customization (the paint brush icon in the title bar): eight color presets plus
  "Windows default", on top of the existing dark/light toggle. Applies immediately and is included
  in the portable settings backup above.
- A toast notification area (bottom-right) for background events that used to only appear as status
  bar text: a silent app-update check finding a new version, and a scheduled save/Tray backup
  running at startup. Dismissible, and disappears on its own after a few seconds.
- [docs/ROADMAP.md](docs/ROADMAP.md): a public list of planned improvements across mod management,
  performance, reliability, localization, accessibility and tooling.

## [1.1.0] - 2026-09-28

### Added

- Self-updating: the app checks GitHub Releases in the background on startup and, when a newer
  version is available, shows a title-bar button to download and install it in place (via
  [Velopack](https://velopack.io)) - no manual download or reinstall needed. Only applies to
  installations set up from a Velopack-built release (see [docs/Release.md](docs/Release.md)); a
  `dotnet run` development build never checks.
- A GitHub Actions workflow ([.github/workflows/release.yml](.github/workflows/release.yml)) that
  builds, packs and publishes a release automatically whenever a `v*` tag is pushed.

### Changed

- Overhauled repository documentation: a rewritten, badge-fronted `README.md` (now English, with a
  table of contents and organized feature sections), `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`,
  `SECURITY.md`, issue/PR templates, `CODEOWNERS`, an `.editorconfig`, and a CI workflow that runs
  build + test on every push and pull request. `docs/Release.md` is now in English to match.

## [1.0.1] - 2026-09-28

### Fixed

- English is now the app's default UI language. Previously, a fresh install (or any settings
  file without an explicit language) defaulted to German; only an explicit German selection
  now switches to German.
- Several places stayed in German even when English was selected, because they built their text
  without going through the translation system: the exported mod list (text and CSV), the
  "Downloads installieren" file picker filter, and a few internal error messages (corrupted
  package data, a missing backup point, a failed resolution verification). All now translate
  correctly.
- The app's version now appears in the title bar.

## [1.0.0] - 2026-09-28

### Added

Initial public release.

- **Detection**: automatic Mods-folder detection across Documents locations (incl. OneDrive),
  independent of game language or install location.
- **Mods**: enable/disable via `.disabled` renaming (reversible, no file moves), multi-file mods
  toggle as a unit.
- **Conflict detection**: DBPF resource-index and script-module comparison across active mods,
  grouped by conflict with severity levels; byte-identical duplicates are not flagged.
- **Conflict resolution**: guided proposals (duplicate/older version/HQ variant/merged
  set/real overwrite/script overlap) with a one-step "safe resolutions" action.
- **Profiles**: save and restore mod selections.
- **History & backup**: every file-changing action is journaled and undoable (`Ctrl+Z`), with
  automatic pre-change backups.
- **Library (Tray)**: households, lots and rooms with CC detection, export, cleanup and backup.
- **Overview / health check**: game version and update detection, game mod switches, cache
  status, and automatic problem detection with one-click safe fixes.
- **Diagnostics**: crash/exception log analysis with mod attribution, and a 50/50 bisection
  assistant.
- **Saves**: save browsing with used-CC detection and backup/restore.
- **Download safety**: new-download detection, dangerous script-mod pattern scanning, and
  missing-library detection (S4CL, XML Injector, Lot 51 Core).
- **Catalog**: thumbnail tiles with category/creator/age/gender filters.
- **Sort**: automatic sorting into collection folders by content and creator.
- **Storage**: size breakdown by category/creator, duplicate detection, lossless compression.
- **Game index**: default-replacement and tuning-override detection, meshless recolor detection,
  and library pack/CC requirements.
- **Package tools**: resource browsing, package optimization, and CC merging (Sims 4
  Studio-compatible manifest).
- **CurseForge updates**: fingerprint-based mod recognition and update installation.
- **Global search**, **light/dark theme**, **German/English UI**, and a **first-run setup
  assistant**.

[Unreleased]: https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/compare/v1.3.0...HEAD
[1.3.0]: https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/releases/tag/v1.0.0
