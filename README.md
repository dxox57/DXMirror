# DXMirror – AirPlay Receiver for Windows

DXMirror macht einen Windows-PC zu einem AirPlay-Empfänger im lokalen Netzwerk:
iPhone-Bildschirmsynchronisierung mit gleichzeitigem Ton sowie reines AirPlay-Audio (z. B. aus der Musik-App).
Auf dem iPhone ist keine zusätzliche App nötig, es gibt keine Cloud und kein Konto.

> **Aktuelle Version: 0.2.0.** DXMirror wurde mit einem iPhone 16 Pro getestet. Andere iPhone-Modelle
> und iOS-Versionen sind noch nicht geprüft; bekannte Einschränkungen und Testergebnisse stehen in
> [TESTING.md](TESTING.md).

## Download

Die aktuelle Windows-64-Bit-Version gibt es auf der [GitHub-Release-Seite](../../releases/latest).
Lade dort `DXMirror-Setup-0.2.0-x64.exe` herunter. Der Quellcode und die Lizenzhinweise liegen
ebenfalls im Release und in diesem Repository.

## Funktionen

| Bereich | Umsetzung |
|---|---|
| Bildschirmsynchronisierung | über [UxPlay](https://github.com/FDH2/UxPlay) 1.74 (AirPlay-2-Mirroring, H.264, optional H.265) |
| Ton beim Spiegeln | AAC-ELD, zeitstempelbasiert mit dem Bild synchronisiert (UxPlay `-vsync`) |
| AirPlay-Audio ohne Bild | ALAC/AAC über denselben Empfänger, optional reiner Audio-Modus |
| Erkennung durch das iPhone | Apple Bonjour (muss installiert sein; DXMirror warnt und verlinkt den Download) |
| Aufnahme | MP4 mit Bild und Ton (AAC), Knopf in der Übersicht, Ordner wählbar |
| Video-Decodierung | GStreamer `decodebin` → Direct3D-11-Hardwaredecoder, Software-Fallback `avdec_h264` |
| Darstellung | Direct3D 11/12 direkt in das DXMirror-Fenster, Vollbild (F11 / Esc) |
| Audio-Ausgabe | WASAPI, wählbares Ausgabegerät, Lautstärke/Stumm nur für DXMirror |
| Bedienung | Windows-11-Stil, Hell/Dunkel, Deutsch/Englisch, Infobereich (Tray), Autostart |
| Sicherheit | nur lokal, optionaler PIN, Firewall-Regeln nur für private Netzwerke, keine Telemetrie |
| Diagnose | Protokolle mit Schwärzung von PIN/Schlüsseln, Ressourcenanzeige, Diagnose-ZIP |

## Systemanforderungen

- Windows 10 (1809 oder neuer) oder Windows 11, 64 Bit
- Grafikkarte mit Direct3D 11 (für Hardware-Decodierung; sonst Software-Decodierung)
- PC und iPhone im selben WLAN/LAN, Netzwerkprofil **Privat**, kein Gast-WLAN mit Client-Isolation
- iPhone mit iOS, das AirPlay-Bildschirmsynchronisierung anbietet (welche Versionen tatsächlich
  funktionieren, wird in [TESTING.md](TESTING.md) nach echten Tests eingetragen)

## Installation

1. `DXMirror-Setup-<Version>-x64.exe` ausführen (Ergebnis von `build\build.cmd`, siehe [BUILD.md](BUILD.md)).
2. Im Setup die Aufgabe **„Windows-Firewall-Regeln anlegen“** aktiviert lassen. Die Regeln gelten nur für
   den mitgelieferten Receiver und nur in privaten Netzwerken.
3. DXMirror starten. Eine .NET- oder GStreamer-Installation ist nicht nötig, alles ist enthalten.

## Bedienung

1. DXMirror starten – der Empfänger startet automatisch (abschaltbar).
2. Auf dem iPhone: **Kontrollzentrum → Bildschirmsynchronisierung → „DXMirror – <PC-Name>“**.
3. Ist der PIN-Schutz aktiv, zeigt DXMirror einen 4-stelligen Code, den du auf dem iPhone eingibst.
4. Bild und Ton erscheinen in der Übersicht. **F11** oder „Vollbild“ für Vollbild, **Esc** zurück.
5. „Verbindung trennen“ beendet die Sitzung, „Receiver stoppen“ schaltet DXMirror unsichtbar.

Musik ohne Bild: In der Musik-App (oder im Kontrollzentrum beim Audio-Player) das AirPlay-Symbol wählen
und DXMirror als Lautsprecher auswählen.

Schließen des Fensters lässt DXMirror standardmäßig im Infobereich weiterlaufen; beenden über das
Tray-Menü → „Beenden“.

## Wenn das iPhone DXMirror nicht findet

- **Diagnose → Firewall-Regeln einrichten** (UAC-Abfrage) und Netzwerkprofil prüfen (muss „Private“ sein).
- iPhone und PC im selben Netz? Kein Gast-WLAN, kein VPN auf dem iPhone, kein „AP/Client Isolation“.
- Mehrere Netzwerkadapter (VPN, Hyper-V, VirtualBox): unter **Einstellungen → Verbindung** den
  WLAN- oder LAN-Adapter fest auswählen.
- Läuft ein anderer AirPlay-Empfänger (z. B. LetsView, AirServer)? Dann sind die Ports belegt.
- Zeigt die Übersicht eine gelbe Warnung („Die Erkennung wird blockiert von: …“), das genannte Programm
  beenden (z. B. TeamViewer belegt den Erkennungs-Port 5353).

## Lizenz

DXMirror steht unter der **GNU GPL v3** ([LICENSE](LICENSE)), weil es UxPlay (GPL-3.0) als Receiver-Kern
verwendet und mitliefert. Die Lizenztexte aller mitgelieferten Komponenten liegen im Installationsordner
unter `receiver\licenses`. Die DXMirror-Änderungen an UxPlay stehen in
`third_party/uxplay/dxmirror-uxplay.patch`.

**Hinweis zu FairPlay:** Apple verlangt für die Bildschirmsynchronisierung einen FairPlay-Schlüsselaustausch.
UxPlay enthält dafür eine nachgebaute Implementierung („playfair“), die es seit Jahren in vielen
Open-Source-Receivern gibt. DXMirror nutzt sie unverändert und nur für den Verbindungsaufbau der
Bildschirmsynchronisierung; DRM-geschützte Video-Inhalte (z. B. aus Streaming-Apps, die AirPlay mit
FairPlay-Video schützen) werden nicht entschlüsselt. Ob diese Nutzung in deinem Land und für deinen
Zweck (privat/kommerziell) zulässig ist, musst du selbst prüfen – das ist keine Rechtsberatung.

„AirPlay“, „iPhone“ und „iOS“ sind Marken von Apple Inc. DXMirror ist kein Apple-Produkt und wird von
Apple weder unterstützt noch zertifiziert.

