# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- A real app icon (`src/Sims4ModManager.App/Resources/app.ico`, 10 sizes from 16px to 256px): the
  exe, all windows, and the release installer now show it instead of a generic placeholder.
- A startup splash screen. Building the main window scans the whole Mods folder synchronously and
  can take a while on a large collection, during which the app previously showed nothing at all -
  easy to mistake for a failed launch. The splash appears immediately and stays visible (with an
  animated progress ring) until the main window is ready.

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

[Unreleased]: https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/releases/tag/v1.0.0
