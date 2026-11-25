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
4. **Installer** – `artifacts\installer\DXMirror-Setup-0.1.0-x64.exe`

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

1. Version in `src/DXMirror.App/DXMirror.App.csproj` (`<Version>`) und `app.manifest` erhöhen.
2. `CHANGELOG.md` ergänzen.
3. `build\build.cmd` → `artifacts\installer\DXMirror-Setup-<Version>-x64.exe`.
4. Installer auf einem sauberen Windows (VM ohne Entwicklertools) nach [TESTING.md](TESTING.md) prüfen.
5. GPL-Pflicht: Quellcode dieses Repositorys inkl. Patch zusammen mit dem Installer bereitstellen.

## Stand dieses Builds

Der Code wurde in einer Linux-Umgebung ohne .NET-SDK und ohne Windows-Toolchain geschrieben.
**Er wurde dort nicht kompiliert.** Geprüft wurde nur, dass der UxPlay-Patch sich sauber auf den gepinnten
Commit anwenden lässt. Beim ersten Build auf Windows sind kleinere Compile-Fehler möglich – bitte die
Ausgabe von `build\build.cmd` zurückmelden.
