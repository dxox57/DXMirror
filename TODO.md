# TODO

Stand: 25.11.2026 – Code vollständig geschrieben, **nicht kompiliert, nicht getestet**.

## Erledigt
- [x] Phase 1: Machbarkeitsanalyse, Architekturentscheidung (UxPlay 1.74, Prozess-Architektur), Projektstruktur
- [x] Phase 2: Netzwerkmanagement, mDNS (UxPlay), Receiver-Registrierung, Verbindungsaufbau, PIN
- [x] Phase 3: Video (D3D11/12 im eigenen Fenster), Audio (WASAPI), A/V-Sync-Optionen, Lautstärke pro App
- [x] Phase 4: Dashboard, Einstellungen, Diagnose, Tray, Benachrichtigungen, DE/EN, Themes
- [x] Phase 5 (Code-Ebene): Neustart mit Back-off, Job-Objekt, begrenzte Puffer, Generationszähler, Fehlerklassen
- [x] Phase 6 (Code-Ebene): Build-Skripte, Installer-Skript, Dokumentation, Unit-Tests

## Als Nächstes (braucht Windows-PC bzw. iPhone)
- [ ] `build\build.cmd` ausführen, Compile-Fehler beheben
- [ ] Unit-Tests ausführen
- [ ] Receiver-Selbsttest im Build prüfen (alle GStreamer-Elemente vorhanden?)
- [ ] Manuelle iPhone-Tests 1–19 aus TESTING.md, Ergebnisse und iOS-Versionen eintragen
- [ ] Prüfen: rendert `d3d11videosink` mit `DXMIRROR_HWND` korrekt in das eingebettete Fenster inkl. Größenänderung und Drehung
- [ ] Prüfen: PIN-Ablauf mit `-pin` / `-reg`
- [ ] Prüfen: Lautstärkeregelung greift auf die WASAPI-Session von `uxplay.exe`
- [ ] Prüfen: `stats fps` entspricht tatsächlichen Frames (1 Aufruf von `video_process` = 1 Frame?)
- [ ] Installer auf sauberer Windows-10- und Windows-11-VM testen

## Später / Ideen
- [ ] Mehrere gleichzeitige iPhones (mehrere Receiver-Instanzen mit eigener MAC/Ports, mehrere Video-Hosts)
- [ ] GPU-Auslastung über Leistungsindikatoren „GPU Engine“
- [ ] Verworfene Frames / Audiopuffer: zusätzliche Ereignisse im UxPlay-Patch (GStreamer-QoS-Nachrichten)
- [ ] Code-Signierung von Installer und Binärdateien (SmartScreen)
- [ ] Automatische Updates (nur mit Zustimmung)
- [ ] Optional MSIX-Paket
