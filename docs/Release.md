# Releases veröffentlichen

Die App aktualisiert sich selbst über [Velopack](https://velopack.io): beim Start prüft sie leise
im Hintergrund die GitHub Releases dieses Repos (`AppUpdateViewModel`); ist eine neuere Version
vorhanden, erscheint oben in der Titelleiste ein Button zum Herunterladen und - nach einem
Neustart - Installieren. Das funktioniert nur bei einer per Velopack installierten App (nicht bei
`dotnet run`), und erst ab der ersten Version, die so veröffentlicht wurde.

## Ablauf für eine neue Version

1. **Version festlegen** (SemVer: `MAJOR.MINOR.PATCH`) und in [`Directory.Build.props`](../Directory.Build.props)
   eintragen (`VersionPrefix`). Das ist die einzige Stelle, die den Build-Prozess betrifft - die
   App zeigt diese Version in der Titelleiste, und `vpk pack` (siehe unten) verlangt exakt dieselbe
   Nummer im Git-Tag.
2. **[`CHANGELOG.md`](../CHANGELOG.md)** aktualisieren: den `[Unreleased]`-Abschnitt in
   `## [MAJOR.MINOR.PATCH] - YYYY-MM-DD` umbenennen und die Compare-Links am Dateiende ergänzen.
3. Beides committen und auf `main` pushen.
4. **Tag setzen und pushen** (mit `v`-Präfix, muss zur `VersionPrefix` oben passen):
   ```bash
   git tag -a v1.2.0 -m "v1.2.0"
   git push origin v1.2.0
   ```
5. Der Tag-Push löst den Workflow [`.github/workflows/release.yml`](../.github/workflows/release.yml)
   aus: er baut die App (`dotnet publish`, self-contained, win-x64), packt sie mit der
   [Velopack-CLI](https://docs.velopack.io/reference/cli/content/vpk-windows) (`vpk pack`) und lädt
   das Ergebnis als GitHub Release hoch (`vpk upload github --publish`). Das dauert einige Minuten;
   Fortschritt steht im "Actions"-Tab des Repos.
6. Fertig - laufende Installationen finden das neue Release beim nächsten Start automatisch.

## Erste Installation (kein Update, sondern Neuinstallation)

Ein Release enthält neben den Velopack-Update-Paketen auch `Sims4ModManagerSetup.exe` - das ist
die Installer-Datei für neue Nutzer (verlinkt von der Release-Seite auf GitHub). Bestehende
Installationen brauchen das nicht; sie aktualisieren sich über den Button in der App.

**Wichtig:** Nutzer, die die App vor der Einführung von Velopack installiert haben (portable
Version, keine Setup.exe), müssen einmalig manuell auf `Sims4ModManagerSetup.exe` umsteigen -
danach läuft die Selbstaktualisierung.

## Manuell veröffentlichen (ohne CI, z. B. zum Testen)

```bash
dotnet publish src/Sims4ModManager.App/Sims4ModManager.App.csproj -c Release -o publish -r win-x64 --self-contained true

dotnet tool install -g vpk   # einmalig
vpk download github --repoUrl https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM --token <PAT>
vpk pack --packId Sims4ModManager --packVersion 1.2.0 --packDir publish --mainExe Sims4ModManager.App.exe --packTitle "Sims 4 Mod Manager" --packAuthors "TrollingDEAD"
vpk upload github --repoUrl https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM --publish --releaseName "Sims 4 Mod Manager v1.2.0" --tag v1.2.0 --token <PAT>
```

`<PAT>` ist ein GitHub Personal Access Token mit `repo`-Rechten; im CI-Workflow übernimmt das
automatisch `secrets.GITHUB_TOKEN`.

## Offene Punkte

- Es gibt noch kein App-Icon (`.ico`); `vpk pack` läuft auch ohne (`--icon`), Setup.exe und
  Taskleiste zeigen dann ein Platzhalter-Symbol.
- Codesigning ist nicht eingerichtet; Windows SmartScreen kann bei `Sims4ModManagerSetup.exe`
  deshalb vorerst warnen.
