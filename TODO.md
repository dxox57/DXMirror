# TODO

Stand: 26.11.2026 – Version 0.3.1 (Installation pro Benutzer, PIN entfernt, Vollbild-Fix).

## Als Nächstes
- [ ] 0.3.0 auf beiden PCs testen: alte Version deinstallieren, neu installieren (keine Admin-Abfrage für
      das Setup, genau eine für die Firewall), iPhone verbinden, Sprache
- [ ] Blockier-Warnung gezielt testen (TeamViewer starten, Bonjour-Dienst stoppen, Firewall-Regeln löschen)
- [ ] Weitere iPhones/iPads und iOS-Versionen in TESTING.md eintragen
- [ ] Vor öffentlicher Veröffentlichung: Signatur über SignPath Foundation (kostenlos für Open Source;
      setzt öffentliches GitHub-Projekt und automatischen Build per GitHub Actions voraus)
- [ ] Optional: Inno Setup auf ≥ 6.7.4 aktualisieren (`winget upgrade JRSoftware.InnoSetup`)

## Erledigt (0.1.0 → 0.3.0)
- [x] Build (MSYS2/GStreamer 1.28.9, .NET 10), Unit-Tests, Installer
- [x] Erkennung über Bonjour, Spiegelung mit Ton, eingebettetes Video, Lautstärke, geringe Latenz
- [x] Umbenennung in DXMirror, Tray-Schalter, Auswahllisten
- [x] Aufnahme als MP4 mit Ton, Hinweis „Aufnahme gespeichert“
- [x] Blockier-Warnung mit Bonjour-Download und Firewall-Knopf
- [x] Installer zweisprachig, App übernimmt Installer-Sprache, Installation ohne Admin-Rechte
- [x] OBS-Ausgabe und PIN ausprobiert und wieder entfernt
- Diagnosebericht des Kollegen: `diagnose-berichte/DXMirror-Diagnose-20261126-011433-Kollege.zip`
