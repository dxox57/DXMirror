# Changelog

## 0.3.1 – 2026-11-26

### Fehlerbehebungen
- Vollbild ließ sich nicht mehr verlassen, sobald man ins Video geklickt hatte: Das Video-Fenster des Receivers nahm DXMirror die Tastatur weg. Es ist jetzt „nicht aktivierbar“, und im Vollbild sind **Esc** und **F11** als System-Tastenkürzel angemeldet, die unabhängig vom Fokus wirken (nur solange DXMirror im Vollbild vorne ist)

## 0.3.0 – 2026-11-26

### Änderungen
- **Installation pro Benutzer ohne Administratorrechte** (`%LOCALAPPDATA%\Programs\DXMirror`, wie Discord oder VS Code). Damit entfallen die Probleme mit der Admin-Abfrage des Installers („muss als Administrator ausgeführt werden“, „Zugriff verweigert“ durch Virenschutz). Nur für die Firewall-Regeln fragt Windows einmal nach – für `cmd /c netsh …` (von Microsoft signiert)
- Fehlen die Firewall-Regeln (oder zeigen sie noch auf die alte Installation unter „Programme“), zeigt die Übersicht eine Warnung mit Knopf „Firewall einrichten“
- Alte Installation für alle Benutzer (bis 0.2.1) wird erkannt; der Installer bittet, sie zuerst zu deinstallieren
- **PIN-Funktion entfernt** (fester und zufälliger PIN, „bekannte iPhones merken“)
- Starter `requireAdministrator` aus 0.2.1-Vorabständen wieder entfernt

## 0.2.1 – 2026-11-26

### Fehlerbehebungen
- Fester PIN: Receiver startete nicht („unknown option -pin1234“). Der PIN wird jetzt als `-pin 1234` übergeben, 0000 ist nicht mehr erlaubt (UxPlay akzeptiert 0001–9999)
- Wird eine Einstellung vom Receiver abgelehnt, zeigt DXMirror einen klaren Hinweis statt endloser Neustarts
- Installer: Unter Windows 11 kam oft erst „muss als Administrator ausgeführt werden“ und erst beim 2./3. Start die Admin-Abfrage. Der veröffentlichte Installer ist jetzt ein kleiner Starter, der als „benötigt Administratorrechte“ markiert ist: Windows fragt direkt beim Doppelklick, das eigentliche Inno-Setup läuft dann schon mit Admin-Rechten. Der Deinstaller wird ebenfalls direkt mit Admin-Rechten gestartet. Nur eine Installer-Instanz gleichzeitig

## 0.2.0 – 2026-11-25

### Neue Funktionen
- Umbenennung in **DXMirror** (Programm, Installer, Datenordner %LOCALAPPDATA%\DXMirror; Einstellungen und Geräteschlüssel werden einmalig übernommen)
- Aufnahme-Knopf in der Übersicht: MP4 mit Bild und Ton, Speicherort in den Einstellungen (Standard: Videos\DXMirror)
- Lautstärke/Stumm direkt im Receiver (GStreamer), kombiniert mit der iPhone-Lautstärke
- Niedrigere Verzögerung als Standard (A/V-Sync per Zeitstempel aus, 100 ms Audiopuffer)
- Installer fragt selbst per Windows-Abfrage nach Administratorrechten (statt Fehlermeldung) und bietet Deutsch/Englisch an; die gewählte Sprache übernimmt DXMirror, solange in den Einstellungen „Automatisch“ eingestellt ist
- **Blockier-Warnung** (ungetestet): Übersicht und Diagnose zeigen einen Hinweis, wenn Bonjour fehlt/nicht läuft, ein anderes Programm (z. B. TeamViewer) den Erkennungs-Port 5353 belegt oder ein anderer AirPlay-Empfänger läuft; bei fehlendem Bonjour mit Download-Knopf

### Fehlerbehebungen
- iPhone fand den PC nicht: Erkennung über Apple Bonjour statt experimentellem UxPlay-mDNS
- Graue Videofläche: Video-Fenster des Receivers wird passgenau über die Fläche gelegt
- Auswahllisten zeigten „Option { Value = … }“
- Minimieren in den Infobereich ist jetzt standardmäßig aus (Schalter in den Einstellungen)
- Build: PowerShell-Kodierung, pacman-Wiederholung bei Fehler 429, fehlendes System.IO, doppelte catch-Klausel

## 0.1.0 – 2026-11-25 (Entwicklungsstand, ungetestet)

### Neue Funktionen
- AirPlay-Receiver auf Basis von UxPlay 1.74 (Commit `fbd040e`) als separater, überwachter Prozess
- DXMirror-Patch für UxPlay: maschinenlesbare Statusereignisse, Steuerkanal (stdin), Rendering in ein
  fremdes Fenster, wählbarer mDNS-Adapter, PIN nicht in der Konsole
- iPhone-Bildschirmsynchronisierung mit Ton, AirPlay-Audio, optionaler Nur-Audio-Modus
- Video im DXMirror-Fenster (Direct3D 11/12), Vollbild (F11/Esc), Hardware-Decodierung mit Software-Fallback
- WASAPI-Ausgabegerät, App-eigene Lautstärke/Stummschaltung, A/V-Sync-Feinjustierung, Audiolatenz
- Dashboard mit Empfänger-, Verbindungs- und Stream-Status; Einstellungen in 5 Kategorien
- Netzwerkadapter-Erkennung (WLAN/LAN/VPN/virtuell), automatische oder manuelle Auswahl, Reaktion auf Netzwerkwechsel
- Optionaler PIN-Schutz (zufällig oder fest, DPAPI-verschlüsselt gespeichert), bekannte iPhones merken
- Firewall-Regeln (programmgebunden, nur privat) per Setup oder Diagnose-Seite mit Zustimmung
- Tray-Symbol mit Menü, Benachrichtigungen, Autostart, Einzelinstanz
- Deutsch/Englisch, Hell/Dunkel/System
- Logging mit Rotation und Schwärzung, Ressourcenanzeige, Netzwerkdiagnose, Diagnose-ZIP
- Unit-Tests für Core-Bibliothek
- Build-Skripte (MSYS2 + .NET) mit Bundle-Selbsttest, Inno-Setup-Installer mit sauberer Deinstallation

### Fehlerbehebungen
- –
