# Diagnose: why does Windows refuse to elevate the DXMirror installer? Writes elevation-check.txt
$out = Join-Path $PSScriptRoot '..\elevation-check.txt'
$setup = Join-Path $PSScriptRoot '..\artifacts\installer\DXMirror-Setup-0.2.0-x64.exe'
function W($t) { $t | Out-File -FilePath $out -Append -Encoding utf8 }
"DXMirror elevation check $(Get-Date)" | Out-File -FilePath $out -Encoding utf8
W "--- 1. Test: Notepad als Admin"
try { $p = Start-Process notepad.exe -Verb RunAs -PassThru; W "OK notepad elevated (PID $($p.Id))"; Start-Sleep 1; Stop-Process $p -ErrorAction SilentlyContinue } catch { W "FEHLER notepad: $($_.Exception.Message)" }
W "--- 2. Test: Installer als Admin (nur Start, danach abbrechen)"
try { $p = Start-Process $setup -ArgumentList '/ALLUSERS' -Verb RunAs -PassThru; W "OK setup elevated (PID $($p.Id))" } catch { W "FEHLER setup: $($_.Exception.Message) / HRESULT $($_.Exception.HResult)" }
W "--- 3. Policies"
Get-ItemProperty HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System -ErrorAction SilentlyContinue |
  Select-Object EnableLUA,ConsentPromptBehaviorAdmin,ValidateAdminCodeSignatures,EnableSecureUIAPaths,EnableInstallerDetection,TypeOfAdminApprovalMode | Format-List | Out-String | W
W "SmartAppControl (0=aus,1=an,2=Test): $((Get-ItemProperty HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy -ErrorAction SilentlyContinue).VerifiedAndReputablePolicyState)"
W "--- 4. Datei"
W "Signatur: $((Get-AuthenticodeSignature $setup).Status)"
W "Zone.Identifier: $((Get-Content $setup -Stream Zone.Identifier -ErrorAction SilentlyContinue) -join ' | ')"
try { W "ControlledFolderAccess: $((Get-MpPreference).EnableControlledFolderAccess)" } catch { W "Defender-Abfrage nicht moeglich" }
W "--- 5. Kopie in Temp als Admin"
$copy = Join-Path $env:TEMP 'DXMirror-Setup-test.exe'
Copy-Item $setup $copy -Force
try { $p = Start-Process $copy -ArgumentList '/ALLUSERS' -Verb RunAs -PassThru; W "OK copy elevated (PID $($p.Id))" } catch { W "FEHLER copy: $($_.Exception.Message)" }
W "fertig"
"fertig - Datei elevation-check.txt erstellt"
