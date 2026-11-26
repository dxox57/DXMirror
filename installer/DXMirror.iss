; DXMirror installer (Inno Setup 6). Built by build\build.ps1:
;   ISCC /DAppVersion=0.3.1 /DPublishDir=..\artifacts\publish installer\DXMirror.iss

#ifndef AppVersion
  #define AppVersion "0.3.1"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
AppId={{3B9E4C21-7D5A-4F86-A1C2-9E7B5D3F8A40}
AppName=DXMirror
AppVersion={#AppVersion}
AppVerName=DXMirror {#AppVersion}
AppPublisher=DXMirror
AppComments=AirPlay receiver for Windows (based on UxPlay)
DefaultDirName={autopf}\DXMirror
DisableDirPage=auto
DefaultGroupName=DXMirror
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputBaseFilename=DXMirror-Setup-{#AppVersion}-x64
SetupIconFile=..\src\DXMirror.App\Assets\DXMirror.ico
UninstallDisplayIcon={app}\DXMirror.exe
UninstallDisplayName=DXMirror
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; Per-user installation (like Discord, Spotify, VS Code): no administrator rights, so no UAC prompt
; for Setup itself. Inno's self-elevation failed intermittently on Windows 11 ("must be run as
; administrator") and antivirus software blocked elevated temp executables. {autopf} is
; %LOCALAPPDATA%\Programs here. Only the firewall rules need admin rights: DXMirror creates them with
; one UAC prompt for "cmd /c netsh ..." (Microsoft-signed), see [Run].
PrivilegesRequired=lowest
; only one Setup at a time
SetupMutex=DXMirrorSetupMutex
; Inno Setup 6.7.0-6.7.3 pass the RedirectionGuard mitigation on to programs started by Setup
; (always with PrivilegesRequired=lowest); the UAC prompt for the firewall rules then fails with
; "access denied". Setup only writes to its own folders, so it is off.
RedirectionGuard=no
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
de.FirewallTask=Windows-Firewall-Regeln für AirPlay anlegen (nur private Netzwerke, nur der DXMirror-Receiver)
en.FirewallTask=Create Windows Firewall rules for AirPlay (private networks only, DXMirror receiver only)
de.AutostartTask=DXMirror mit Windows starten
en.AutostartTask=Start DXMirror with Windows
de.RemoveData=Sollen auch die DXMirror-Einstellungen und Protokolle gelöscht werden?
en.RemoveData=Also delete DXMirror settings and logs?
de.Network=Netzwerk:
en.Network=Network:
de.Launch=DXMirror starten
en.Launch=Launch DXMirror
de.FirewallStatus=Firewall-Regeln werden eingerichtet (bitte die Windows-Abfrage bestätigen) …
en.FirewallStatus=Setting up firewall rules (please confirm the Windows prompt) …
de.OldVersion=Eine ältere DXMirror-Version ist für alle Benutzer installiert (Ordner „Programme“).%n%nBitte deinstalliere sie zuerst: Windows-Einstellungen → Apps → Installierte Apps → DXMirror → Deinstallieren.%n%nApp-Einstellungen jetzt öffnen?
en.OldVersion=An older DXMirror version is installed for all users ("Program Files").%n%nPlease uninstall it first: Windows Settings → Apps → Installed apps → DXMirror → Uninstall.%n%nOpen the app settings now?

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "firewall"; Description: "{cm:FirewallTask}"; GroupDescription: "{cm:Network}"
Name: "autostart"; Description: "{cm:AutostartTask}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[INI]
; language chosen in Setup; DXMirror uses it while its own language setting is "Automatic"
Filename: "{app}\install.ini"; Section: "Setup"; Key: "Language"; String: "{language}"; Flags: uninsdeletesection

[Icons]
Name: "{group}\DXMirror"; Filename: "{app}\DXMirror.exe"
Name: "{autodesktop}\DXMirror"; Filename: "{app}\DXMirror.exe"; Tasks: desktopicon

[Run]
; firewall: program-scoped, inbound, private profile only. DXMirror runs "cmd /c netsh ..." through one
; UAC prompt (Microsoft-signed, not blocked by antivirus). If declined, DXMirror offers it again later.
Filename: "{app}\DXMirror.exe"; Parameters: "--setup-firewall"; StatusMsg: "{cm:FirewallStatus}"; Flags: waituntilterminated; Tasks: firewall
Filename: "{app}\DXMirror.exe"; Parameters: "--set-autostart"; Flags: waituntilterminated; Tasks: autostart
Filename: "{app}\DXMirror.exe"; Description: "{cm:Launch}"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im DXMirror.exe"; Flags: runhidden; RunOnceId: "KillApp"
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im uxplay.exe"; Flags: runhidden; RunOnceId: "KillReceiver"
; removes the firewall rules (one UAC prompt, only if they exist) and the autostart entry
Filename: "{app}\DXMirror.exe"; Parameters: "--remove-firewall"; Flags: waituntilterminated; RunOnceId: "Firewall"
Filename: "{app}\DXMirror.exe"; Parameters: "--cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "Cleanup"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\receiver"

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{3B9E4C21-7D5A-4F86-A1C2-9E7B5D3F8A40}_is1';

{ Versions up to 0.2.1 were installed for all users into "Program Files". Both cannot be mixed,
  so the old one has to be uninstalled first. }
function InitializeSetup(): Boolean;
var
  Code: Integer;
begin
  Result := True;
  if RegKeyExists(HKLM64, UninstallKey) or RegKeyExists(HKLM32, UninstallKey) then
  begin
    if (not WizardSilent) and (MsgBox(CustomMessage('OldVersion'), mbInformation, MB_YESNO) = IDYES) then
      ShellExec('open', 'ms-settings:appsfeatures', '', '', SW_SHOWNORMAL, ewNoWait, Code);
    Result := False;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\DXMirror');
    { silent uninstall keeps user data; otherwise the user decides }
    if (not UninstallSilent) and DirExists(DataDir) then
      if MsgBox(CustomMessage('RemoveData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
