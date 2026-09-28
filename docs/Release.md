# Publishing a release

The app updates itself via [Velopack](https://velopack.io): on startup it quietly checks this
repo's GitHub Releases in the background (`AppUpdateViewModel`); if a newer version exists, a
button appears in the title bar to download it and - after a restart - install it. This only works
for an app installed via Velopack (not `dotnet run`), and only from the first version published
this way onward.

## Steps for a new version

1. **Decide the version** (SemVer: `MAJOR.MINOR.PATCH`) and set it in
   [`Directory.Build.props`](../Directory.Build.props) (`VersionPrefix`). This is the single place
   that affects the build - the app shows this version in the title bar, and `vpk pack` (below)
   requires the exact same number in the git tag.
2. **Update [`CHANGELOG.md`](../CHANGELOG.md)**: rename the `[Unreleased]` section to
   `## [MAJOR.MINOR.PATCH] - YYYY-MM-DD` and add the compare link at the bottom of the file.
3. Commit both and push to `main`.
4. **Tag and push** (with a `v` prefix, must match the `VersionPrefix` above):
   ```bash
   git tag -a v1.2.0 -m "v1.2.0"
   git push origin v1.2.0
   ```
5. The tag push triggers [`.github/workflows/release.yml`](../.github/workflows/release.yml): it
   builds the app (`dotnet publish`, self-contained, win-x64), extracts that version's section from
   `CHANGELOG.md` to use as the release notes, packs everything with the
   [Velopack CLI](https://docs.velopack.io/reference/cli/content/vpk-windows) (`vpk pack`), and
   uploads the result as a GitHub Release (`vpk upload github --publish`). This takes a few
   minutes; progress is visible under the repo's "Actions" tab. (This is also why step 2 matters -
   without a matching `## [MAJOR.MINOR.PATCH]` heading in `CHANGELOG.md`, this step fails.)
6. Done - existing installations pick up the new release automatically on their next launch.

## First install (not an update - a fresh install)

Besides the Velopack update packages, a release also contains `Sims4ModManager-win-Setup.exe` - the
installer for new users (linked from the release page on GitHub). Existing installations don't
need it; they update via the in-app button instead.

**Important:** users who installed the app before Velopack was introduced (the portable version,
no Setup.exe) need to switch to `Sims4ModManager-win-Setup.exe` once, manually - after that,
self-updating takes over.

## Publishing manually (without CI, e.g. for testing)

```bash
dotnet publish src/Sims4ModManager.App/Sims4ModManager.App.csproj -c Release -o publish -r win-x64 --self-contained true

dotnet tool install -g vpk   # one-time
vpk download github --repoUrl https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM --token <PAT>
vpk pack --packId Sims4ModManager --packVersion 1.2.0 --packDir publish --mainExe Sims4ModManager.App.exe --packTitle "Sims 4 Mod Manager" --packAuthors "TrollingDEAD" --runtime win-x64 --icon src/Sims4ModManager.App/Resources/app.ico
vpk upload github --repoUrl https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM --publish --releaseName "Sims 4 Mod Manager v1.2.0" --tag v1.2.0 --token <PAT>
```

`--runtime win-x64` matters: without it, `vpk pack` silently assumes x86 in its package metadata
even though the published binaries are x64 (confirmed by dry-running this exact pipeline before
the first release).

`<PAT>` is a GitHub Personal Access Token with `repo` scope; the CI workflow supplies this
automatically via `secrets.GITHUB_TOKEN`.

## Known gaps

- Code signing isn't set up; Windows SmartScreen may warn on `Sims4ModManager-win-Setup.exe` until
  it is.
