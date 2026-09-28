# Funktionsplan: Sims 4 Mod Manager als „ein Tool für alles“

Ziel: Alle Werkzeuge und Handgriffe, die Simmer beim Modden regelmäßig brauchen, in einem Programm
vereinen – ohne Sims 4 Studio, Tray Importer, Mod Conflict Detector, Better Exceptions, Mod Hound,
CurseForge oder Aufräum-Skripte nebenbei.

Grundlage: Recherche in Foren und Community-Seiten (Quellen am Ende) und ein Abgleich mit echten
Daten aus einem Sims-4-Ordner (Options.ini, GameVersion.txt, lastException-Dateien).

---

## Bestandsaufnahme: Was es schon gibt

| Bereich | Bereits im Tool |
|---|---|
| Mods | Scannen, Aktivieren/Deaktivieren, Profile, Suche/Filter |
| Konflikte | Erkennung auf Ressourcen- und Skriptebene, identische Duplikate ignoriert, Lösungsvorschläge, Package-Bearbeitung |
| Bibliothek | Tray-Einträge, CC-Erkennung, Export, Installation (ZIP/RAR/7z), Aufräumen falsch abgelegter Dateien |
| Sicherheit | Änderungsjournal mit Rückgängig für alle Aktionen, ZIP-Sicherung von Tray/Saves/Mods |
| Ordner | Erkennung in jeder Spielsprache und OneDrive, Warnung bei Mod-Dateien außerhalb von `Mods` |

Die neuen Funktionen bauen darauf auf. Jede Funktion, die Dateien verändert, läuft über das
Änderungsjournal und lässt sich damit rückgängig machen.

---

## A. Diagnose & Fehlersuche

### A1 Fehlerprotokoll-Analyse *(ersetzt: manuelles Lesen, teils Better Exceptions)*
**Problem:** „Last Exception“-Meldungen sind die häufigste Forenfrage. Die Dateien liegen als
`lastException_*.txt`, `lastUIException_*.txt`, `lastCrash_*.txt`, `MC_LastException.html` (MCCC) oder
`BE-ExceptionReport` im Spielordner und sind für Laien unlesbar (XML mit HTML-kodiertem Python-Traceback).

**Funktion:**
- Alle Protokolle auflisten: Datum, Art (Exception/UI/Crash/Desync), Spielversion aus `buildsignature`.
- Traceback lesbar aufbereiten und **den Verursacher zuordnen**:
  - Pfade wie `Mods\XYZ.ts4script\modul.py` → direkt der Skript-Mod.
  - Python-Modulnamen → über den vorhandenen Skript-Modul-Index zum `.ts4script`.
  - Objekt-/Tuning-Namen (z. B. `object_bedDoubleSC_01`) → über Name Maps (`0x0166038C`) und
    Objektdefinitionen der Packages zur CC-Datei.
- Gleiche Fehler zusammenfassen („12× derselbe Fehler“).
- **Alte Protokolle erkennen:** Wurde der Fehler unter einer älteren Spielversion geschrieben als der
  aktuellen (`GameVersion.txt`), ist er meist erledigt. Beispiel aus den Testdaten: Fehler von 1.121,
  das Spiel ist inzwischen auf 1.126.
- Direkt aus dem Fehler: „Verdächtigen Mod deaktivieren“ und „Alte Protokolle aufräumen“.

**Technik:** XML-Parser + Regex über den Traceback, vorhandene Indizes (Skriptmodule, Package-Ressourcen).
**Aufwand:** mittel · **Nutzen:** sehr hoch

### A2 50/50-Assistent *(ersetzt: manuelle 50/50-Methode)*
**Problem:** Die in allen Foren empfohlene Standardmethode: Hälfte der Mods raus, testen, wiederholen.
Kein Tool nimmt einem das ab, weil nur der Spieler im Spiel sehen kann, ob der Fehler noch auftritt.

**Funktion:** Ein geführter Ablauf, der das Umschalten übernimmt:
1. Fehler beschreiben (optional mit Verweis auf ein Fehlerprotokoll).
2. Tool deaktiviert die Hälfte der aktiven Mods. Zusammengehörige Dateien bleiben zusammen:
   Skript-Mod + Package, Mesh + Recolors, Einträge desselben Ordners.
3. Spiel starten, testen, im Tool „Fehler noch da? Ja/Nein“ klicken.
4. Wiederholen, bis nur noch der Verursacher übrig ist. Fortschritt übersteht Neustarts von App und PC.
5. Am Ende: Verursacher anzeigen, alles andere wird wieder aktiviert.

Nach etwa 11–12 Runden ist aus 2.266 Mods der Verursacher gefunden. Jeder Schritt steht im Journal.
**Aufwand:** mittel · **Nutzen:** sehr hoch

### A3 Patch-Erkennung *(ersetzt: EA-Forum-Listen „Broken and Updated Mods“, teils Mod Hound)*
**Problem:** Nach jedem Spiel-Update brechen Skript-Mods und Tuning-Overrides. Die Community pflegt dafür
händisch Listen.

**Funktion:**
- `GameVersion.txt` bei jedem Start vergleichen → Hinweis „Spiel wurde von 1.125 auf 1.126 aktualisiert“.
- Liste der **gefährdeten Mods:** Skript-Mods und Packages mit Tuning/SimData, deren Datei älter als der
  Patch ist.
- Ein-Klick-Profil „Sicherer Modus nach Patch“: Skript-Mods und Tuning-Mods aus, CC bleibt an.
- Link zum aktuellen EA-Forum-Thread „Broken and Updated Mods“ für die Version.

**Aufwand:** gering · **Nutzen:** hoch

### A4 Spieleinstellungen in `Options.ini` *(ersetzt: Suchen im Spielmenü)*
**Problem:** Nach Patches deaktiviert das Spiel häufig Mods. „Mods werden nicht geladen“ ist eine der
Top-Fragen.

**Funktion:** Anzeige und Umschalten von `modsdisabled`, `scriptmodsenabled` und `showmodliststartup`
(die CC-Liste beim Start verlängert laut Foren die Ladezeit). Ein deutlicher Warnhinweis erscheint, wenn
Mods im Spiel ausgeschaltet sind. Schreiben nur bei geschlossenem Spiel und über das Journal.
**Aufwand:** gering · **Nutzen:** hoch

### A5 Falsche und kaputte Dateien *(ersetzt: ModFix, Mod Folder Cleaner, teils Mod Conflict Detector)*
- **Sims-2/3-Dateien erkennen:** TS2 nutzt DBPF 1.x, TS3 DBPF 2.0, TS4 DBPF 2.1. TS3-CC im Mods-Ordner
  lässt das Spiel laut Foren oft schon vor dem Hauptmenü abstürzen. Dazu `.sims3pack`/`.sims2pack`.
- Leere (0 Byte) und abgeschnittene Dateien, Packages ohne Ressourcen.
- Überflüssiges im Mods-Ordner: `.txt`, `.jpg`, `.url`, `Thumbs.db`, leere Ordner.
  Standard: in einen Unterordner verschieben, Löschen als Option.

**Aufwand:** gering · **Nutzen:** hoch

### A6 Ordnerregeln & Dateinamen *(ersetzt: Troubleshooting-Checklisten)*
- `.ts4script` tiefer als **1 Unterordner** → wird nicht geladen (wichtigste Ordnerregel der Community).
- `.package` tiefer als **5 Unterordner** → wird nicht geladen.
- Windows-Pfadlänge über 260 Zeichen, Sonderzeichen in Dateinamen (die Foren empfehlen, sie zu
  entfernen).
- `Resource.cfg` fehlt oder ist verändert → mit Standardinhalt neu erzeugen.
- Jeweils mit „Automatisch reparieren“: Dateien nach oben verschieben bzw. umbenennen, über das Journal.

**Aufwand:** gering · **Nutzen:** hoch

### A7 OneDrive-Prüfung
**Problem:** Viele Beiträge in den EA-Foren drehen sich um OneDrive. Das Spiel liest nur die lokale Kopie;
„Dateien bei Bedarf“ lässt Mods als Platzhalter in der Cloud liegen.

**Funktion:** Erkennen, ob der Spielordner in OneDrive liegt. Nur-Online-Platzhalter über die Attribute
`RECALL_ON_DATA_ACCESS`/`OFFLINE` zählen. Angebot „Auf diesem Gerät behalten“ bzw. eine Anleitung zum
Verschieben. Warnung, wenn OneDrive voll ist.
**Aufwand:** gering · **Nutzen:** mittel–hoch

### A8 Spiel läuft? + Cache leeren
- Läuft `TS4_x64.exe`, werden Aktionen, die Dateien ändern, gesperrt oder es wird gewarnt (sonst schlägt
  das Umbenennen fehl).
- **Cache leeren:** `localthumbcache.package`, `cachestr`, `onlinethumbnailcache` – der
  Standard-Handgriff nach jeder Mod-Änderung. Automatischer Hinweis, wenn Mods seit der letzten Leerung
  geändert wurden. Optional: vor jedem Spielstart automatisch.

**Aufwand:** gering · **Nutzen:** hoch

---

## B. CC-Verwaltung

### B1 CC-Katalog mit Vorschaubildern *(ersetzt: CurseForge-Ansicht, S4S-Suche)*
**Problem:** Mod-Listen zeigen nur Dateinamen wie `yooniesimImperfectionTeethSetMergedRandom`.

**Funktion:** Vorschaubilder direkt aus den Packages. CAS-Thumbnails (`0x3C1AF1F2`) und
Kaufmodus-Thumbnails (`0x3C2A8647`, `0x5B282D45`) sind JPEG/PNG mit Alpha-Block, also decodierbar – anders
als die verschlüsselten Tray-Bilder. Dazu Namen aus CAS-Teilen, Katalogeinträgen und Stringtabellen.
Kachelansicht mit Filtern (Haare, Kleidung, Make-up, Möbel, Bau …) sowie Alter und Geschlecht aus den
CAS-Flags.
**Aufwand:** mittel–hoch · **Nutzen:** sehr hoch

### B2 Automatische Kategorisierung & Ordner-Sortierung *(ersetzt: manuelles Sortieren, „Mod Manager“)*
- Jede Datei nach Inhalt einordnen: CAS, Bau/Kauf, Gameplay-Tuning, Skript, Slider/Presets, Posen,
  Default Replacement.
- Ersteller aus Namenskonventionen (`[Creator]`, `Creator_`) und Package-Metadaten.
- „Mods-Ordner sortieren“: Vorschau der Zielstruktur (z. B. `Mods\CAS\Haare\<Ersteller>`), beachtet die
  Tiefenregeln aus A6, Ausführung über das Journal.

**Aufwand:** mittel · **Nutzen:** hoch

### B3 Fehlende Meshes & verwaiste Recolors *(ersetzt: S4S-Suche, Tray-Importer-Tricks)*
**Problem:** In den Foren ein Klassiker: Recolor installiert, Mesh fehlt → unsichtbare CC. CAS-Teile
verweisen per Ressourcenschlüssel auf ihre Geometrie; Objekt-Recolors auf das Modell.

**Funktion:** Für jedes Recolor prüfen, ob das referenzierte Mesh in den Mods oder im Spiel vorhanden ist
(Letzteres braucht C1). Anzeige „Recolor ohne Mesh“ mit dem gesuchten Mesh-Namen.
**Aufwand:** mittel · **Nutzen:** hoch

### B4 Abhängigkeiten *(ersetzt: Mod Hound „Missing requirements“, CurseForge-Abhängigkeiten)*
- Bekannte Bibliotheken erkennen, die andere Mods voraussetzen (S4CL, XML Injector, Lot 51 Core Library …),
  anhand von Modulnamen in `.ts4script` und Tuning-Verweisen.
- „Mod X braucht Y, Y fehlt/ist deaktiviert“. Beim Deaktivieren einer Bibliothek warnen, welche Mods davon
  abhängen.

**Aufwand:** mittel · **Nutzen:** hoch

### B5 Merge / Unmerge *(ersetzt: Sims 4 Studio „Merge Packages“)*
- Viele kleine CC-Dateien zu einem Package zusammenführen. Die Community-Grenzen (max. ~500 Dateien bzw.
  2 GB, lieber kleinere Pakete) werden eingehalten.
- **Unmerge:** Das S4S-Merge-Manifest (`0x7FB6AD8A`) listet die Originaldateien → gemergte Packages
  wieder zerlegen, z. B. um einen Teil zu entfernen.
- Hinweis aus den Foren: Merging verkürzt Ladezeiten, macht das Spiel aber nicht schneller.
  Das wird ehrlich so kommuniziert.

**Technik:** Der vorhandene DBPF-Writer wird von „ohne“ auf „aus mehreren“ erweitert.
**Aufwand:** mittel · **Nutzen:** mittel

### B6 Package-Werkzeuge *(Teilersatz für S4S-Batch-Fixes)*
- Doppelte Ressourcen innerhalb eines Packages entfernen.
- Unkomprimierte Ressourcen komprimieren (spart Speicher).
- Leere oder nur aus Metadaten bestehende Packages entfernen.
- Inhaltsansicht eines Packages (Ressourcenliste, Typen, Größen) für Fortgeschrittene.

**Aufwand:** mittel · **Nutzen:** mittel

### B7 Speicherplatz-Analyse
Größte Mods, Verteilung nach Kategorie und Ersteller, verschwendeter Platz durch Duplikate und
unkomprimierte Daten. Darstellung als Kachel- bzw. Treemap-Ansicht.
**Aufwand:** gering · **Nutzen:** mittel

### B8 Notizen, Tags & Herkunft *(ersetzt: „Mod Manager“-Notizen, Excel-Listen)*
Pro Mod: Notiz, eigene Tags, Download-Link, Ersteller-Link, „Warum installiert?“.
Speicherung außerhalb des Mods-Ordners, gebunden an die Datei-ID.
**Aufwand:** gering · **Nutzen:** mittel

---

## C. Spiel-Integration

### C1 Spielinstallation & Basisspiel-Index *(Grundlage für mehrere Funktionen)*
- Installationsordner über Registry, EA App oder Steam finden, dazu installierte Erweiterungen
  (`EP*`, `GP*`, `SP*`, `FP*`).
- Die Ressourcen-Indizes der Spiel-Packages einlesen (nur Indizes, wie bei den Mods) und cachen.

**Ermöglicht:**
- **Default Replacements und Tuning-Overrides erkennen:** Mods, die Spielressourcen ersetzen – das größte
  Risiko nach Patches (A3).
- **Fehlende Meshes** zuverlässig prüfen (B3).
- **Fehlender CC in Tray-Einträgen:** Referenzen, die weder in Mods noch im Spiel existieren, gehören zu
  CC, der fehlt. Das schließt die heutige Lücke der Bibliothek.
- **Benötigte Erweiterungen** für Tray-Einträge und CC. Der Tray Importer zeigt das an.

**Aufwand:** hoch · **Nutzen:** sehr hoch

### C2 Spielstände *(neu, kein Tool macht das bequem)*
- Spielstände (`saves\Slot_*.save`, ebenfalls DBPF) mit Datum, Größe und Haushaltsnamen auflisten.
- **Welchen CC nutzt ein Spielstand?** Gleiche Technik wie bei der Tray-CC-Erkennung.
- **Warnung vor dem Deaktivieren:** „Dieser Mod wird im Spielstand ‚Familie Müller‘ verwendet.“
- Einzelne Spielstände sichern, wiederherstellen und die Versionen `.ver0`–`.ver4` verwalten.

**Aufwand:** mittel · **Nutzen:** hoch

### C3 Spielen mit Profil
Knopf „Spielen“: Profil anwenden → Cache leeren (A8) → Spiel starten (EA App/Steam/exe). Nach Spielende
automatisch neue Fehlerprotokolle auswerten (A1) und optional die Saves sichern.
**Aufwand:** gering–mittel · **Nutzen:** hoch

---

## D. Downloads, Updates & Sicherheit

### D1 Download-Ordner überwachen
Neue `.package`/`.ts4script`/Archive im Windows-Download-Ordner → Benachrichtigung „In Sims 4
installieren?“ mit dem vorhandenen Installer (Vorschau, Duplikatprüfung, Journal).
**Aufwand:** gering · **Nutzen:** hoch

### D2 Update-Prüfung *(ersetzt teilweise CurseForge-App, Mod Hound)*
- **CurseForge:** Die offizielle API (mit Schlüssel) bietet Versionen, Abhängigkeiten und Downloads. Mods
  lassen sich über Datei-Fingerprints erkennen, Updates mit einem Klick installieren.
- **Andere Quellen** (Patreon, Tumblr, eigene Seiten): kein automatischer Abgleich möglich. Stattdessen
  der hinterlegte Download-Link (B8) und die Patch-Warnung (A3).
- **Mod-Liste exportieren** (Text/CSV/HTML) für Mod Hound, Discord-Support oder Freunde.

**Aufwand:** hoch (CurseForge) / gering (Export) · **Nutzen:** hoch

### D3 Malware-Schutz für Skript-Mods *(ersetzt: ModGuard von TwistedMexi)*
Nach bekannten Vorfällen mit Schadcode in Mods hat TwistedMexi ModGuard veröffentlicht. Wir prüfen
`.ts4script`-Archive statisch auf verdächtige Muster:
- `subprocess`, `os.system`, `ctypes`, Netzwerkzugriffe (`urllib`, `socket`, `requests`).
- `exec`/`eval` auf dekodierten Daten, eingebettete `.exe`/`.dll`.

Ergebnis ist eine Warnung, kein Urteil: Manche legitimen Mods (z. B. mit Update-Check) greifen aufs Netz
zu. Installierte Skripte werden bei der Installation und beim Scan geprüft.
**Aufwand:** mittel · **Nutzen:** hoch

---

## E. Komfort

- **E1 Änderungsvergleich:** „Was hat sich seit gestern/seit dem letzten Spielen geändert?“
  (neue, entfernte und geänderte Dateien) – aus Snapshots und Journal.
- **E2 Globale Suche** über Mods, CC-Namen, Bibliothek und Spielstände.
- **E3 Automatische Sicherung** von Saves und Tray vor jedem Spielstart bzw. nach Zeitplan, mit
  Aufbewahrungsregeln.
- **E4 Englische Oberfläche**, damit das Tool in internationalen Foren und Discords weitergegeben werden
  kann.
- **E5 Einrichtungsassistent** beim ersten Start: Ordner prüfen (inkl. OneDrive), Mods in `Options.ini`
  aktivieren, erste Sicherung, Überblick über Probleme.
- **E6 „Gesundheitscheck“-Übersicht:** eine Startseite, die alle Prüfungen (A1–A8, B3, B4, D3)
  zusammenfasst: „3 Probleme, 2 automatisch behebbar“.

---

## Priorisierung

**Stufe 1 – schnelle Gewinne (je gering, hoher Nutzen):** ✅ umgesetzt (Tab „Übersicht“)
A4 Options.ini · A5 falsche/kaputte Dateien · A6 Ordnerregeln · A7 OneDrive · A8 Spiel läuft + Cache ·
A3 Patch-Erkennung · D2 Export der Mod-Liste · E6 Gesundheitscheck als Rahmen

**Stufe 2 – die großen Alltagshelfer:** ✅ umgesetzt (Tabs „Diagnose“, „Spielstände“, Button „Spielen“)
A1 Fehlerprotokoll-Analyse · A2 50/50-Assistent · C3 Spielen mit Profil · D1 Download-Überwachung ·
C2 Spielstände · B4 Abhängigkeiten · D3 Malware-Schutz

**Stufe 3 – Katalog & Tiefe:** ✅ umgesetzt (Tabs „Katalog“, „Speicherplatz“, „Sortieren …“, Notizen, „Was hat sich geändert?“, globale Suche, automatische Sicherung, Einrichtungsassistent, englische Oberfläche)
B1 CC-Katalog mit Vorschaubildern · B2 Sortierung · B7 Speicherplatz · B8 Notizen · E1–E5

**Stufe 4 – Spielindex & Online:** ✅ umgesetzt (Tabs „Spiel“ und „Updates“, „Zusammenführen …“, Mod-Details → „Inhalt“, benötigte Packs und fehlender CC in der Bibliothek)
C1 Basisspiel-Index (danach B3, fehlender Tray-CC, Default Replacements) · B5 Merge/Unmerge ·
B6 Package-Werkzeuge · D2 CurseForge-Anbindung
*Grenzen:* Spiel-Tuning liegt seit einigen Patches als Binärblock vor – Tuning-Overrides werden daher per
SimData-Schlüssel und EA-typischer ID/Name erkannt (Schätzung). Fehlender CC wird nur bei Haushalten geprüft;
Grundstücke und Räume speichert das Spiel komprimiert (dort nur „mindestens“-Angaben zu Packs).
CurseForge braucht einen eigenen API-Schlüssel des Nutzers.

---

## Bewusst nicht geplant

- **CC selbst erstellen** (Meshes bearbeiten, Recolors anlegen, Texturen malen): Das bleibt Domäne von
  Sims 4 Studio und Blender. Ein Nachbau wäre riesig und für Spieler, nicht Ersteller, kaum nötig.
- **Vollautomatische Fehlersuche ohne Spieler:** Die Foren betonen zu Recht, dass kein Tool sieht, was im
  Spiel passiert. Deshalb der geführte 50/50-Assistent statt Scheinsicherheit.
- **Vorschaubilder der Bibliothek:** Das Format der Tray-Bilder ist verschlüsselt und undokumentiert.

---

## Quellen

- [SnootySims – Alternativen zum Mod Conflict Detector](https://snootysims.com/wiki/sims-4/the-sims-4-mod-conflict-detector/)
- [Pro Game Guides – Mod Conflict Detectors](https://progameguides.com/sims-4/sims-4-best-mod-conflict-detectors/)
- [Mod The Sims – Mod Conflict Detector](https://modthesims.info/d/561550/mod-conflict-detector-update-03-03-2018.html)
- [Simularity – Konflikte finden](https://simularity.cc/articles/finding-conflicts/) · [Kaputten CC mit dem Tray Importer finden](https://simularity.cc/articles/find-broken-cc/)
- [Lumpinou – TS4 Mod Hound](https://lumpinoumods.com/2024/11/06/ts4-mod-hound-mod-checker-tracker-for-mod-users/)
- [SnootySims – Better Exceptions](https://snootysims.com/wiki/sims-4/better-exceptions-by-twisted-mexi/)
- [GGRecon – Last Exception und 50/50-Methode](https://www.ggrecon.com/guides/sims4-how-to-fix-last-exception-error/)
- [Sacrificial Mods – Last-Exception-Berichte verstehen](https://sacrificialmods.com/understanding-last-exception-error-reports.html) · [MCCC Troubleshooting](https://deaderpool-mccc.com/troubleshooting.html)
- [EA Forums – Broken and Updated Mods, Patch 1.127](https://forums.ea.com/discussions/the-sims-4-mods-and-custom-content-en/old-broken-and-updated-sims-4-mods-and-cc-patch-1-127-august-25-2026/13670124)
- [Sims 4 Studio – Batch Fix Information](https://sims4studio.com/thread/37124/batch-fix-information) · [Merge Packages](https://sims4studio.com/thread/402/merge-packages-using-sims-studio?page=10)
- [EA Forums – Does merging packages make Sims run better?](https://forums.ea.com/discussions/the-sims-4-general-discussion-en/does-merging-packages-make-sims-run-better/305339) · [Steam-Guide Merging](https://steamcommunity.com/sharedfiles/filedetails/?id=2153837001)
- [Sims After Dark – Mods-Ordner organisieren](https://simsafterdark.com/diy-troubleshooting-library/how-to-organize-your-mods-folder/) · [Kemzima – Ordnerregeln](https://kemzimamods.com/how-to-organise-your-sims-4-mods-folder/)
- [EA Forums – CC erscheint nicht (localthumbcache)](https://forums.ea.com/discussions/the-sims-4-mods-and-custom-content-en/re-cc-not-showing-up-in-game/11752299)
- [EA Forums – Mods funktionieren nicht mit OneDrive](https://forums.ea.com/discussions/the-sims-4-technical-issues-pc-en/why-doesnt-mods-work-in-onedrive-sims4/11777696)
- [Mod The Sims – TS3-CC im TS4-Ordner](https://modthesims.info/t/541781) · [TheSimsTree – Fehlende Meshes](https://thesimstree.com/en/blog/the-sims-tips/sims-4-mesh-guide-why-cc-disappears-and-how-to-fix-missing-meshes.html)
- [ModFix](https://missyai87.itch.io/modfix) · [Sims 4 Mod Folder Cleaner](https://squishy-taco.itch.io/sims-4-mod-folder-cleaner-pc-tool)
- [SnootySims – CurseForge Mod Manager](https://snootysims.com/news/curseforge-mod-manager-for-the-sims-4/) · [TwistedMexi (ModGuard)](https://twistedmexi.com/)
