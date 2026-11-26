# Build

## Voraussetzungen (nur auf dem Build-PC)

| Werkzeug | Zweck | Installation |
|---|---|---|
| .NET 10 SDK | App, Tests | `winget install Microsoft.DotNet.SDK.10` |
| MSYS2 (Pfad `C:\msys64`) | UxPlay + GStreamer bauen | `winget install MSYS2.MSYS2` |
| Inno Setup 6 | Installer | `winget install JRSoftware.InnoSetup` |
| Internet | pacman-Pakete, UxPlay-Quellcode (GitHub), NuGet | – |

Alles auf einmal: `powershell -ExecutionPolicy Bypass -File build\setup-prerequisites.ps1`

## Kompletter Build

```
build\build.cmd
```

bzw. `powershell -ExecutionPolicy Bypass -File build\build.ps1`. Ablauf:

1. **Receiver** – `build/build-uxplay.sh` in MSYS2 UCRT64:
   pacman-Abhängigkeiten → UxPlay klonen (fester Commit `fbd040e`, Version 1.74) → DXMirror-Patch anwenden →
   CMake/Ninja → `uxplay.exe` + benötigte GStreamer-Plugins + alle DLL-Abhängigkeiten nach
   `artifacts\receiver\` → Lizenztexte → **Selbsttest**: prüft mit `gst-inspect-1.0` bei reinem
   Windows-`PATH`, dass alle benötigten Elemente aus dem Bundle ladbar sind.
2. **Tests** – `dotnet test tests\DXMirror.Core.Tests`
3. **App** – `dotnet publish` (self-contained, win-x64, ReadyToRun) nach `artifacts\publish\`
   (inkl. `receiver\`). Dieser Ordner ist bereits eine lauffähige portable Version.
4. **Installer** – `artifacts\installer\DXMirror-Setup-0.3.1-x64.exe`. Installiert pro Benutzer nach
   `%LOCALAPPDATA%\Programs\DXMirror` ohne Administratorrechte; nur die Firewall-Regeln werden über eine
   Windows-Abfrage für `cmd /c netsh …` angelegt (`DXMirror.exe --setup-firewall`).

Optionen: `-SkipReceiver` (vorhandenes `artifacts\receiver` nutzen), `-SkipTests`, `-SkipInstaller`,
`-Msys2Root D:\msys64`.

## Entwickeln in Visual Studio / Rider

`DXMirror.slnx` öffnen (VS 2022 17.14+ oder VS 2026). Ist `artifacts\receiver` vorhanden, wird es beim
Build automatisch nach `bin\…\receiver` kopiert, sodass F5 direkt mit echtem Receiver startet.

Nur Receiver neu bauen (z. B. nach Patch-Änderung), im „MSYS2 UCRT64“-Terminal:

```
cd /c/Users/<du>/Documents/DXMirror
./build/build-uxplay.sh
```

## UxPlay aktualisieren

1. `UXPLAY_COMMIT` in `build/build-uxplay.sh` ändern.
2. Patch neu erzeugen: in `build/work/UxPlay` Änderungen nachziehen, `git diff > ../../third_party/uxplay/dxmirror-uxplay.patch`.
3. `uxplay -h` der neuen Version mit den in `UxPlayStartupFile` verwendeten Optionen vergleichen.

## Release erstellen

1. Bei einer neuen Version `<Version>` in der App-Projektdatei und den Changelog aktualisieren.
2. Einen GitHub-Release mit Tag `vX.Y.Z` veröffentlichen. Der Tag muss auf den gewünschten Commit mit der passenden Projektversion und dem Workflow zeigen.
3. Nach erfolgreichem Windows-Build, Tests und CVE-Scan erscheinen beim Release die Installer-EXE, `SHA256.txt`, Quellarchive für DXMirror inklusive Patch und den gepinnten UxPlay-Stand, Lizenztext und native SBOM. Der Build und ein erneuter Lauf sind unter Actions einsehbar.
4. Installer auf einem sauberen Windows mit echtem AirPlay-Gerät nach [TESTING.md](TESTING.md) prüfen.

Der Upload setzt einen Release voraus, dessen Assets nach Veröffentlichung bearbeitet werden dürfen.
Bei einem Upload-Fehler den fehlgeschlagenen Job erneut ausführen. Vorhandene Dateien mit gleichem
SHA-256 bleiben erhalten. Bei abweichendem SHA-256 meldet der Job einen Konflikt.

Für den Release `v0.3.1` werden hohe und kritische Grype-Befunde im Report und als Warnung
angezeigt. Nur beim Upload zu diesem Release blockieren diese Befunde den Job nicht.
Scannerfehler und die übrigen Prüfungen können den Upload weiterhin blockieren.

## Sicherheitsprüfungen in GitHub Actions

- Pull Requests und Pushes auf `main` bauen den Installer und prüfen NuGet sowie die
  mitgelieferten nativen Komponenten. NuGet blockiert ab mittel. Der native Scan blockiert
  nur hohe und kritische Befunde und zeigt mittlere Befunde weiterhin im Bericht.
- Dependency Review prüft neue Abhängigkeiten in Pull Requests. CodeQL meldet mögliche Probleme
  in C# und GitHub-Actions-Workflows im Security-Tab. Dependabot öffnet Update-PRs für NuGet und Actions.
- Die native SBOM enthält das Bundling-Inventar aus MSYS2 sowie Version und Commit von UxPlay.
  Grype prüft anhand der verfügbaren Paketkennungen. Für UxPlay werden zusätzlich die veröffentlichten
  Projekt-Advisories geprüft. Fehlende Paketkennungen werden im Build gemeldet.
- Wöchentlich laufen der Build und eine erneute Prüfung der nativen SBOM aus dem letzten stabilen
  Release. Bis zum ersten Release mit SBOM meldet der Lauf, dass dieses Artefakt noch fehlt.
- Der Windows-Workflow kann manuell zur Vorschau gestartet werden. Der Installer erscheint dabei
  als Actions-Artefakt. Der Upload läuft beim Veröffentlichen eines GitHub-Releases.

Für Dependency Review muss ein Repository-Admin unter **Settings → Advanced Security**
den **Dependency graph** aktivieren. GitHub kann den Check ohne diese Einstellung mit HTTP 403 ablehnen.
