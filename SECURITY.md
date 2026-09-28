# Security Policy

## Supported Versions

Sims 4 Mod Manager auto-updates in place (see the [self-update feature](README.md#self-updating)),
so only the **latest released version** is supported. If you can reproduce an issue, please update
to the latest release first - the title-bar update button, or a fresh download from
[Releases](https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/releases/latest), gets you
there.

## Reporting a Vulnerability

Please **do not** open a public issue for a security vulnerability.

Instead, use GitHub's private reporting:

1. Go to the [Security tab](https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM/security)
   of this repository.
2. Click **"Report a vulnerability"** to open a private advisory.

This reaches the maintainer directly and keeps the report confidential until a fix is available.

Please include:

- A description of the vulnerability and its potential impact.
- Steps to reproduce it (a minimal example if possible).
- The affected version (see `v` in the title bar, or `AppVersion` in a bug report).

There's no formal SLA (this is a single-maintainer hobby project), but security reports are
prioritized over regular bug fixes and feature work.

## Scope

This app reads and writes files under the Sims 4 user-data folder (`Mods`, `Tray`, saves) and its
own settings under `%AppData%\Sims4ModManager`, and talks to two external services: GitHub
Releases (self-update) and, if you supply your own API key, the CurseForge API (mod updates). A
vulnerability report is most useful if it relates to one of these: for example, unsafe handling of
a malformed `.package`/save file, path traversal when installing a download, or an issue in how
updates are verified/applied.
