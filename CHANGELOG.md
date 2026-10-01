# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Notification history: a bell icon in the title bar (next to the keyboard-shortcuts button) opens a
  popup listing the last 30 toast notifications with their timestamp, so one that auto-dismissed while
  you were away isn't lost for good. A red dot badges the icon when a notification has arrived since
  the popup was last opened. Closes the "toast history" item from
  [docs/ROADMAP.md](docs/ROADMAP.md#app-level-features).
- Bulk tagging: the Mods tab's mod grid now supports multi-select (Ctrl/Shift-click), and selecting
  more than one row shows a small bar above the grid to add or remove one tag across the whole
  selection in one step, instead of opening each mod's Details panel individually. Closes the "bulk
  tagging" item from [docs/ROADMAP.md](docs/ROADMAP.md#mod-management).
- Drag-and-drop install: dropping a downloaded archive (`.zip`/`.rar`/`.7z`), a loose `.package`/
  `.ts4script`/tray file, or a folder anywhere onto the window now runs it through the same install
  pipeline (safety scan, overview/confirm dialog) as a watched Downloads file or "Install downloads
  …", instead of requiring the file picker. A drop overlay shows while dragging a recognized file
  over the window and is silently inert for anything else (photos, documents, ...). Closes the
  "drag-and-drop install" item from [docs/ROADMAP.md](docs/ROADMAP.md#mod-management).
- "Always enable together" mod grouping: a mod's Details panel has a new "Group" field (next to
  Tags), and the multi-select bulk bar can set or clear a group across the current selection.
  Enabling or disabling any mod that's part of a group now cascades to every other mod sharing that
  group name, as one journaled/undoable action - e.g. a hair mod and its matching accessory always
  toggle together without hunting them down individually. The "used in a save" safety prompt before
  disabling covers every mod the cascade would actually disable, not just the one directly clicked.
  A small link-icon badge in the mod list marks which mods currently belong to a group. Closes the
  "always enable together" item from [docs/ROADMAP.md](docs/ROADMAP.md#mod-management).
- Bulk "why is this here" report: a new "Export notes …" button next to "Export list …" (Overview
  tab, "Mods" card) writes every mod's own note, tags, "why installed" reason, group and links to a
  text or CSV document - not just the file list the existing mod-list export gives - for people who
  maintain a curated pack across machines and want their own documentation to travel with it. Mods
  with nothing recorded are skipped; an empty result says so instead of writing an empty file.
  Closes the "bulk 'why is this here' report" item from
  [docs/ROADMAP.md](docs/ROADMAP.md#mod-management).
- Rename-safe move: a new "Move …" button on the Mods tab's Details panel relocates a mod between
  collection subfolders (or straight to the Mods root) from inside the app instead of dragging files
  in Explorer - pick an existing collection or type a new one. A mod's ID is already just its file/
  folder name, not its path, so notes, tags and profile membership keep pointing at the same mod
  automatically; nothing needs updating. Refuses a move that would bury a mod deeper than the game
  actually reads (scripts: 1 folder, packages: 5) or collide with an existing file/folder of the
  same name, and the move is journaled like every other change, so it shows up in History and can be
  undone. Closes the "rename-safe move" item from
  [docs/ROADMAP.md](docs/ROADMAP.md#mod-management).
- Status indicators in the mod list: two new per-row icons next to the existing conflict warning -
  a "broken" icon for a mod with a file that failed to parse (corrupted, locked, or not a real
  Sims 4 package/archive) and an "update available" icon sourced from the Updates tab's last
  CurseForge check. Both reuse the reserved-slot (`Visibility="Hidden"`, not `Collapsed`) pattern
  the conflict icon already used, so mod names stay aligned to the same column regardless of how
  many indicators a given row shows. Closes the "status indicators in the mod list" item from
  [docs/ROADMAP.md](docs/ROADMAP.md#mod-management).
- Automatic background CurseForge update check: the Updates tab's fingerprint check now also runs
  once per app startup, the same way the silent app-update check already does - skipped silently
  without a CurseForge API key, and paced to at most once every 12 hours so it doesn't hammer the
  API on every launch. It waits for the startup mod scan to finish first, and shows a toast
  ("N mod update(s) available on CurseForge.") when it finds any, instead of only ever reflecting
  whatever the last manual "Check now" click happened to see. Closes the "automatic background
  CurseForge update check" item from [docs/ROADMAP.md](docs/ROADMAP.md#mod-management).
- Conflict report export: a new "Bericht exportieren …" button in the Conflicts tab's header
  writes every conflict group - its affected mods, resources/script modules, severity, and the
  resolver's recommended fix - to a text or CSV document, for asking the community for help with a
  conflict the in-app resolver doesn't safely auto-fix. Mirrors the existing mod-list/notes export
  buttons. Closes the "export a conflict report" item from
  [docs/ROADMAP.md](docs/ROADMAP.md#conflict-resolution).
- Live-while-playing installs: a new "Downloads auch bei laufendem Spiel installieren" toggle
  (Overview tab, off by default) skips the "Sims 4 läuft" confirmation specifically for installing
  downloads - "Install downloads …", drag-and-drop, "install downloaded folder", the issue-driven
  "install archive" fix, and the pending-download banner all funnel through the same install path,
  which only ever adds brand-new files and never overwrites an existing one, so it can't touch
  anything the game may already have loaded. Every other mutating operation (toggling, moving,
  merging, resolving conflicts) keeps the confirmation unconditionally. Closes the "live-while-
  playing mode" item from [docs/ROADMAP.md](docs/ROADMAP.md#mod-management).
- Explicit load-order control: a new "Reihenfolge" column in the Mods tab shows each mod's true
  effective load-order position - the game scans directories depth-first, merging files and
  subfolders alphabetically at each level, not "all files first" or "all folders first" as often
  repeated. Dragging a row onto another renumbers them using the community's numeric-prefix
  convention (`000_`, `010_`, ...), preferring to rename only the dragged file/folder and falling
  back to renumbering every sibling only when there's no clean numeric gap available; restricted to
  siblings sharing the same collection folder (cross-folder drags point to the existing
  "Verschieben …" action instead). Notes/tags/favorites automatically follow a renamed mod; saved
  profiles referencing the old name do not, and the confirmation dialog says so. Conflict proposals
  for Override and script-overlap conflicts (the two kinds without a "safe" auto-fix) now flag when
  the game's real load order would actually pick a different winner than the proposal's file-date-
  based recommendation. Closes the "explicit load-order control" item from
  [docs/ROADMAP.md](docs/ROADMAP.md#load-order--collections).
- Texture downscaling: a new "Überdimensionierte Texturen" card on the Storage tab flags CAS/object
  DDS textures above 2048×2048 pixels, with a "Verkleinern …" button to downscale them in one
  journaled/undoable step - many CC creators ship 4K textures the game never resolves at typical
  camera distances, and they cost disk space and per-file scan time for no visual gain. Adds the
  BCnEncoder.Net package to decode the base mip, box-filter it down, regenerate a full mip chain and
  re-encode with the same block-compression format the original used; a resource whose compression
  isn't one BCnEncoder.Net recognizes as well-formed is left untouched rather than guessed at, since
  this codebase had no texture-codec groundwork to build on before this change. Partially closes the
  "deeper mod optimization tooling" item from
  [docs/ROADMAP.md](docs/ROADMAP.md#mod-management) (texture downscaling only - debloating and
  script merging remain open).
- Thumbnail debloating: a new "Überdimensionierte Vorschaubilder" card on the Storage tab flags
  oversized CAS/Build-Buy catalog preview thumbnails above 256×256 pixels, with a "Verkleinern …"
  button to downscale them in one journaled/undoable step. Deleting them outright (Sims 4 Studio's
  documented "Delete CC thumbnails" batch fix) was ruled out during scoping, since this app's own
  Catalog tab reads the same resource for its own previews and would lose them too; downscaling
  keeps both the in-game catalog icon and this app's preview working. Thumbnails carrying the
  undocumented "ALFA" transparency segment (hair/clothes cutouts) are left untouched, since this
  codebase can only decode that scheme, not safely rebuild it. Adds the BitMiracle.LibJpeg.NET
  package for the JPEG decode/resize/re-encode (chosen over a newer ImageSharp version after
  finding the license-compatible pre-split-license release carries several unpatched high-severity
  CVEs). Closes the "debloating unused resources" item from
  [docs/ROADMAP.md](docs/ROADMAP.md#mod-management) (texture downscaling already closed; script
  merging remains open).

### Fixed

- Mods tab: when no mod was selected, the Details panel's "Select a mod..." placeholder text
  overlapped illegibly with the (still-visible, disabled) "Show in Explorer"/"Make collection"
  buttons and empty Notes fields underneath it, since only the placeholder text's own visibility
  was toggled, not the details content around it. Found while driving the Library (Tray) screen
  for the ongoing UI-consistency pass below. The details `ScrollViewer` now collapses itself
  whenever no mod is selected, leaving only the placeholder text visible.
- Conflicts sub-tab: the "safe"/"recommended" solution badges and the "CAS part"/"CAS thumbnail"
  resource-type labels showed their raw German source strings ("sicher", "empfohlen", "CAS-Teil",
  "CAS-Vorschaubild") in the English build - copy-paste omissions where these four literals weren't
  wrapped in `L.T(...)` like every neighboring entry in the same dictionaries. Found while
  screenshotting the Conflicts/Solutions/Resources sub-tabs for the same pass. Fixed in
  [ResolutionProposalViewModel.cs](src/Sims4ModManager.App/ViewModels/ResolutionProposalViewModel.cs)
  and [ResourceTypeCatalog.cs](src/Sims4ModManager.Core/ResourceTypeCatalog.cs), with matching
  translations added to [en.json](src/Sims4ModManager.Core/Localization/en.json).

### Changed

- Reworked [docs/ROADMAP.md](docs/ROADMAP.md): substantially expanded with new areas (community
  mod-tracking integration via TS4 Mod Hound, load order & shareable collections, game-performance/
  optimization tooling distinct from the app's own performance, workflow automation, fun/
  personalization tools, platform & distribution), grounded in research into the wider Sims 4
  modding ecosystem - GameTimeDev's S4MM, PlumbBuddy, the official CurseForge manager, Sims 4
  Ultimate Mod Manager, Sims 4 Studio, TS4 Mod Hound, and the March 2026 ModTheSims malware
  incident. Every already-shipped (✅) entry was removed now that it's recorded here instead, and
  the remaining sections were reordered so related areas (mod management → load order → conflict
  resolution; then community data & safety; then app/game performance; then workflow and UX) read
  as a more coherent sequence rather than accumulated in research order.
- Converted [docs/ROADMAP.md](docs/ROADMAP.md) into an actual checkable task list (GitHub-flavored
  Markdown `- [ ]` boxes) instead of plain bullets, so it can be worked and ticked off directly
  rather than only read as prose. The in-progress UI-consistency item was broken out into a nested
  per-screen checklist (four screens already checked off with their CHANGELOG reference, the rest
  still open) instead of one paragraph of prose. `README.md` and `docs/README.md` now describe it
  as a checkable development TODO rather than a plain idea list.
- Closed the "deeper mod optimization tooling" [docs/ROADMAP.md](docs/ROADMAP.md#mod-management)
  item: texture downscaling and thumbnail debloating shipped (see above); script merging was
  researched and deliberately declined rather than built - community consensus is that merging
  `.ts4script` files breaks mods roughly 9 times out of 10 (module/namespace collisions) and works
  against per-mod updates after game patches, which this app's whole toggle/undo model depends on.
  No code changed for that sub-item.
- Closed "known community batch-fixes for broken CC" [docs/ROADMAP.md](docs/ROADMAP.md#mod-management):
  researched and deliberately declined, not implemented. The flagship example (arms stuck to sides)
  needs mesh (GEOM) rigging edits, not a simple flag flip, and the simpler-looking CASP flag fixes'
  only precise byte-level spec lives in GPLv3-licensed code this MIT-licensed project shouldn't port
  from - mirroring the same caution `CasPartReader.cs` already applies to that resource's drift-prone
  flag block. No code changed.

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
