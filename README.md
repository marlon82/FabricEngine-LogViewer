# LogViewer 2.0

WPF-Neuentwicklung des LogViewer 1.60 für VOSS/ERS-Netzwerklogs.

## Funktionen

- Alte und neue VOSS-Logformate, ERS-Logs und Trace-Zeilen
- Mehrere Dateien, Drag-and-drop (Strg halten zum Anhängen)
- Sofortsuche und Filter nach Modul, Schweregrad und Kategorie
- Sortierung, Detailansicht, Statistik und CSV-/Text-Export
- Deutsch und Englisch zur Laufzeit umschaltbar
- Eventkataloge (`.evdb`) aus GitHub-Releases herunterladen und aktualisieren

## Eventkataloge auf GitHub bereitstellen

Im Repository `marlon82/FabricEngine-LogViewer` einen öffentlichen GitHub-Release anlegen und die vorgepackten `.evdb`-Dateien als Release-Assets anhängen. Der LogViewer liest die Assets aller veröffentlichten Releases über die GitHub-API; ein GitHub-Konto oder Zugriffstoken ist für öffentliche Releases nicht nötig.

Im LogViewer steht der Abruf unter **Event-Dokumentation verwalten > Kataloge von GitHub aktualisieren …** zur Verfügung. Vor dem Download werden Anzahl, Gesamtgröße, Dateinamen und Zielordner angezeigt. Bereits vorhandene Dateien gleichen Namens werden nach erfolgreicher Validierung aktualisiert.

## Programmaktualisierung

Über **Update > Jetzt nach Updates suchen …** prüft der LogViewer die GitHub-Releases. Standardmäßig werden nur stabile Releases berücksichtigt. Die Option **Pre-Releases einbeziehen** aktiviert zusätzlich Vorschauversionen und wird lokal gespeichert. Nach Zustimmung lädt die Anwendung die neue Einzeldatei-EXE herunter, beendet sich, ersetzt die bisherige EXE und startet neu.

Die Release-Automatisierung und die Verwendung des `prerelease`-Zweigs sind in [RELEASING.md](RELEASING.md) beschrieben.

## Build

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
