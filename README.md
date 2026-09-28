<div align="center">

# Sims 4 Mod Manager

**An all-in-one mod manager for The Sims 4** — enable/disable mods, detect and resolve resource
conflicts between packages, manage your Tray library, diagnose crashes, and keep everything backed
up and reversible.

[![CI](https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/actions/workflows/ci.yml/badge.svg)](https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM)](https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/releases/latest)
[![License: MIT](https://img.shields.io/github/license/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform: Windows](https://img.shields.io/badge/platform-Windows-0078D6)](#installation)

[Installation](#installation) ·
[Features](#features) ·
[Building from source](#building-from-source) ·
[Contributing](#contributing) ·
[Changelog](CHANGELOG.md)

</div>

---

## Table of contents

- [Installation](#installation)
- [Self-updating](#self-updating)
- [Features](#features)
  - [Detection & setup](#detection--setup)
  - [Managing mods](#managing-mods)
  - [Conflict detection & resolution](#conflict-detection--resolution)
  - [Library (Tray)](#library-tray)
  - [Game overview & health check](#game-overview--health-check)
  - [Diagnostics](#diagnostics)
  - [Saves](#saves)
  - [History & backups](#history--backups)
  - [Downloads & safety](#downloads--safety)
  - [Storage & package tools](#storage--package-tools)
  - [Mod updates (CurseForge)](#mod-updates-curseforge)
  - [Search, UI & persistence](#search-ui--persistence)
- [Project structure](#project-structure)
- [Building from source](#building-from-source)
- [Documentation](#documentation)
- [Contributing](#contributing)
- [License](#license)

## Installation

1. Download the latest installer (`Sims4ModManager-win-Setup.exe`) from the
   [Releases page](https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/releases/latest).
2. Run it. No admin rights or separate .NET install required.
3. On first launch, the setup assistant walks you through language, Mods-folder detection, and the
   game's mod switches.

Requires Windows. The app finds your Sims 4 `Mods` folder automatically (see
[Detection & setup](#detection--setup)) regardless of game language or install location.

## Self-updating

The app checks this repo's [Releases](https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/releases)
in the background on startup. When a newer version is available, a button appears in the title bar
to download and install it in place — no manual download or reinstall needed. This is built on
[Velopack](https://velopack.io); see [`docs/Release.md`](docs/Release.md) for how releases are
published.

## Features

### Detection & setup

- **Automatic Mods-folder detection** across every Documents location (including redirected or
  OneDrive folders, in any language). The Sims 4 data folder is recognized independent of game
  language ("The/Die/Les Sims 4" …, falling back to marker files like `Options.ini` or `saves`).
  With multiple installations, the one with a `Mods` folder and the most recent activity wins.
  Accidentally selecting the Sims 4 folder instead of `Mods` is corrected automatically. Mod files
  sitting next to `Mods` instead of inside it (which the game ignores) are flagged.
- **First-run setup assistant** (language, Mods folder incl. OneDrive warning, the game's mod
  switches, an initial backup, overview) — reachable again later via the wand icon.

### Managing mods

- Every loose file or top-level folder in the Mods folder counts as one mod. Multi-file mods (e.g.
  CC packs with several `.package` files) toggle as a single unit.
- **Enable/disable** reversibly by appending/removing the `.disabled` suffix — files are never
  moved, folder structure is preserved.
- **Catalog**: every mod file as a tile with its in-package thumbnail (transparency-aware), names
  from string tables, category (CAS hair/clothes/makeup …, objects, build items, gameplay,
  scripts, sliders, poses, retextures), creator, age and gender from CAS parts. Filterable by
  category/age/gender; results are cached under `cache\`.
- **Sort**: organizes mods with a preview into collection folders like `CAS - Hair\<Creator>`,
  `Objects`, or `Skript-Mods` (respecting the game's folder-depth limits; script-mod folders stay
  put). Collection folders carry a `.s4mm-sammlung` marker; every mod inside stays individually
  toggleable and keeps its identity. Your own folders can be marked "as a collection" too.
  Fully undoable via History.
- **Details & notes**: preview, category, creator and files; plus a note, "why installed?", tags,
  and download/creator links (`notes.json`, stored outside the Mods folder).

### Conflict detection & resolution

- Reads the DBPF index of every active `.package` file (type/group/instance, deleted entries
  ignored) and the Python modules of every `.ts4script` file, and reports resources or modules
  provided by more than one mod.
  - Byte-identical duplicates (even differently compressed) don't count as a conflict.
  - Conflicts are grouped by the set of mods involved, with resource type and severity
    (high: tuning/SimData/CAS parts/objects/scripts, medium: meshes/textures/strings,
    low: thumbnails).
  - Unreadable files are shown as a warning instead of being silently skipped.
- **Resolution proposals** for each conflict group, working at the file level and keeping as much
  as possible:
  - *Duplicate install* → disable the older copy
  - *Older version* (v1/v2, "Fixed", "Update", "(1)" …) → disable the older version
  - *HQ vs. non-HQ variant* → disable non-HQ (alternative: HQ)
  - *Already inside a merged set* (all its resources already exist in another package) → disable
    the standalone file
  - *Genuine overwrite* between different mods → remove only the contested resources from the
    losing mod's package (rewritten in place, the rest of the mod stays intact); the newer file
    wins by default, with the reverse offered as an alternative
  - *Script overlap* → disable the older `.ts4script`
  - "Safe resolutions" applies every pure duplicate-disable in one step.
  - Sims 4 Studio's merge manifest (type `7FB6AD8A`) is never flagged as a conflict.

### Library (Tray)

A dedicated tab for the `Tray` folder — your local library of households, lots and rooms.

- Shows name, creator, description, Sims, lot size, tags and date from `.trayitem` files
  (protobuf), grouping every file that belongs to one entry (including Sim portraits).
- Flags incomplete entries (missing `.trayitem`/`.householdbinary`/`.blueprint`/`.room`).
- **CC detection**: finds the mod files an entry's CAS parts, objects and build items depend on —
  including disabled ones, enabled with one click. The mod list shows the reverse: how many
  library entries use a given mod.
- **Export** an entry with its CC as a folder or ZIP (`Tray\` + `Mods\`).
- **Install downloads** (ZIP/RAR/7z, including nested archives, folders or loose files): Tray files
  go unchanged and flat into `Tray`, `.package`/`.ts4script` files into `Mods\<Download>\`. Files
  that already exist (anywhere in `Mods`) or would collide by name are detected and never
  overwritten.
- **Cleanup**: Tray files sitting in `Mods`, mods sitting in `Tray`, Tray subfolders, renamed Tray
  files, and un-extracted archives are flagged and can be moved on request.
- **Remove** moves entries to `%AppData%\Sims4ModManager\backups` (restorable via "install
  folder"); **backup** packs Tray + saves (optionally Mods) into a ZIP.
- Thumbnails (`.hhi`/`.sgi`/`.bpi`/`.rmi`) aren't rendered — their format is proprietary and
  undocumented.

### Game overview & health check

The "Overview" tab: game version, update detection, the game's own mod switches (`Options.ini`:
mods/CC, script mods, CC list at startup), cache status, and every detected problem with one-click
auto-fix ("fix all safe issues"). Checks include:

- Sims 2/3 packages (DBPF 1.x/2.0), corrupted and empty files, interrupted downloads
- `.ts4script` nested deeper than 1 folder, `.package` deeper than 5, paths over 259 characters
- Readmes/images left in the Mods folder, empty folders, manually disabled mods (`.packageOFF` …),
  missing or incomplete `Resource.cfg`, special characters
- OneDrive sync and mods that only exist in the cloud
- Stale game cache; after game updates, script/gameplay mods at risk ("safe mode" disables them
  temporarily)

Sorted-out files move to `Mods (sorted out)` next to the Mods folder — never deleted. Also: export
or copy the mod list as text (for Discord/forums) or CSV. While the game is running, the app warns
before any file-changing action.

**Play**: the "Play" button (top right and on the overview) optionally applies a profile, clears
the cache, optionally backs up saves, and launches `TS4_x64.exe` (install location found via
registry/Steam). After the game exits, everything is rescanned and new error logs are flagged.

### Diagnostics

Reads `lastException*`, `lastUIException*`, `lastCrash*`, and MC Command Center / Better Exceptions
reports, groups identical errors, and identifies the mod responsible (`.ts4script` path in the
traceback, or the Python module name) with a button to disable it. Errors from older game versions
are flagged and can be cleaned up.

The **50/50 assistant** splits active mods in half repeatedly until the culprit behind a problem is
found; progress survives an app restart (`bisect.json`), and every toggle goes through History.

### Saves

Shows every save with its households, Sims, lots, game version, and the CC it uses (including
disabled CC). Disabling a mod a save depends on prompts a confirmation first. Saves can be backed
up and restored individually (`backups\saves`).

### History & backups

Every file-changing action (enable/disable, profiles, installs, cleanup, removal, conflict
resolutions) is journaled to `%AppData%\Sims4ModManager\backups\journal`; overwritten or removed
files are copied first. The "History" tab (or `Ctrl+Z`) undoes any action — files changed again
since then are never overwritten. The last 100 actions are kept.

**Automatic backups** run before every game launch and/or daily/weekly at app start; only the
newest automatic backups per slot are kept. The library is mirrored incrementally (`backups\tray`,
older snapshots kept 30 days).

### Downloads & safety

New Sims downloads in your Downloads folder are detected and offered for install via a banner
(can be turned off). Script mods are scanned for dangerous patterns (embedded `.exe`, process
launches, obfuscated code) — including before install. Missing or disabled required libraries
(S4CL, XML Injector, Lot 51 Core) are flagged.

### Storage & package tools

- **Storage**: size by category and creator, largest mods, byte-identical duplicates (extra copies
  removable), and uncompressed data ("compress …" losslessly compresses it).
- **Game index**: locates the install (registry, EA App, Steam, or manual) and reads the resource
  indices of every game package (base game, delta patches, all packs; string tables excluded) —
  cached in `cache\gameindex.bin`, rebuilt after updates or new packs. This enables:
  - **Default replacements & tuning overrides**: mods that replace game resources (flagged and
    filterable in the mod list). Game tuning lives in a binary blob, so tuning overrides are
    detected via SimData keys or EA-typical ID/name patterns (a heuristic). Overrides older than
    the last game update are flagged on the overview.
  - **Meshless recolors**: CAS parts (GEOM references) and objects (the OBJD's model reference)
    whose mesh exists in neither a mod nor the game — or only in a disabled mod.
  - **Library**: required packs and missing CC per household (reference fields are learned from
    the data). Lots and rooms are stored compressed by the game, so only "at least" figures are
    available there.
- **Package tools** (Mods tab → "Content"): resources by type, "optimize" (remove duplicate
  entries, losslessly recompress), "unmerge" for merged packages. "Merge …" combines CC mods into
  one package; the list of original files is stored in Sims 4 Studio's format (`0x7FB6AD8A`), so
  either program can unmerge such packages.

### Mod updates (CurseForge)

Recognizes CurseForge mods by file fingerprint (MurmurHash2, same as CurseForge; cached in
`cache\fingerprints.json`), shows newer releases, and installs them through History (undoable).
Requires your own free API key (from console.curseforge.com), stored encrypted in settings via
Windows DPAPI. *(This is separate from the app's own [self-update](#self-updating) mechanism.)*

### Search, UI & persistence

- **"What changed?"** (History tab): compares against the daily snapshot or the state before the
  last game launch — new, removed, changed, (de)activated and moved files.
- **Global search** (`Ctrl+K`): mods (name, creator, category, notes, tags, in-game names), library
  and saves.
- **Language**: German or English (EN/DE button in the title bar, restarts to apply). German is the
  source language; translations live in
  [`src/Sims4ModManager.Core/Localization/en.json`](src/Sims4ModManager.Core/Localization/en.json).
- **UI**: Fluent design (WPF-UI) with a dark theme (toggle to light via the title-bar icon,
  remembered), icons and tooltips throughout, search/filters in the mod list (e.g. "with
  conflicts", "used in library"), a warning icon on conflicted mods, dimmed disabled mods.
  Shortcuts: `F5` refresh, `Ctrl+F` search, `Enter` in the profile name saves it, double-click a
  library entry to reveal it in Explorer.
- **Persistence**: settings (`%AppData%\Sims4ModManager\settings.json`: Mods folder, recent
  folders, last profile, window position) and profiles are written atomically; the previous
  version is kept as `.bak` and used automatically if a file gets corrupted.

## Project structure

| Path | Contents |
|---|---|
| `src/Sims4ModManager.Core` | Core logic, no UI dependency: Mods/Tray folder scanning, DBPF parsing (`.package` files), enable/disable, conflict detection, profiles. |
| `src/Sims4ModManager.App` | WPF UI (.NET 8, MVVM via CommunityToolkit.Mvvm, Fluent design via WPF-UI). |
| `tests/Sims4ModManager.Core.Tests` | xUnit tests for the core logic. |
| `docs/` | Design notes and the release process. |
| `.github/workflows/` | CI (build + test) and the release pipeline. |

## Building from source

```bash
dotnet build
dotnet run --project src/Sims4ModManager.App
```

If the window stays blank (broken/virtualized graphics drivers), run without GPU acceleration by
setting the `S4MM_SOFTWARE_RENDERING=1` environment variable. Unexpected errors are logged to
`%AppData%\Sims4ModManager\error.log`.

### Tests

```bash
dotnet test
```

## Documentation

- [CHANGELOG.md](CHANGELOG.md) — what changed in each version
- [CONTRIBUTING.md](CONTRIBUTING.md) — dev setup, coding conventions, commit style
- [docs/Release.md](docs/Release.md) — how a release is versioned, tagged and published
- [SECURITY.md](SECURITY.md) — how to report a vulnerability
- [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)

## Contributing

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) for dev setup, project
structure, coding conventions, and how commits/PRs are expected to look. Please also read the
[Code of Conduct](CODE_OF_CONDUCT.md).

## License

[MIT](LICENSE)
