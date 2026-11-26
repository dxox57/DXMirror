# Changelog

## 0.2.0 – 2026-11-25

### New features
- Renamed the application to **DXMirror** (application, installer, and data folder `%LOCALAPPDATA%\DXMirror`; settings and device keys are migrated once)
- Added a recording button to the dashboard: records video and audio as MP4; save location can be set in Settings (default: `Videos\DXMirror`)
- Added volume and mute controls directly in the receiver (GStreamer), combined with the iPhone’s volume
- Reduced default latency (timestamp-based A/V sync disabled, 100 ms audio buffer)
- The installer now requests administrator permission through a Windows prompt instead of showing an error, and offers German and English; DXMirror uses the selected language while the language setting is set to “Automatic”
- **Blocking warning** (untested): the dashboard and Diagnostics show a warning if Bonjour is missing or not running, another program (e.g. TeamViewer) is using discovery port 5353, or another AirPlay receiver is running; includes a download button when Bonjour is missing

### Bug fixes
- iPhone could not find the PC: switched discovery to Apple Bonjour instead of experimental UxPlay mDNS
- Gray video area: the receiver’s video window is now positioned to fit the display area
- Fixed option lists displaying “Option { Value = … }”
- Minimize to the system tray is now off by default (can be changed in Settings)
- Build fixes: PowerShell encoding, retrying pacman after error 429, missing `System.IO`, and a duplicate catch clause

## 0.1.0 – 2026-11-25 (development version, untested)

### New features
- AirPlay receiver based on UxPlay 1.74 (commit `fbd040e`) running as a separate, supervised process
- DXMirror patch for UxPlay: machine-readable status events, control channel (stdin), rendering inside another window, selectable mDNS adapter, and PIN hidden from the console
- iPhone screen mirroring with audio, AirPlay audio, and optional audio-only mode
- Video displayed in the DXMirror window (Direct3D 11/12), full screen (F11/Esc), hardware decoding with software fallback
- WASAPI output device, app-specific volume and mute controls, A/V sync adjustment, and audio latency settings
- Dashboard showing receiver, connection, and stream status; settings organized into five categories
- Network adapter detection (Wi-Fi/LAN/VPN/virtual), automatic or manual selection, and response to network changes
- Optional PIN protection (random or fixed, stored encrypted with DPAPI); remembers known iPhones
- Per-application Windows Firewall rules, limited to private networks, configurable through the installer or Diagnostics with user consent
- System tray icon and menu, notifications, auto-start, and single-instance support
- German/English and light/dark/system themes
- Rotating logs with sensitive data redaction, resource monitor, network diagnostics, and diagnostic ZIP export
- Unit tests for the Core library
- Build scripts (MSYS2 + .NET) with bundle self-test, Inno Setup installer, and clean uninstall

### Bug fixes
- None
