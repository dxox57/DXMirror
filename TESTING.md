# Tests

## Automatisierte Tests

`dotnet test tests\DXMirror.Core.Tests` (läuft auch in `build\build.ps1`). Abgedeckt:

| Bereich | Tests |
|---|---|
| Protokoll zum Receiver | Parser für `@@DXM`-Ereignisse inkl. fehlerhafter/überlanger/manipulierter Zeilen |
| Sitzungszustand | kompletter Spiegelungsablauf, Hoch-/Querformat-Wechsel, Audio-only, abgelehnter Client, Verbindungs-Reset, unplausible Werte |
| Receiver-Optionen | Erzeugung der UxPlay-Startdatei für alle Einstellungen, Quoting, Schutz vor Pipeline-Injection über die Audio-Geräte-ID, Optionen unbekannter UxPlay-Versionen |
| Prozesssteuerung | Start/Stopp mit Fake-Prozess, `quit` und Kill-Fallback, Ressourcenfreigabe, automatischer Neustart, Fehlerklassifizierung, verworfene Ereignisse alter Prozesse, fehlende Binärdatei |
| Einstellungen | Validierung/Grenzwerte, Gerätenamen, atomares Speichern, defekte Datei |
| Netzwerk | Adapter-Klassifizierung (WLAN/LAN/VPN/virtuell), automatische und manuelle Auswahl, Fallback, private Adressen |
| Logging/Diagnose | Schwärzung von PIN/Passwort/Schlüsseln, begrenzter Speicher, Logdatei, Diagnose-ZIP |
| Neustart-Strategie | Back-off, Aufgeben, Zurücksetzen nach stabiler Laufzeit |

Der Build-Selbsttest in `build-uxplay.sh` prüft zusätzlich, dass alle GStreamer-Elemente aus dem Bundle
ohne MSYS2 im `PATH` geladen werden können.

## Testergebnisse

| Datum | Was | Ergebnis |
|---|---|---|
| 25.11.2026 | UxPlay-Patch auf Commit `fbd040e` anwenden (`git apply --check`) | ✅ erfolgreich |
| 25.11.2026 | Unit-Tests | ⚠️ **nicht ausgeführt** – in der Entwicklungsumgebung gab es kein .NET-SDK |
| 25.11.2026 | Kompilieren App/Receiver | ⚠️ **nicht ausgeführt** – keine Windows-Toolchain verfügbar |
| 25.11.2026 | Build auf Windows 11 (Build 26300), .NET 10.0.404, MSYS2 UCRT64, GStreamer 1.28.9 | ✅ nach Korrekturen erfolgreich |
| 25.11.2026 | Unit-Tests | ✅ 88/88 bestanden |
| 25.11.2026 | Erkennung mit UxPlay-internem mDNS (1.74, experimentell) | ❌ kein UDP-5353-Socket, iPhone findet PC nicht → umgestellt auf Apple Bonjour |
| 25.11.2026 | Erkennung über Bonjour, Verbindung, Spiegelung, Ton | ✅ iPhone 16 Pro (iPhone17,1, AirPlay/1005.13.1) |
| 25.11.2026 | Video eingebettet per GstVideoOverlay bzw. SetParent | ❌ graue Fläche → Receiver-Fenster wird jetzt als rahmenloses „owned window“ über die Fläche gelegt ✅ |
| 25.11.2026 | Lautstärkeregler (WASAPI-Session) | ❌ ohne Wirkung → jetzt über GStreamer-Volume im Receiver ✅ |
| 25.11.2026 | Verzögerung mit -vsync / 250 ms | ~0,5 s+; ohne Sync, 100 ms Puffer: ~0,5 s, Bild/Ton synchron ✅ |
| 25.11.2026 | Aufnahme (MP4), Umbenennung DXMirror | ✅ nach Korrekturen (Plugins, AAC-LC statt AAC-ELD): Bild und Ton im Medienplayer |
| 26.11.2026 | OBS-Ausgabe (SRT) | ❌ OBS zeigte kein Bild → Funktion wieder entfernt |
| 26.11.2026 | Installer: Admin-Abfrage (Inno 6.7.3, RedirectionGuard aus), Sprache DE/EN | ✅ auf dem Entwickler-PC; beim Kollegen mehrfaches „Ja“ nötig → eigene Neustart-Lösung ersetzt durch Inno-eigene Abfrage + SetupMutex ⏳ |
| 26.11.2026 | Zweiter PC (Kollege): Windows 11 Build 26300, iPhone 15 Pro Max (iPhone16,2), WLAN | ✅ Erkennung, Spiegelung, Ton, Lautstärke, Aufnahme |
| 26.11.2026 | Installer 0.2.1-Vorabstand (Inno-eigene Abfrage) | ❌ erst „muss als Administrator ausgeführt werden“, beim 3. Start Abfrage → Starter mit `requireAdministrator` ⏳ |
| 26.11.2026 | Fester PIN | ❌ Receiver startete nicht („unknown option -pin1234“), nach Korrektur weiter Probleme beim Kollegen → PIN-Funktion in 0.3.0 entfernt |
| 26.11.2026 | Installer mit `requireAdministrator`-Starter | ❌ beim Kollegen „write installer – Zugriff verweigert (5)“ (vermutlich Norton) → 0.3.0: Installation pro Benutzer ohne Admin-Rechte ⏳ |
| 26.11.2026 | Blockier-Warnung | ⏳ noch nicht gezielt getestet (z. B. mit TeamViewer) |
| 26.11.2026 | Vollbild verlassen nach Klick ins Video | ❌ keine Taste wirkte → 0.3.1: Video-Fenster nicht aktivierbar, Esc/F11 als Hotkeys im Vollbild ⏳ |

Getestet mit einem iPhone 16 Pro (siehe Tabelle). Andere Modelle und iOS-Versionen sind noch nicht geprüft.

### Unterstützte iPhone-/iOS-Versionen

Noch keine eigenen Testergebnisse. Bitte nach dem manuellen Test hier eintragen:

| iPhone | iOS | Spiegelung | Audio | Sync | Hoch/Quer | Bemerkung |
|---|---|---|---|---|---|---|
| iPhone 16 Pro (iPhone17,1) | AirPlay/1005.13.1 (iOS-Version noch eintragen) | ✅ | ✅ | ✅ (ohne -vsync) | Hochformat ✅, Quer ⏳ | TeamViewer muss aus sein (blockiert mDNS auf WLAN-Adresse) |

## Manueller Test mit einem echten iPhone

Voraussetzung: DXMirror installiert (Setup mit Firewall-Aufgabe), PC und iPhone im selben privaten WLAN.
Vorher **Diagnose** öffnen: Firewall „Regeln vorhanden“, Netzwerkprofil „Private“.
Bei jedem Fehler: **Diagnose → Diagnosebericht exportieren** und ZIP beilegen.

| # | Szenario | Schritte | Erwartung |
|---|---|---|---|
| 1 | Erkennung | DXMirror starten, iPhone: Kontrollzentrum → Bildschirmsynchronisierung | „DXMirror – <PC>“ erscheint innerhalb ~5 s |
| 2 | Verbindung | Eintrag antippen | Status „Verbunden“, Gerätename und Dauer sichtbar |
| 3 | Spiegelung | iPhone-Startbildschirm, Apps öffnen, scrollen | Bild im Fenster, flüssig, keine Artefakte; FPS > 0 bei Bewegung |
| 4 | Ton | YouTube/Video mit Ton oder Spiel abspielen | Ton aus dem gewählten Windows-Gerät |
| 5 | Synchronität | Video mit sichtbarem Sprecher / Klatschen | kein wahrnehmbarer Versatz (sonst „Ton-Verzögerung“ anpassen und notieren) |
| 6 | Hoch-/Querformat | iPhone drehen (Video-App im Vollbild) | Bild passt sich an, Auflösung zeigt „Querformat“ |
| 7 | Sauber beenden | iPhone: „Synchronisierung beenden“; danach DXMirror: „Verbindung trennen“ bei neuer Sitzung | Status „Bereit“, keine hängenden Prozesse (Task-Manager: ein `uxplay.exe`) |
| 8 | Wiederverbinden | direkt erneut verbinden, 5× wiederholen | jedes Mal erfolgreich |
| 9 | WLAN-Unterbrechung | während Spiegelung WLAN am iPhone 10 s aus, wieder an | nach Timeout „Bereit“, neue Verbindung möglich, App stürzt nicht ab |
| 10 | Dauerbetrieb | 2–3 h spiegeln (Video), Diagnose offen lassen | RAM von DXMirror und Receiver bleibt annähernd konstant, keine Aussetzer |
| 11 | Autostart | Einstellung „mit Windows starten“, Neustart | DXMirror im Tray, Receiver bereit, iPhone findet PC |
| 12 | Deinstallation | Apps → DXMirror deinstallieren | Programm, Firewall-Regeln (`wf.msc`), Autostart-Eintrag entfernt; Daten nur nach Zustimmung |
| 13 | Nur Audio | Musik-App → AirPlay → DXMirror | Musik spielt, Modus „AirPlay-Audio“ |
| 14 | Lautstärke | Regler/Stumm in DXMirror | nur DXMirror wird leiser, Systemlautstärke unverändert |
| 15 | Anderes Ausgabegerät | Kopfhörer in Einstellungen wählen, speichern | Ton über Kopfhörer |
| 16 | Vollbild | F11, Esc | Vollbild ohne Rahmen, Esc zurück, Übertragung läuft weiter |
| 17 | Minimieren | Fenster minimieren / schließen | Übertragung läuft weiter, Tray-Menü funktioniert |
| 18 | Mehrere Adapter | VPN aktiv, Adapter in Einstellungen wählen | iPhone findet PC über gewählten Adapter |
| 19 | Fremder PC | Installer auf PC ohne Entwicklertools | Installation und Szenarien 1–4 funktionieren |

## Bekannte Einschränkungen

- **Apple Bonjour muss installiert sein** (Dienst „Bonjour Service“, kommt z. B. mit iTunes oder „Bonjour Print Services“). UxPlays eingebauter mDNS-Responder funktionierte im Test nicht.
- **TeamViewer** belegte im Test UDP 5353 auf der WLAN-Adresse; solange es lief, konnte Bonjour dort nicht antworten.
- Die Netzwerkadapter-Auswahl wirkt mit Bonjour nicht (Bonjour kündigt auf allen Adaptern an).
- Aufnahmen: Startet man mitten in einer Sitzung, beginnt die Datei beim letzten Schlüsselbild (bis 96 MB werden im Speicher gehalten); sonst wartet die Aufnahme auf das nächste Schlüsselbild. Beim Drehen des iPhones beginnt eine neue Datei.

- Nicht kompiliert und nicht mit echter Hardware getestet (siehe oben).
- Nur **eine** aktive iPhone-Verbindung (UxPlay-Einschränkung; ein zweiter Receiver bräuchte eine eigene
  Instanz mit anderem Namen/Ports/MAC – Architektur vorbereitet, nicht umgesetzt).
- **GPU-Auslastung, verworfene Frames und Audiopuffer-Füllstand** werden nicht angezeigt, weil UxPlay diese
  Werte nicht liefert. Die FPS-Anzeige zählt die vom iPhone gesendeten Frames (iOS sendet bei statischem
  Bild kaum Frames).
- „Verbindung trennen“ startet den Receiver neu (UxPlay hat keinen Befehl zum Trennen eines Clients).
- Automatische Qualitätsanpassung erfolgt durch iOS; DXMirror setzt nur Obergrenzen (Auflösung/FPS).
- IPv6: mDNS-Ankündigung über IPv4 und IPv6 durch UxPlay; die Adapterauswahl wirkt nur auf IPv4.
- Geschützte Inhalte (DRM, z. B. manche Streaming-Apps) werden von iOS nicht oder nur schwarz gespiegelt –
  das ist beabsichtigt und wird nicht umgangen.
- Deinstallation: Autostart-Eintrag und Datenordner werden für den Benutzer entfernt, der die
  Deinstallation ausführt (Installation ist ohnehin pro Benutzer).
- YouTube-HLS-Weitergabe von UxPlay (`-hls`) ist nicht aktiviert.
