; DXMirror installer (Inno Setup 6). Built by build\build.ps1:
;   ISCC /DAppVersion=0.1.0 /DPublishDir=..\artifacts\publish installer\DXMirror.iss

#ifndef AppVersion
  #define AppVersion "0.2.0"
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
; Admin rights are needed for Program Files and the firewall rules. Setup starts unelevated and
; relaunches itself through a UAC prompt with /ALLUSERS (see InitializeSetup). This avoids the
; "must be run as administrator" error when Windows' automatic elevation of Setup does not happen.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
; Inno Setup 6.7.0-6.7.3 pass the RedirectionGuard mitigation on to child processes; the UAC
; relaunch below then fails with "access denied". Setup only writes to its own folders, so it is off.
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
de.NeedAdmin=Für die Installation sind Administratorrechte nötig (Programmordner und Firewall-Regeln).%n%nBitte die Windows-Abfrage mit „Ja“ bestätigen. Nochmal versuchen?
en.NeedAdmin=Installing needs administrator rights (program folder and firewall rules).%n%nPlease confirm the Windows prompt with "Yes". Try again?

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "firewall"; Description: "{cm:FirewallTask}"; GroupDescription: "{cm:Network}"
Name: "autostart"; Description: "{cm:AutostartTask}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[INI]
; language chosen in Setup; DXMirror uses it while its own language setting is "Automatic"
Filename: "{app}\install.ini"; Section: "Setup"; Key: "Language"; String: "{language}"; Flags: uninsdeletesection
; hand-off to the unelevated Setup instance, which starts DXMirror / sets autostart as the real user
Filename: "{app}\install.ini"; Section: "Setup"; Key: "PostAutostart"; String: "1"; Tasks: autostart
Filename: "{app}\install.ini"; Section: "Setup"; Key: "PostAutostart"; String: "0"; Tasks: not autostart
Filename: "{app}\install.ini"; Section: "Setup"; Key: "PostLaunch"; String: "0"

[Icons]
Name: "{group}\DXMirror"; Filename: "{app}\DXMirror.exe"
Name: "{autodesktop}\DXMirror"; Filename: "{app}\DXMirror.exe"; Tasks: desktopicon

[Run]
; firewall: program-scoped, inbound, private profile only (same rules as Diagnose → "Firewall-Regeln einrichten")
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""DXMirror Receiver (TCP-In)"""; Flags: runhidden; Tasks: firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""DXMirror Receiver (UDP-In)"""; Flags: runhidden; Tasks: firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""DXMirror Receiver (TCP-In)"" dir=in action=allow program=""{app}\receiver\bin\uxplay.exe"" protocol=TCP profile=private enable=yes description=""AirPlay receiver (DXMirror). Local network only."""; Flags: runhidden; Tasks: firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""DXMirror Receiver (UDP-In)"" dir=in action=allow program=""{app}\receiver\bin\uxplay.exe"" protocol=UDP profile=private enable=yes description=""AirPlay receiver (DXMirror). Local network only."""; Flags: runhidden; Tasks: firewall
; Autostart and "Launch DXMirror" are done by the unelevated Setup instance (see [Code]), so they
; apply to the signed-in user and not to the administrator account used for elevation.

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im DXMirror.exe"; Flags: runhidden; RunOnceId: "KillApp"
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im uxplay.exe"; Flags: runhidden; RunOnceId: "KillReceiver"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""DXMirror Receiver (TCP-In)"""; Flags: runhidden; RunOnceId: "FwTcp"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""DXMirror Receiver (UDP-In)"""; Flags: runhidden; RunOnceId: "FwUdp"
; removes the HKCU autostart entry of the uninstalling user
Filename: "{app}\DXMirror.exe"; Parameters: "--cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "Cleanup"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\receiver"

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{3B9E4C21-7D5A-4F86-A1C2-9E7B5D3F8A40}_is1';

var
  LaunchBox: TNewCheckBox;

{ runs in the unelevated instance after the elevated one has finished }
procedure RunPostInstallAsUser();
var
  AppDir, Ini: String;
  Code: Integer;
begin
  if not RegQueryStringValue(HKLM64, UninstallKey, 'InstallLocation', AppDir) then Exit;
  Ini := AddBackslash(AppDir) + 'install.ini';
  if not FileExists(Ini) then Exit;
  if GetIniString('Setup', 'PostAutostart', '0', Ini) = '1' then
    Exec(AddBackslash(AppDir) + 'DXMirror.exe', '--set-autostart', AppDir, SW_HIDE, ewWaitUntilTerminated, Code);
  if GetIniString('Setup', 'PostLaunch', '0', Ini) = '1' then
    Exec(AddBackslash(AppDir) + 'DXMirror.exe', '', AppDir, SW_SHOW, ewNoWait, Code);
end;

function PsQuote(const S: String): String;
begin
  Result := S;
  StringChangeEx(Result, '''', '''''', True);
  Result := '''' + Result + '''';
end;

function InitializeSetup(): Boolean;
var
  Params, Arg, Script, PsFile, ErrFile: String;
  ErrText: AnsiString;
  Lines: TArrayOfString;
  I, Code: Integer;
begin
  Result := True;
  if IsAdminInstallMode then Exit;
  { Not in all-users mode yet: start Setup again through the Windows UAC prompt with /ALLUSERS,
    keep the chosen language, wait for it and then finish the per-user steps here.
    Inno's ShellExec refuses to start the Setup exe itself, so PowerShell does the elevation. }
  Params := '/ALLUSERS /LANG=' + ActiveLanguage;
  for I := 1 to ParamCount do
  begin
    Arg := ParamStr(I);
    { internal loader switches of this instance must not be passed on }
    if (Pos('/SL5', Uppercase(Arg)) = 1) or (Pos('/SPAWNWND', Uppercase(Arg)) = 1) or
       (Pos('/NOTIFYWND', Uppercase(Arg)) = 1) or (Pos('/LANG=', Uppercase(Arg)) = 1) then Continue;
    Params := Params + ' "' + Arg + '"';
  end;
  PsFile := ExpandConstant('{tmp}\elevate.ps1');
  ErrFile := ExpandConstant('{tmp}\elevate-error.txt');
  Script :=
    '$ErrorActionPreference = ''Stop''' + #13#10 +
    'try {' + #13#10 +
    '  $p = Start-Process -FilePath ' + PsQuote(ExpandConstant('{srcexe}')) +
    ' -ArgumentList ' + PsQuote(Params) + ' -Verb RunAs -Wait -PassThru' + #13#10 +
    '  exit $p.ExitCode' + #13#10 +
    '} catch {' + #13#10 +
    '  Set-Content -LiteralPath ' + PsQuote(ErrFile) + ' -Value $_.Exception.Message' + #13#10 +
    '  exit 1223' + #13#10 +
    '}' + #13#10;
  while True do
  begin
    SetArrayLength(Lines, 1);
    Lines[0] := Script;
    SaveStringsToUTF8File(PsFile, Lines, False);
    DeleteFile(ErrFile);
    if Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
            '-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + PsFile + '"',
            '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code <> 1223) then
    begin
      Log('Elevated Setup finished with exit code ' + IntToStr(Code));
      if Code = 0 then RunPostInstallAsUser();
      Break;
    end;
    ErrText := '';
    LoadStringFromFile(ErrFile, ErrText);
    Log('Elevation failed, code ' + IntToStr(Code) + ': ' + ErrText);
    if WizardSilent or (MsgBox(CustomMessage('NeedAdmin') + #13#10#13#10 + Trim(ErrText), mbConfirmation, MB_YESNO) <> IDYES) then
      Break;
  end;
  Result := False;   { this unelevated instance always ends here }
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and (LaunchBox = nil) then
  begin
    LaunchBox := TNewCheckBox.Create(WizardForm);
    LaunchBox.Parent := WizardForm.FinishedPage;
    LaunchBox.Left := WizardForm.FinishedLabel.Left;
    LaunchBox.Top := WizardForm.FinishedLabel.Top + WizardForm.FinishedLabel.Height + ScaleY(16);
    LaunchBox.Width := WizardForm.FinishedLabel.Width;
    LaunchBox.Height := ScaleY(20);
    LaunchBox.Caption := CustomMessage('Launch');
    LaunchBox.Checked := True;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpFinished) and (LaunchBox <> nil) and LaunchBox.Checked then
    SetIniString('Setup', 'PostLaunch', '1', ExpandConstant('{app}\install.ini'));
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
