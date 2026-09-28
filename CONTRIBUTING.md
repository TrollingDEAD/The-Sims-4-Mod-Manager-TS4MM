# Contributing

Thanks for taking the time to contribute. This document covers everything you need to get set
up, make a change, and get it merged.

## Table of contents

- [Prerequisites](#prerequisites)
- [Getting started](#getting-started)
- [Project structure](#project-structure)
- [Making a change](#making-a-change)
- [Coding conventions](#coding-conventions)
- [Localization](#localization)
- [Commit messages](#commit-messages)
- [Branches and pull requests](#branches-and-pull-requests)
- [Reporting bugs and requesting features](#reporting-bugs-and-requesting-features)
- [Releasing](#releasing)

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows (the app targets `net8.0-windows` and uses WPF; it cannot be built or run on
  Linux/macOS)

## Getting started

```bash
git clone https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM.git
cd The-Sims-4-Mod-Manager-TS4MM

dotnet build
dotnet test
dotnet run --project src/Sims4ModManager.App
```

If the app window is blank on startup (broken/virtualized graphics drivers), set
`S4MM_SOFTWARE_RENDERING=1` before running it.

## Project structure

| Path | Contents |
|---|---|
| `src/Sims4ModManager.Core` | Core logic, no UI dependency: mod/tray scanning, DBPF package parsing, conflict detection & resolution, profiles, backups, localization. |
| `src/Sims4ModManager.App` | WPF UI (.NET 8, MVVM via CommunityToolkit.Mvvm, Fluent design via WPF-UI). One view model per tab/window. |
| `tests/Sims4ModManager.Core.Tests` | xUnit tests for `Core`. The UI project has no automated tests; verify UI changes by running the app. |
| `docs/` | Design notes and the release process. |
| `.github/workflows/` | CI (build + test on every push/PR) and the release pipeline (triggered by version tags). |

Read [`README.md`](README.md) for a full feature overview before diving into a specific area.

## Making a change

1. Fork the repo and create a branch off `main` (see [branch naming](#branches-and-pull-requests)).
2. Make your change. Keep it focused - unrelated cleanup makes a PR harder to review.
3. Add or update tests in `Core.Tests` for any logic change.
4. Run `dotnet build` and `dotnet test` locally; both must pass.
5. If your change is user-visible, add an entry under `## [Unreleased]` in
   [`CHANGELOG.md`](CHANGELOG.md) ([Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format).
6. Open a pull request against `main`. CI runs the same build + test automatically.

## Coding conventions

The codebase is consistent about a few things; please match them rather than introducing a new
style:

- **File-scoped namespaces** (`namespace Foo.Bar;`), 4-space indentation, Allman braces - see
  [`.editorconfig`](.editorconfig) for the formal rules (most editors pick this up automatically).
- **Nullable reference types are enabled** project-wide. Don't suppress warnings with `!` unless
  the non-null invariant is actually guaranteed.
- **No comments that restate the code.** A comment is only worth adding when it explains a
  non-obvious *why* (a workaround, a subtle invariant, a constraint from the game's file formats).
- **No speculative abstractions.** Don't add configuration, interfaces, or extensibility hooks for
  a use case that doesn't exist yet.
- **MVVM, one view model per concern.** UI logic belongs in a view model
  (`src/Sims4ModManager.App/ViewModels`), not in code-behind; file/format logic belongs in `Core`,
  not in a view model. Look at an existing view model (e.g. `StorageViewModel.cs`) for the
  established shape before adding a new one.
- **Every action that changes files on disk goes through the change journal**
  (`Sims4ModManager.Core.Backup.ChangeJournal`), so it shows up in the "History" tab and can be
  undone. Don't write, move or delete a mod/tray/save file directly from a view model or command.
- **User-visible text is German, wrapped in `L.T("…")` or `L.F("…", args)`**
  (`Sims4ModManager.Core.Localization.L`) - see [Localization](#localization) below.

## Localization

German is the source language: UI strings are written in German directly in the code
(`L.T("Mods aktualisieren")`), and English is a translation dictionary keyed by that exact German
string (`src/Sims4ModManager.Core/Localization/en.json`). A few rules that keep this working:

- Every string a user can see must go through `L.T(...)` (fixed text) or `L.F("...", args)`
  (format strings with placeholders) - including file-dialog filters, exported reports, and
  exception messages that might reach the UI. A raw string literal bypasses translation entirely.
- When you add or change a German string that's wrapped in `L.T`/`L.F`, add the matching English
  entry to `en.json` with the *exact same* German text as the key. `en.json` is sorted by key
  (ordinal); re-sort after editing, or let a review comment catch it.
- Placeholders (`{0}`, `{1}`, …) must match between the German key and the English value -
  `LocalizationTests.EnglishDictionaryKeepsPlaceholders` enforces this.
- Static, literal text written directly in XAML (`Content="…"`, `Text="…"`, `ToolTip="…"`, …) is
  translated at runtime by `UiTranslator`, which does an exact lookup in the same `en.json` - the
  XAML text must match a German key there too.
- File/folder names the app creates on disk (e.g. the `Skript-Mods` sort folder, `Mods
  (aussortiert)`) are intentionally **not** translated - they're stable across languages so a
  user's folder layout doesn't change when they switch the UI language.

## Commit messages

This project uses [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>: <short summary, imperative mood>

<optional body: what changed and why - not a restatement of the diff>
```

Common types: `feat` (new capability), `fix` (bug fix), `docs`, `refactor`, `test`, `chore`
(tooling/CI/deps), `perf`. Example:

```
fix: translate the mod-list export instead of hardcoding German

The exported text and CSV report built every label as a raw German
literal, so it stayed German even with English selected.
```

Keep the summary line under ~72 characters and in the imperative ("add", not "added"/"adds").

## Branches and pull requests

- Branch names: `feat/<short-description>`, `fix/<short-description>`,
  `docs/<short-description>`, etc., matching the commit type.
- Keep PRs focused on one change; split unrelated changes into separate PRs.
- Describe *what* changed and *why* in the PR description - link any related issue.
- CI (build + test) must pass before merge.

## Reporting bugs and requesting features

Please use the issue templates (bug report / feature request) shown when you open a new issue on
GitHub - they ask for the information needed to act on the report (repro steps, log excerpt,
version). For a security vulnerability, see [`SECURITY.md`](SECURITY.md) instead of a public
issue.

## Releasing

Cutting a release (version bump, tagging, the automated build/pack/publish pipeline) is documented
in [`docs/Release.md`](docs/Release.md). That's a maintainer task, not something a contributor
PR needs to touch.
