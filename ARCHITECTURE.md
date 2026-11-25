# Architektur

## Entscheidung: UxPlay als Receiver-Kern

Das Projektziel verlangt echte iPhone-Bildschirmsynchronisierung. Die Machbarkeitsanalyse (Nov. 2026):

| Projekt | Mirroring | Windows | Lizenz | Bewertung |
|---|---|---|---|---|
| **UxPlay** (FDH2/UxPlay, 1.74, aktiv gepflegt, letzter Commit 23.11.2026) | ja (H.264/H.265, Audio) | ja, offiziell (MSYS2/MinGW, getestet auf Win 10/11) | GPL-3.0 | **gewählt** |
| RPiPlay | ja | nein (Raspberry Pi/Linux) | GPL-3.0 | Vorgänger von UxPlay, nicht mehr aktiv |
| shairport-sync | nein, nur Audio | nein | MIT-artig | erfüllt Kernziel nicht |
| Eigenentwicklung | – | – | – | Protokoll undokumentiert, FairPlay-Handshake nötig: unverhältnismäßig |

UxPlay erfüllt als einziges gepflegtes Projekt alle Kernanforderungen und läuft nativ unter Windows.
Seit 1.74 bringt es einen eigenen mDNS-Responder mit, Apples Bonjour-Dienst ist nicht mehr nötig.
Konsequenz: DXMirror ist GPL-3.0.

DXMirror bindet UxPlay **als eigenen Prozess** ein, nicht als Bibliothek:

- Ein Absturz im Streaming (Decoder, Treiber, Netzwerkpaket) beendet nur den Receiver-Prozess; die UI
  bleibt bedienbar und startet den Receiver mit Back-off neu.
- Klare Lizenz- und Build-Grenze: UxPlay + GStreamer werden mit MSYS2/MinGW gebaut, die UI mit .NET.
- Ein Windows-Job-Objekt (`KILL_ON_JOB_CLOSE`) stellt sicher, dass kein verwaister `uxplay.exe` die Ports
  blockiert, falls DXMirror abstürzt.

## Komponenten

```
┌───────────────────────────── DXMirror.exe (.NET 10, WPF) ─────────────────────────────┐
│ Desktop UI            Views/, ViewModels/, Themes/, Localization/, Services/TrayIcon     │
│   Dashboard · Einstellungen · Diagnose · Tray · Vollbild · Benachrichtigungen            │
│ App-Dienste           Services/AppHost (Komposition), Firewall, Autostart, SingleInstance │
│ Plattform             Platform/AudioEndpoints (WASAPI-Sessions), ChildProcessJob, DPAPI   │
│ Video-Host            Controls/VideoHost (HwndHost, natives Kindfenster)                  │
├──────────────────────────── DXMirror.Core (.NET 10, plattformneutral) ──────────────────┤
│ Receiver Core         ReceiverController (Prozess, Neustart, Events) · ReceiverSession    │
│                       (Zustandsautomat) · UxPlayStartupFile (Optionen) · Capabilities      │
│ Network Manager       NetworkAdapterService (Erkennung, Klassifizierung, Auswahl, Events) │
│ Logging/Diagnostics   AppLog (asynchron, rotierend, geschwärzt) · ResourceMonitor ·        │
│                       DiagnosticsReport (ZIP) · LogRedactor                                │
│ Settings              AppSettings (validiert) · SettingsStore (atomar, PIN per DPAPI)     │
└───────────────┬─────────────────────────────────────────────▲──────────────────────────┘
   -rc uxplay.rc│ ENV: DXMIRROR_EVENTS/CONTROL/HWND/MDNS_IPV4  │ stdout "@@DXM <event> k=v"
   stdin "quit" ▼ GST_PLUGIN_* (nur gebündelte Plugins)        │
┌──────────────────── receiver\bin\uxplay.exe (UxPlay 1.74 + DXMirror-Patch) ─────────────┐
│ AirPlay-Protokoll (RTSP/HTTP, Pairing, FairPlay-Setup, RTP-Mirror, RAOP-Audio, NTP)      │
│ mDNS-Responder (_airplay._tcp, _raop._tcp)                                               │
│ GStreamer: appsrc → h264parse → decodebin (d3d11h264dec | avdec_h264) → d3d11videosink ──┼─► HWND im DXMirror-Fenster
│            appsrc → avdec_aac/avdec_alac → audioconvert → audioresample → volume → wasapisink ─► Windows-Audio
└──────────────────────────────────────────────────────────────────────────────────────────┘
```

## Schnittstelle DXMirror ↔ UxPlay

Der Patch `third_party/uxplay/dxmirror-uxplay.patch` (≈90 Zeilen, nur aktiv wenn die Umgebungsvariablen
gesetzt sind) ergänzt UxPlay um:

| Mechanismus | Zweck |
|---|---|
| `DXMIRROR_EVENTS=1` → Zeilen `@@DXM …` auf stdout | `started`, `client_request name= model=`, `client_denied`, `pin code=`, `conn_open/close count=`, `conn_reset reason=`, `video_size codec= w= h=`, `audio_format ct= screen= media=`, `stats fps= kbps= missed=` (1×/s), `stopping` |
| `DXMIRROR_CONTROL=1` → stdin | Zeile `quit` oder geschlossenes stdin ⇒ gleicher Pfad wie Strg+C (sauberes Beenden) |
| `DXMIRROR_HWND=<handle>` | ruft das in UxPlay vorhandene `video_renderer_set_window_handle()` auf ⇒ GStreamer rendert per `GstVideoOverlay` in das DXMirror-Kindfenster |
| `DXMIRROR_MDNS_IPV4=<ip>` | mDNS-Ankündigung auf dem vom Nutzer gewählten Adapter |
| PIN | wird bei aktivem DXMirror nicht als ASCII-Grafik in die Konsole geschrieben (Logs bleiben frei von Auth-Daten) |

Alle Optionen werden über eine UTF-8-Startdatei (`-rc`) übergeben, weil Windows Kommandozeilenargumente an
MinGW-Programme in der ANSI-Codepage übergibt und UxPlay Nicht-UTF-8-Argumente ablehnt (Gerätenamen mit
„–“ oder Umlauten). Pfade mit Nicht-ASCII-Zeichen werden als 8.3-Kurzpfad übergeben.
Vor jedem Start liest DXMirror `uxplay -h` und verwendet nur Optionen, die die Binärdatei kennt.

## Datenfluss einer Spiegelung

1. UxPlay kündigt `_airplay._tcp`/`_raop._tcp` per mDNS an → iPhone listet „DXMirror – PC“.
2. iPhone verbindet sich (RTSP/HTTP), Pairing (optional PIN), FairPlay-Setup → `client_request`, `conn_open`.
3. Video: H.264-NAL-Einheiten via TCP-RTP → `video_size` → GStreamer-Pipeline → Direct3D-Präsentation im HWND.
4. Audio: AAC-ELD via UDP-RTP → `audio_format ct=8` → WASAPI. Synchronisation über NTP-Zeitstempel des
   iPhones (UxPlay `-vsync`, Feinjustierung per Einstellung „Ton-Verzögerung“).
5. Heartbeats des iPhones jede Sekunde; bleiben sie `-reset n` Sekunden aus → `conn_reset reason=1`.
6. DXMirror-Lautstärke: `ISimpleAudioVolume` der WASAPI-Session des Receiver-Prozesses (Systemlautstärke
   und andere Programme bleiben unberührt).

## Threads und Robustheit

- UI-Thread macht keine I/O: Receiver-Ereignisse laufen über einen Channel in eine Ereignisschleife
  (ein Leser), Snapshots sind unveränderlich und werden per `Dispatcher.InvokeAsync` übergeben.
- Start/Stopp serialisiert per `SemaphoreSlim`; Generationszähler verwirft späte Ereignisse alter Prozesse.
- Neustart nach Absturz: 1 s, 2 s, 5 s, 10 s, 30 s; nach 6 schnellen Fehlschlägen → Status „Fehler“.
- Netzwerkänderungen (entprellt 2 s): Adapter neu wählen, Receiver neu ankündigen, wenn kein iPhone verbunden.
- Begrenzte Puffer: Log-Channel 4096 (älteste verwerfen), Speicherlog 2000, UI-Log 500, Logdateien 5×5 MB.
  Video-/Audio-Puffer verwaltet GStreamer/UxPlay (`queue`, Jitterpuffer); DXMirror kopiert keine Frames.
- Unbehandelte UI-Ausnahmen werden protokolliert, die laufende Sitzung bleibt bestehen.

## Netzwerk und Ports

- UxPlay-Standardports (dynamisch) bzw. TCP 7000/7001/7100 und UDP 6000/6001/7011 mit `-p`; mDNS UDP 5353.
- Firewall-Regeln sind programmgebunden (`receiver\bin\uxplay.exe`), eingehend, Profil **Private**.
- Keine ausgehenden Internetverbindungen, keine Telemetrie.

## Daten auf dem PC

`%LOCALAPPDATA%\DXMirror\`: `settings.json` (fester PIN per DPAPI verschlüsselt), `logs\`,
`receiver\uxplay.rc` (generiert), `receiver\uxplay-key.pem` (Geräteschlüssel, damit das iPhone den PC
wiedererkennt), `receiver\uxplay-register.txt` (PIN-registrierte iPhones), `cache\gstreamer-registry.bin`.

## Technologien

.NET 10 (LTS) / C# / WPF, Windows Core Audio (COM), Win32 (Job-Objekte, DPAPI, DWM), UxPlay 1.74 (C/C++),
GStreamer 1.x aus MSYS2 UCRT64 (d3d11/d3d12, wasapi, libav/FFmpeg), OpenSSL 3, libplist, Inno Setup 6.
