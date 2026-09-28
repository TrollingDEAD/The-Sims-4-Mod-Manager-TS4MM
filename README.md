# Sims 4 Mod Manager

Ein Mod-Manager für Die Sims 4: Mods aktivieren/deaktivieren, Metadaten anzeigen,
Ressourcenkonflikte zwischen Packages erkennen und Mod-Auswahlen als Profile speichern.

Änderungen an diesem Projekt stehen in [CHANGELOG.md](CHANGELOG.md); die aktuelle Version zeigt
die App selbst oben in der Titelleiste an.

## Projektstruktur

- `src/Sims4ModManager.Core` – Kernlogik (kein UI-Bezug): Scannen des Mods- und Tray-Ordners,
  DBPF-Parsing (`.package`-Dateien), Aktivieren/Deaktivieren, Konflikterkennung, Profile.
- `src/Sims4ModManager.App` – WPF-Oberfläche (.NET 8, MVVM mit CommunityToolkit.Mvvm).
- `tests/Sims4ModManager.Core.Tests` – xUnit-Tests für die Kernlogik.

## Funktionsweise

- **Erkennung:** Der Mods-Ordner wird automatisch gesucht: in allen Dokumente-Ordnern (auch
  umgeleitet oder in OneDrive, inkl. „Dokumente“) unter `Electronic Arts\<Sims 4-Ordner>\Mods`.
  Der Sims 4-Ordner wird unabhängig von der Spielsprache erkannt („The/Die/Les Sims 4“ …, notfalls
  an typischen Spieldateien wie `Options.ini` oder `saves`). Bei mehreren Installationen gewinnt die
  mit Mods-Ordner und zuletzt gespielte. Wird versehentlich der Sims 4-Ordner statt `Mods` gewählt,
  wird das korrigiert. Mod-Dateien, die neben statt in `Mods` liegen (und vom Spiel ignoriert
  werden), werden als Warnung angezeigt.
- **Mods:** Jede lose Datei bzw. jeder Ordner auf oberster Ebene im Mods-Ordner zählt als
  ein Mod. Mehrdateien-Mods (z. B. CC-Packs mit mehreren `.package`-Dateien) werden als
  Einheit ein-/ausgeschaltet.
- **Aktivieren/Deaktivieren:** Erfolgt reversibel durch Anhängen/Entfernen der Endung
  `.disabled` an die Dateiendung – Dateien werden nicht verschoben, Ordnerstruktur bleibt
  erhalten.
- **Konflikterkennung:** Liest den DBPF-Index jeder aktiven `.package`-Datei (Type/Group/Instance,
  gelöschte Einträge werden ignoriert) sowie die Python-Module jeder `.ts4script`-Datei und meldet
  Ressourcen bzw. Module, die von mehr als einem Mod bereitgestellt werden.
  - Byte-identische Duplikate (auch unterschiedlich komprimiert) gelten nicht als Konflikt.
  - Konflikte werden pro Gruppe betroffener Mods zusammengefasst, mit Ressourcentyp und
    Schweregrad (Hoch: Tuning/SimData/CAS-Teile/Objekte/Skripte, Mittel: Meshes/Texturen/Strings,
    Niedrig: Vorschaubilder).
  - Nicht lesbare Dateien werden als Warnung angezeigt statt stillschweigend übergangen.
- **Profile:** Momentaufnahmen der aktivierten Mods, gespeichert unter
  `%AppData%\Sims4ModManager\profiles` (inkl. zugehörigem Mods-Ordner). Beim Anwenden werden
  nicht mehr vorhandene Mods gemeldet.
- **Übersicht (Gesundheitscheck):** Startseite mit Spielversion, Update-Erkennung, den Mod-Schaltern
  des Spiels (`Options.ini`: Mods/CC, Skript-Mods, CC-Liste beim Start), Cache-Status und allen
  gefundenen Problemen samt automatischer Behebung („Alle sicheren beheben“). Geprüft wird u. a.:
  - Sims-2/3-Packages (DBPF 1.x/2.0), beschädigte und leere Dateien, abgebrochene Downloads
  - `.ts4script` tiefer als 1 und `.package` tiefer als 5 Unterordner, Pfade über 259 Zeichen
  - Readmes/Bilder im Mods-Ordner, leere Ordner, per Hand deaktivierte Mods (`.packageOFF` …),
    fehlende oder unvollständige `Resource.cfg`, Sonderzeichen
  - OneDrive-Sync und Mods, die nur in der Cloud liegen
  - veralteter Spiel-Cache; nach Spiel-Updates die gefährdeten Skript- und Gameplay-Mods
    („Sicherer Modus“ deaktiviert sie vorübergehend)
  Aussortierte Dateien werden nach `Mods (aussortiert)` neben dem Mods-Ordner verschoben, nie gelöscht.
  Außerdem: Mod-Liste als Text (Discord/Foren) oder CSV exportieren bzw. kopieren. Läuft das Spiel,
  warnt die App vor Aktionen, die Dateien ändern.
- **Konfliktlösung:** Zu jeder Konfliktgruppe gibt es Lösungsvorschläge (Tab „Lösungen“), die auf
  Dateiebene arbeiten und Mods möglichst behalten:
  - *Doppelt installierte Datei* → ältere Kopie deaktivieren
  - *Ältere Version* (v1/v2, „Fixed“, „Update“, „(1)“ …) → ältere Version deaktivieren
  - *HQ- und NonHQ-Variante* → NonHQ deaktivieren (Alternative: HQ)
  - *In MERGED-Set enthalten* (alle Ressourcen stecken schon in einem anderen Package) → Einzeldatei deaktivieren
  - *Echte Überschreibung* zwischen verschiedenen Mods → nur die umstrittenen Ressourcen aus dem
    Package des Verlierers entfernen (Package wird neu geschrieben, der Rest des Mods bleibt);
    Standard-Gewinner ist die neuere Datei, die Gegenrichtung wird als Alternative angeboten
  - *Skript-Überschneidung* → ältere `.ts4script` deaktivieren
  „Sichere Lösungen“ wendet alle reinen Deaktivierungen von Duplikaten in einem Schritt an.
  Das Merge-Manifest von Sims 4 Studio (Typ `7FB6AD8A`) zählt nicht als Konflikt.
- **Verlauf & Sicherung:** Jede Aktion, die Dateien ändert (Aktivieren/Deaktivieren, Profile,
  Installationen, Aufräumen, Entfernen, Konfliktlösungen), wird in
  `%AppData%\Sims4ModManager\backups\journal` protokolliert; überschriebene oder entfernte Dateien
  werden vorher kopiert. Im Tab „Verlauf“ (oder mit `Strg+Z`) lässt sich jede Aktion zurücknehmen;
  Dateien, die seitdem erneut geändert wurden, werden dabei nie überschrieben. Die letzten 100
  Aktionen bleiben erhalten.
- **Bibliothek (Tray):** Eigener Tab für den `Tray`-Ordner (lokale Bibliothek: Haushalte,
  Grundstücke, Räume).
  - Zeigt Name, Ersteller, Beschreibung, Sims, Grundstücksgröße, Tags und Datum aus den
    `.trayitem`-Dateien (Protobuf) und fasst alle zusammengehörigen Dateien eines Eintrags
    zusammen (inkl. Sim-Porträts).
  - Meldet unvollständige Einträge (fehlende `.trayitem`/`.householdbinary`/`.blueprint`/`.room`).
  - **CC-Erkennung:** findet die Mod-Dateien, deren CAS-Teile, Objekte und Bauelemente ein
    Eintrag verwendet – auch deaktivierte, die sich per Klick aktivieren lassen. Die Mod-Liste
    zeigt umgekehrt, in wie vielen Bibliothekseinträgen ein Mod verwendet wird.
  - **Export** eines Eintrags samt CC als Ordner oder ZIP (`Tray\` + `Mods\`).
  - **Downloads installieren** (ZIP/RAR/7z, auch verschachtelt, Ordner oder Einzeldateien):
    Tray-Dateien unverändert und flach nach `Tray`, `.package`/`.ts4script` nach
    `Mods\<Download>\`. Bereits vorhandene (auch anderswo in `Mods`) und gleichnamige andere
    Dateien werden erkannt und nicht überschrieben.
  - **Aufräumen:** Tray-Dateien in `Mods`, Mods in `Tray`, Tray-Unterordner, umbenannte
    Tray-Dateien und nicht entpackte Archive werden gemeldet und auf Wunsch verschoben.
  - **Entfernen** verschiebt Einträge in `%AppData%\Sims4ModManager\backups` (wiederherstellbar
    über „Ordner installieren“); **Sicherung** packt Tray + Saves (optional Mods) in ein ZIP.
  - Vorschaubilder (`.hhi`/`.sgi`/`.bpi`/`.rmi`) werden nicht angezeigt – ihr Format ist
    verschlüsselt und nicht dokumentiert.
- **Spielen:** Der Button „Spielen“ (oben rechts und in der Übersicht) wendet optional ein Profil
  an, leert den Cache, sichert auf Wunsch die Spielstände und startet `TS4_x64.exe` (Installation
  per Registry/Steam gefunden). Nach dem Beenden wird neu eingelesen und auf neue
  Fehlerprotokolle hingewiesen.
- **Diagnose:** Liest `lastException*`, `lastUIException*`, `lastCrash*` und MC-Command-Center-
  bzw. Better-Exceptions-Berichte, fasst gleiche Fehler zusammen und nennt den verursachenden
  Mod (Pfad der `.ts4script` im Traceback bzw. Python-Modulname), mit Button zum Deaktivieren.
  Fehler aus älteren Spielversionen werden markiert und lassen sich aufräumen.
  Der **50/50-Assistent** teilt die aktiven Mods schrittweise in Hälften, bis der Verursacher
  eines Problems gefunden ist; der Fortschritt übersteht einen Neustart der App
  (`bisect.json`), alle Umschaltungen laufen über den Verlauf.
- **Spielstände:** Zeigt alle Saves mit Haushalten, Sims, Grundstücken, Spielversion und dem
  verwendeten CC (auch deaktiviertem). Vor dem Deaktivieren eines Mods, den ein Spielstand
  nutzt, wird nachgefragt. Saves lassen sich einzeln sichern und wiederherstellen
  (`backups\saves`).
- **Downloads & Sicherheit:** Neue Sims-Downloads im Download-Ordner werden erkannt und per
  Banner zur Installation angeboten (abschaltbar). Skript-Mods werden auf gefährliche Muster
  (eingebettete `.exe`, Prozessstart, verschleierter Code) geprüft – auch vor der Installation.
  Fehlende oder deaktivierte Bibliotheken (S4CL, XML Injector, Lot 51 Core) werden gemeldet.
- **Katalog:** Jede Mod-Datei als Kachel mit dem Vorschaubild aus dem Package (inkl. Transparenz),
  Namen aus den Stringtabellen, Kategorie (CAS · Haare/Kleidung/Make-up …, Objekte, Bauelemente,
  Gameplay, Skripte, Slider, Posen, Ersatztexturen), Ersteller, Altersstufen und Geschlecht aus den
  CAS-Teilen. Filter nach Kategorie, Alter, Geschlecht; Ergebnisse werden in `cache\` zwischengespeichert.
- **Sortieren:** Ordnet Mods mit Vorschau in Sammelordner wie `CAS - Haare\<Ersteller>`, `Objekte` oder
  `Skript-Mods` ein (Tiefenregeln des Spiels werden eingehalten, Skript-Mod-Ordner bleiben). Sammelordner
  tragen die Markierung `.s4mm-sammlung`; jeder Mod darin bleibt einzeln schaltbar und behält seine ID.
  Eigene Ordner lassen sich ebenfalls „als Sammelordner“ markieren. Alles über den Verlauf rückgängig.
- **Details & Notizen:** Im Tab „Mods“ zeigt „Details“ Vorschau, Kategorie, Ersteller und Dateien; dazu
  Notiz, „Warum installiert?“, Tags sowie Download- und Ersteller-Link (`notes.json`, außerhalb des Mods-Ordners).
- **Speicherplatz:** Größe nach Kategorie und Ersteller, größte Mods, byte-identische Duplikate (überzählige
  Kopien entfernbar) und unkomprimiert gespeicherte Daten („Komprimieren …“ komprimiert sie verlustfrei).
- **Spiel (Spielindex):** Findet die Installation (Registry, EA App, Steam oder manuell) und liest die
  Ressourcen-Indizes aller Spiel-Packages (Basisspiel, `Delta`-Patches, alle Packs; ohne Stringtabellen) –
  gecacht in `cache\gameindex.bin`, neu aufgebaut nach Updates oder neuen Packs. Damit:
  - **Default Replacements & Tuning-Overrides:** Mods, die Ressourcen des Spiels ersetzen (Symbol und Filter
    „Ersetzt Spielinhalte“ in der Mod-Liste). Spiel-Tuning steckt in einem Binärblock; Tuning-Overrides werden
    deshalb an SimData-Schlüsseln bzw. an EA-typischer ID und Name erkannt (Schätzung). Tuning-Overrides, die
    älter als das letzte Spiel-Update sind, meldet die Übersicht.
  - **Recolors ohne Mesh:** CAS-Teile (GEOM-Verweise) und Objekte (Modell-Verweis der OBJD), deren Mesh weder
    ein Mod noch das Spiel enthält – oder nur ein deaktivierter Mod.
  - **Bibliothek:** benötigte Packs und fehlender CC pro Haushalt (die Verweis-Felder werden aus den Daten
    gelernt). Grundstücke und Räume speichert das Spiel komprimiert – dort nur „mindestens“-Angaben.
- **Package-Werkzeuge** (Tab „Mods“ → „Inhalt“): Ressourcen nach Typ, „Optimieren“ (doppelte Einträge
  entfernen, verlustfrei komprimieren), „Zerlegen“ für zusammengeführte Packages. **Zusammenführen …** fasst
  CC-Mods zu einem Package zusammen; die Liste der Originaldateien wird im Format von Sims 4 Studio
  gespeichert (`0x7FB6AD8A`), sodass beide Programme solche Packages zerlegen können.
- **Updates (CurseForge):** Erkennt CurseForge-Mods am Datei-Fingerabdruck (MurmurHash2 wie CurseForge,
  gecacht in `cache\fingerprints.json`), zeigt neuere Releases und installiert sie über den Verlauf
  (rückgängig machbar). Braucht einen eigenen, kostenlosen API-Schlüssel (console.curseforge.com); er wird
  per Windows-DPAPI verschlüsselt in den Einstellungen gespeichert.
- **Was hat sich geändert?** (Tab „Verlauf“): Vergleich mit dem täglichen Stand bzw. dem Stand vor dem
  letzten Spielstart – neue, entfernte, geänderte, (de)aktivierte und verschobene Dateien.
- **Globale Suche** (`Strg+K`): Mods (Name, Ersteller, Kategorie, Notizen, Tags, Namen im Spiel), Bibliothek
  und Spielstände.
- **Automatische Sicherung:** vor jedem Spielstart und/oder täglich/wöchentlich beim App-Start; pro Slot
  werden nur die neuesten automatischen Sicherungen behalten. Die Bibliothek wird inkrementell gespiegelt
  (`backups\tray`, ältere Stände 30 Tage).
- **Einrichtungsassistent** beim ersten Start (Sprache, Mods-Ordner inkl. OneDrive-Hinweis, Spielschalter,
  erste Sicherung, Überblick); später über das Zauberstab-Symbol.
- **Sprache:** Deutsch oder Englisch (Button „EN“/„DE“ in der Titelleiste, Neustart). Deutsch ist die
  Quellsprache; die Übersetzungen stehen in `src/Sims4ModManager.Core/Localization/en.json`.
- **Oberfläche:** Fluent-Design (WPF-UI) mit dunklem Theme (umschaltbar auf hell über das
  Symbol in der Titelleiste, wird gespeichert), Icons und Tooltips an allen Bedienelementen,
  Suche und Filter in der Mod-Liste (u. a. „Mit Konflikten“, „In Bibliothek verwendet“),
  Warnsymbol an Mods mit Konflikten, abgeblendete deaktivierte Mods.
  Tastenkürzel: `F5` aktualisieren, `Strg+F` Suche, `Enter` im Profilnamen speichert,
  Doppelklick auf einen Bibliothekseintrag zeigt ihn im Explorer.
- **Persistenz:** Einstellungen (`%AppData%\Sims4ModManager\settings.json`: Mods-Ordner, zuletzt
  genutzte Ordner, letztes Profil, Fensterposition) und Profile werden atomar geschrieben; die
  vorherige Version bleibt als `.bak` erhalten und wird bei einer beschädigten Datei automatisch
  verwendet.

## Bauen & Ausführen

```bash
dotnet build
dotnet run --project src/Sims4ModManager.App
```

Bei Grafiktreiber-Problemen (leeres Fenster) lässt sich die App ohne GPU-Beschleunigung starten:
Umgebungsvariable `S4MM_SOFTWARE_RENDERING=1` setzen. Unerwartete Fehler werden in
`%AppData%\Sims4ModManager\error.log` protokolliert.

## Tests

```bash
dotnet test
```
