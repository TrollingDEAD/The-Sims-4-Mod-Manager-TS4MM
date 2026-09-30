# Roadmap / development TODO

A working, checkable backlog for where TS4MM could go next, grouped by area. Nothing here is a
promise or a schedule. Check a box off when its change actually ships (with a matching entry in
[CHANGELOG.md](../CHANGELOG.md)) - checked items are pruned from this file on the next cleanup pass
so it stays a live list of open work, not an archive. Suggestions and votes are welcome via
[issues](https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/issues).

## Known issues

- [x] General UI consistency pass: audit borders, padding, and control sizing (dropdowns, buttons,
  placement) across all screens and window sizes via a systematic screenshot review, to catch
  design inconsistencies that have accumulated across features. (Every `SymbolIcon` usage was
  already swept for a missing-glyph bug found earlier - no further icons need checking there, just
  new ones added later.) Driven live and screenshotted, screen by screen - every screen below
  checked out clean or had its finding fixed (see CHANGELOG.md for the Mods-tab overlap and the
  four untranslated-German-literal fixes found along the way):
  - [x] Overview (already fixed the "Affected: N" dropdowns being inconsistently sized per card -
    see [CHANGELOG.md](../CHANGELOG.md))
  - [x] Mods
  - [x] Catalog
  - [x] Diagnose
  - [x] Library (Tray) - checked borders/padding/control sizing and the empty/selected detail-panel
    states, live and at multiple window widths down to the enforced 1040px minimum; found and fixed
    an overlapping-text bug in the Mods tab's details panel along the way (see
    [CHANGELOG.md](../CHANGELOG.md))
  - [x] Saves - checked the save-slot list, selected-slot detail panel (households/used-CC table),
    and the "Automatic backup"/"Backups" expanders, live and at both the minimum (1040px) and
    maximized window widths; no inconsistencies found.
  - [x] Storage - checked the stat tiles, category/creator bar charts, and largest-mods/duplicate-
    files panels, live and at both the minimum and maximized window widths; bar proportions check
    out and text truncates/wraps consistently with the rest of the app, no inconsistencies found.
  - [x] History - checked both sub-tabs ("Actions" undo list incl. a strikethrough/undone entry,
    and "What has changed?" daily-state diff), live and at the minimum window width; no
    inconsistencies found.
  - [x] Game - checked the installed-packs expander, default-replacement table, and recolors-without-
    mesh panel, live and at both window widths. Noted EP19/EP20/EP21 show as bare codes with no
    name in the packs list, but confirmed in `GamePacks.cs` this is documented, intentional
    fallback for packs newer than the app's name table, not a bug.
  - [x] Updates - checked both CurseForge sub-tabs ("Updates" table, "Browse" catalog-style tile
    grid), live and at maximized width; no inconsistencies found.
  - [x] Conflicts/Solutions/Resources sub-tabs - found and fixed four untranslated German literals
    ("sicher", "empfohlen", "CAS-Teil", "CAS-Vorschaubild" - see CHANGELOG.md), confirmed clean
    afterward across Solutions, Resources and the Warnings expander.
  - [x] Setup/Merge/Sort dialog windows - all three checked live (Setup's 5-step wizard, Merge's
    file-selection grid, Sort's category-folder preview); consistent card/button styling
    throughout, no overlaps or cut-off content.

## Mod management

- [x] Bulk tagging: apply/remove a tag across the current selection or filtered view, instead of one
  mod at a time. The Mods tab's grid now supports multi-select (Ctrl/Shift-click); selecting more
  than one row shows a bar above the grid to add or remove a tag across the whole selection at once
  (see [CHANGELOG.md](../CHANGELOG.md)).
- [x] Drag-and-drop install: drop a downloaded `.zip`/`.7z` mod archive onto the window and have it
  extracted into the Mods folder (with the same download-safety scan already applied to watched
  Downloads). Dropping an archive, a loose `.package`/`.ts4script`/tray file, or a folder anywhere
  onto the window now runs it through the same install pipeline as "Install downloads …" (see
  [CHANGELOG.md](../CHANGELOG.md)).
- [x] Load-order-style "always enable together" grouping for mods that depend on each other, beyond
  what folder-based collections already give. A mod's Details panel (and the multi-select bulk bar)
  can now set a named group; toggling any mod in a group cascades to every other member as one
  journaled, undoable action, with the "used in saves" safety prompt covering the whole cascade (see
  [CHANGELOG.md](../CHANGELOG.md)).
- [x] Mod dependency hints: detect known requirement patterns (e.g. a CAS mod referencing a mesh
  pack) and surface them the way missing-library detection already does for S4CL/XML Injector.
  Turns out already covered, just never checked off here: `GameContentAnalyzer.FindMissingMeshes`
  (a recolor whose mesh neither the game nor an enabled mod provides) already flows into a
  `HealthIssue` (`GameViewModel.BuildHealthIssues`, id `missing-mesh`) merged into the Overview
  tab's "Probleme" list alongside the S4CL/XML Injector checks (`HealthViewModel.ShowIssues`) - same
  severity styling, same affected-file "Betroffen" list. No code changed for this entry.
- [x] Bulk "why is this here" report: export not just the mod list but the full note/tag/reason set in
  one document, for people who maintain a curated pack across machines. A new "Export notes …"
  button next to "Export list …" (Overview tab) writes every mod's note, tags, "why installed"
  reason, group and links to a text or CSV document, skipping mods with nothing recorded (see
  [CHANGELOG.md](../CHANGELOG.md)).
- [x] Rename-safe move: relocate a mod between collection subfolders from inside the app instead of
  Explorer, updating notes/tags/profile references in one step. A new "Move …" button on the Details
  panel opens a picker (known collections, a typed new one, or straight to the Mods root); since a
  mod's ID is already just its file/folder name (not its path), notes/tags/profile membership need
  nothing updated - they keep pointing at the same mod automatically. Refuses moves that would bury
  a mod deeper than the game reads or collide with an existing name, and the move is journaled like
  every other change (see [CHANGELOG.md](../CHANGELOG.md)).
- [x] Status indicators in the mod list: small icons per row for update-available, broken, conflicting,
  and other warnings, with mod names still aligned consistently across rows so the list stays
  clean regardless of how many indicators a given mod has. Added a "broken" icon (a file that
  failed to parse - corrupted, locked, or not a real package/archive) and an "update available" one
  (from the Updates tab's last CurseForge check), both reusing the existing conflict icon's
  reserved-slot (`Visibility="Hidden"`, not `Collapsed`) pattern so the name column never shifts
  (see [CHANGELOG.md](../CHANGELOG.md)).
- [x] Automatic background CurseForge update check (e.g. once on startup, like the silent app-update
  check already does): today `UpdatesViewModel.CheckAsync` only runs from the "Check now" button,
  so the Updates tab's notification badge only ever reflects the last manual check. Needs an
  API-key precondition (skip silently without one) and rate-limit-friendly pacing since it's a
  real network call, unlike the other two tabs' badges which piggyback on the existing rescan.
  `UpdatesViewModel.CheckIfDueAsync` now runs once per startup (after the mod scan finishes, so it
  isn't wasted on an empty list), skips silently without an API key, and is paced to at most once
  every 12 hours via the already-persisted `LastUpdateCheckUtc`; a toast reports the count when
  updates are found. Verified live against a real account/library: found 17 real updates on first
  run, correctly updated the CurseForge tab badge and the new per-row update-available icon, and
  persisted the check timestamp (see [CHANGELOG.md](../CHANGELOG.md)).
- [ ] Deeper mod optimization tooling: go beyond the current diagnostics to automatically detect and
  fix more problem classes, plus performance-focused operations for large mod lists - texture
  scaling/downscaling, debloating unused resources, and script merging.
- [ ] Known community batch-fixes for broken CC, applied from inside the app: Sims 4 Studio ships
  curated "Batch Fix" routines (Tools > Content Management > Batch Fixes) for recurring, well-
  understood breakage - e.g. CAS items with arms stuck to sides after a game update - backing up
  every file it touches first. A small, versioned library of the same well-known fix patterns
  (sourced from S4S's public documentation of what each fix does, not its code) applied through the
  existing package-rewrite pipeline and journaled like every other change would close a concrete gap
  against a tool many players currently alt-tab to specifically for this.
- [ ] Live-while-playing mode: PlumbBuddy can integrate newly added mods while the game keeps running,
  where the existing "warn and block file changes while the game is running" safety net is more
  conservative. Worth a narrow, explicitly-opt-in exception for operations that are safe mid-session
  (adding a brand-new file, e.g. from a background CurseForge install) while keeping the existing
  block for anything that touches a file the game may have already loaded (toggling, moving, or
  resolving a conflict on an existing mod).

## Load order & collections

- [ ] Explicit load-order control: the game loads mods alphabetically by path, so overrides between two
  script mods or two tuning packages are decided by filename today - something the community works
  around by hand with numeric folder prefixes (`000_`, `010_`, ...). Surface the effective load
  order in the mod list, let a user drag to reorder (writing the numeric-prefix convention under the
  hood so it keeps working without the app running), and flag load-order-sensitive conflicts
  (two mods editing the same tuning where order decides the winner) separately from resource
  conflicts, where disabling one side is the only fix.
- [ ] Shareable mod collections ("modpacks"): package a curated set of mods - by reference (CurseForge
  IDs + versions) rather than the files themselves - into one file a creator or Discord/forum
  community can publish; installing one resolves and downloads every mod (and its dependencies) via
  the existing CurseForge browse/install pipeline and reports anything only available elsewhere.
  Mirrors what Nexus/Vortex "Collections" and Mod Organizer 2's profile exports do for other games,
  and complements the existing portable settings backup (which intentionally excludes the Mods
  folder itself).
- [ ] "Recreate on a new PC" wizard: given a saved mod list (already exportable as text/CSV) plus the
  CurseForge fingerprint/ID data the app already caches, offer to re-download and reinstall
  everything it recognizes in one pass after a fresh game install, instead of only exporting a list
  for manual reference.
- [ ] A disposable "test profile": stage an alternate Mods-folder state (new mod, a reorder, a
  resolution) without touching the real one, run/verify it, then either commit or discard -
  approximating the isolation a virtual-file-system mod manager (Mod Organizer 2, Vortex) gives
  other games, without needing to intercept the game's actual file reads.

## Conflict resolution

- [ ] Conflict resolution history: remember which proposal was applied for a given conflict group so
  re-running the resolver after adding mods doesn't ask about already-decided conflicts again.
- [ ] Severity-weighted "safe resolutions" preview before applying, showing exactly which files change.
- [x] Export a conflict report (already possible for the mod list) as a shareable document for asking
  for help in the community. A new "Bericht exportieren …" button in the Conflicts tab's header
  writes every conflict group (affected mods, resources/script modules, severity, and the
  resolver's recommended fix) to a text or CSV document - what "Export list …" already does for
  the mod list, but for conflicts (see [CHANGELOG.md](../CHANGELOG.md)).
- [ ] Persistent "ignore this conflict" dismissal for overlaps a user has reviewed and accepted (e.g. an
  intentional override), so it stops resurfacing in the count on every rescan without disabling
  either file.

## Community mod-tracking integration

The app's own update checking only covers CurseForge (via file fingerprint). Two community
projects now close that gap for everything else, and a direct competitor already integrates one of
them - this is the single biggest feature gap against the rest of the ecosystem today.

- [ ] **TS4 Mod Hound integration**: Lumpinou's Mod Hound (`app.ts4modhound.com`) is a crowd-sourced,
  modder-maintained database covering mods regardless of host (Patreon, Tumblr, personal sites, not
  just CurseForge) - its "Check My Mods" function reports outdated, duplicate, broken/obsolete,
  incompatible and missing-requirement files, and modders themselves declare dependency/incompatible
  relationships between their own mods. PlumbBuddy (a close, actively developed competitor) already
  surfaces Mod Hound reports in-app. Integrating the same reports would turn the update checker from
  "CurseForge-only" into "everything the community tracks," and would let the existing dependency-hint
  and patch-risk features (S4CL/XML Injector detection, post-patch risk list) draw on modder-declared
  relationships instead of only heuristic pattern matching.
- [ ] Surface Mod Hound's declared incompatibilities the same way the existing conflict detector surfaces
  resource-level conflicts - a second, modder-sourced signal distinct from (and often catching things
  ahead of) file-level resource conflict detection.
- [ ] If direct API integration isn't available or desired, at minimum link out to a mod's Mod Hound page
  from its details panel the way CC/creator links already work.

## Safety & reliability

- [ ] Mod archive virus/heuristic scanning: optional integration with a scanning API (e.g. VirusTotal)
  for freshly downloaded script mods, alongside the existing dangerous-pattern scan.
- [ ] Verify-after-write: re-read a file immediately after a toggle/move/merge operation to catch
  silent write failures (locked file, out of disk space, antivirus quarantine) before they're
  mistaken for a successful change.
- [ ] Crash reporting opt-in: let users send `error.log` contents (with a review/redact step) instead
  of only being told the local log path.
- [ ] Backup integrity check: after a scheduled automatic backup runs (see the toast it now reports
  through), periodically verify a recent one actually restores cleanly instead of only confirming the
  copy succeeded, so a silently corrupt backup is caught before it's needed.
- [ ] Known-malicious-mod blocklist: the March 2026 ModTheSims account-compromise wave (malicious
  `.ts4script` files injected into otherwise-legitimate CC uploads, exfiltrating browser/Discord/
  Steam/Telegram data) and the earlier 2024 wave show the existing dangerous-pattern scan needs a
  second layer - a community-maintained hash/fingerprint list of confirmed-malicious files (the same
  fingerprinting the CurseForge update checker already does), checked on every scan and install, not
  just pattern-matched heuristics. Mirrors what TwistedMexi's ModGuard does standalone; worth
  offering as a built-in, opt-out layer rather than a separate mod users have to know to install.
- [ ] Network/process-behavior watch, not just static scan: the dangerous-pattern scan looks at a
  `.ts4script`'s code before install; actual incidents involve script mods that only misbehave when
  the game loads them. A lightweight runtime watch (flag if a mod's Python process opens a listening
  socket, spawns a child process, or touches browser/Discord/Steam credential paths while the game
  is running) would catch what static analysis misses - closer to what ModGuard does in-process.
- [ ] Surface security alerts proactively: a title-bar/toast notice sourced from a maintained feed
  (EA Forums "malicious script mods" thread, ModGuard's shared list) when a mod the user has
  installed - matched by fingerprint or name - gets flagged, instead of relying on the user to see
  the forum post themselves.
- [ ] Per-expansion-pack compatibility tracking: since script/tuning mods can break with a specific
  expansion's release (not just a base-game patch - e.g. custom career mods breaking against the
  Royalty & Legacy expansion), extend the existing patch-detection (`GameVersion.txt` diff) to also
  flag mods last updated before the most recently *installed* expansion/game pack, not only before
  the last numbered patch.

## Performance & architecture (the app itself)

- [ ] Incremental rescans: re-scan only the parts of the Mods folder that changed (via file-system
  watcher events already used for download detection) instead of a full walk every time.
- [ ] Virtualize the package-contents resource list for very large `.package` files instead of
  materializing the full resource table up front.
- [ ] Apply the startup scan's background-thread pattern (`Task.Run` + `MainViewModel.ApplyScanResult`,
  already proven live at startup - the UI stayed interactive mid-scan) to the manual Refresh button
  too: show a small inline "Scanning…" indicator instead of blocking, rather than the current brief
  freeze. Deliberately deferred rather than shipped alongside the startup version, to keep that
  change reviewable on its own.

## Game performance & optimization (the player's game)

Distinct from the section above: this is tooling that helps a user's actual Sims 4 session load
faster and run smoother, not the app's own responsiveness.

- [ ] Mod-count/file-count budget on the Overview tab: community consensus (and the app's own startup
  scan) is that the game's launch-time cost scales mainly with *file count* in the Mods folder (every
  package/script/thumbnail/tuning file gets scanned), not with total data size - so a "2,000 files"
  warning is more actionable than a "4 GB" one. Track it over time and flag a sudden jump (e.g. after
  a big CurseForge install) as the likely cause if load time is a recent complaint.
- [ ] Simulation-lag diagnostics: the community's standard metric is *tickrate* (time to process one
  simulation update; healthy is under ~50ms, over ~100ms is noticeable lag), normally read via a
  separate diagnostics mod. Surface the same signal without a third mod, if the game exposes it
  through a readable log/debug channel - alongside the existing exception-log analysis, since
  tuning errors in mods are a common lag cause the current Diagnostics tab already half-covers.
- [ ] A "before you hit Play" pass, built into the existing Play button flow: a rough script-mod count
  (every active `.ts4script` loads into memory and runs on the CPU at startup and simulation ticks -
  more of them lengthens startup and can add simulation-tick overhead) plus a flag for mods known to
  the community for heavy population/story-progression simulation load (the kind MCCC-style NPC
  limits target), so a performance-conscious user gets a heads-up before launching, not after.
- [ ] Honest guidance on texture/package compression: research confirms what the community has found -
  the existing "compress uncompressed resources" tool (Storage tab) saves disk space but has
  negligible effect on load time or FPS, since decompression is cheap relative to per-file scan
  overhead. The tool's description should say this plainly rather than imply a performance win, the
  same honesty already applied to the merge feature ("merging shortens load time but doesn't make
  the game run faster").
- [ ] Save-file bloat as a lag source: very large households/Sim counts and long-lived saves are a
  known, separate lag contributor from mods/CC. The Saves tab already inventories a save's
  households/Sims/CC - extend it with a rough size/Sim-count flag and point at the relevant game
  settings (or a link to an NPC-management mod) rather than implying mods are always to blame.
- [ ] A startup-time trend, not just a point-in-time scan duration: record how long the app's own mod
  scan takes each run (a reasonable proxy for game scan cost, since both walk the same folder) and
  show the trend on the Overview, so a user notices "load times crept up" as their mod count grew,
  instead of only reacting to an already-bad session.

## Workflows & automation

- [ ] One-click "automatic" mode, in the spirit of what Vortex is consolidating toward in its 2026
  roadmap (auto-sort load order, minimal-effort "get to a stable modded game"): a single action that
  runs safe-resolution conflict fixes, sorts new mods into their category folders, and flags anything
  that needs a manual decision, instead of running Sort/resolve conflicts as separate deliberate
  steps every time.
- [ ] Saved filter views ("smart folders"): let a user save a mod-list filter/search combination (e.g.
  "CAS hair, no conflicts, not favorited") under a name and reselect it later, instead of rebuilding
  multi-criteria filters from scratch each session.
- [ ] User-defined action chains for repeat routines: e.g. "before every play session: clear cache,
  apply the Safe Mode profile if the game updated, rescan" as one named, one-click sequence built
  from actions the app already exposes individually.
- [ ] A download/install queue: CurseForge Browse installs one mod (plus its dependencies) at a time
  today; let a user multi-select several catalog results and queue them, processing sequentially
  with progress per item and a single combined History/undo entry per item (not merged into one, so
  a bad install among several doesn't force undoing all of them).
- [ ] Named History checkpoints: today the last 100 actions are a flat undo stack; let a user bookmark
  "known-good" points (e.g. right after a successful play session) to jump back to directly, instead
  of stepping through intervening actions one at a time.
- [ ] Side-by-side profile diff: given the existing profile system, show exactly which mods/settings
  differ between two profiles in one view, for users who keep a "testing" profile alongside their
  main one and want to know what changed before merging the two.

## App-level features

- [ ] A custom accent color beyond the eight presets already offered (a small hex/RGB picker), for the
  accent color picker (paint brush icon, title bar).
- [ ] Minimize-to-tray and an optional "launch with Windows" setting, for people who leave the app
  running alongside the game rather than closing it after "Spielen".
- [ ] Opt-in, anonymous usage analytics: feature/tab usage and OS/game-version counts only, no file
  paths or mod names, gated behind an explicit opt-in during setup (default off). The roadmap today
  is prioritized from issue-tracker volume and manual research alone; this would give real data for
  calls like the macOS-port question below, without compromising the local-only, privacy-preserving
  design the app has otherwise kept.
- [ ] A plugin/extension point for community-contributed health checks, beyond the built-in diagnostic
  set: a small, sandboxed way for a power user or community maintainer to add a new detection rule
  (e.g. a newly discovered incompatibility pattern) without waiting for an app release.

## Fun tools & personalization

Two competitors (GameTimeDev's S4MM "Fun Tools" menu, PlumbBuddy's config-override editing) ship
small cosmetic/quality-of-life tools that aren't mod management in the strict sense but are a real
reason players pick one app over another. Worth matching:

- [ ] Custom loading screen creator: pick an image, generate the package the game reads for its loading
  screen, install/replace it through the normal enable-disable + History pipeline (only one loading-
  screen mod can be active at a time - the app should enforce that instead of leaving it to the user
  to remember, unlike the competing tools).
- [ ] A generic `ConfigOverride` editor: the game reads a number of unofficial `.ini` overrides from
  `ConfigOverride` (plumbob color, camera settings, clock speed multiplier, neighborhood story
  progression rate, among others) that are otherwise hand-edited text files passed around forums as
  snippets. A form-based editor for the known/documented keys, validated before writing, covers the
  plumbob-color tool GameTimeDev ships as a separate "Fun Tool" and generalizes past it.
- [ ] Custom CC-indicator icon (the CAS/Build-Buy "wrench" badge): a small settings toggle to swap it for
  one of several alternate icons, matching what's otherwise a separate small CC download.

## Platform & distribution

- [ ] macOS support: at least one competing Sims 4 mod manager (GameTimeDev's S4MM) ships a macOS build,
  and the Mac/Steam Play version of the game is a real share of the playerbase. The current app is
  WPF (.NET, Windows-only by construction) - a port isn't a small lift, but a shared `Core` project
  already has no UI dependency, which is the precondition for one; worth scoping as "port the App
  layer to Avalonia" rather than a rewrite, if there's ever demand data to justify it (see the
  opt-in usage-analytics idea above).
- [ ] Multiple game installs: detect and let a user switch between more than one Sims 4 install (e.g.
  Steam and EA App side by side, or a Game Pass install), each with its own Mods folder, instead of
  only ever tracking the single most-recently-active one autodetection currently picks.
- [ ] Portable/USB mode: run against a Mods folder on removable media without writing anywhere under
  `%AppData%`, for people who carry their mod setup between PCs (e.g. a laptop and a desktop).

## Localization & accessibility

- [ ] Additional UI languages beyond German/English - French, Spanish and Portuguese cover a large
  share of the Sims 4 modding community. The existing `L.T`/`L.F` + per-language JSON dictionary
  architecture already supports adding a language file without touching call sites.
- [ ] Screen-reader pass: audit `AutomationProperties.Name` coverage on icon-only buttons (several
  exist in the mod grid and title bar) and custom controls.
- [ ] High-contrast theme support, verified against Windows' built-in high-contrast modes rather than
  only the app's own dark/light themes.

## Testing & CI

- [ ] ViewModel-level tests for `Sims4ModManager.App` - `tests/Sims4ModManager.Core.Tests` covers Core
  thoroughly, but the App project's view models (filtering, favorite/note state, command wiring)
  currently have no automated coverage of their own.
- [ ] Code coverage reporting in CI, surfaced as a badge on the README.
- [ ] A smoke test in the release workflow that actually launches the packed installer output, beyond
  the current build-and-unit-test gate.

## Documentation & community

- [ ] A short video/GIF walkthrough embedded in the README for the features hardest to describe in
  text (conflict resolution, the merge tool).
- [ ] A `docs/FAQ.md` seeded from recurring issue-tracker questions once there's a track record to
  draw from.
- [ ] A maintained comparison note against alternatives (GameTimeDev's S4MM, PlumbBuddy, the official
  CurseForge app, Sims 4 Ultimate Mod Manager, TwistedMexi's standalone tools, Sims 4 Studio, Mod
  Hound) in the README or a `docs/Comparison.md`: what this project covers that they don't
  (resource-level conflict resolution with file-level auto-fixes, full undo/journal history covering
  every action rather than just mod toggles, Tray/CC dependency detection, a base-game resource index
  for default-replacement and missing-mesh detection) and, just as honestly, what it doesn't yet (no
  macOS build - see [Platform & distribution](#platform--distribution); no crowd-sourced update
  tracking beyond CurseForge - see
  [Community mod-tracking integration](#community-mod-tracking-integration); no CAS/package editing
  the way Sims 4 Studio does, and deliberately so - see "Bewusst nicht geplant" in
  [Funktionsplan.md](Funktionsplan.md)). Helps newcomers choose without guessing, and keeps scope
  decisions grounded against what the rest of the ecosystem actually does rather than against
  memory of it.
- [ ] A pointer, kept current, to wherever the community tracks post-patch breakage (the EA Forums
  "Broken and Updated Mods" thread) from inside the app itself - the patch-detection banner (A3 in
  [Funktionsplan.md](Funktionsplan.md)) already links the thread for the detected version; extend
  the same pointer to the "What's new"/health-check surfaces so it's visible without hunting for it.
