<#
.SYNOPSIS
  Installs the build tools for DXMirror with winget (only needed on the build PC, not for users):
  .NET 10 SDK, MSYS2 and Inno Setup 6.
#>
$ErrorActionPreference = "Stop"
if (-not (Get-Command winget -ErrorAction SilentlyContinue)) { throw "winget not available. Install 'App Installer' from the Microsoft Store." }
winget install --id Microsoft.DotNet.SDK.10 --exact --accept-package-agreements --accept-source-agreements
winget install --id MSYS2.MSYS2 --exact --accept-package-agreements --accept-source-agreements
winget install --id JRSoftware.InnoSetup --exact --accept-package-agreements --accept-source-agreements
Write-Host "`nUpdating MSYS2 (may need to run twice if the shell restarts)..." -ForegroundColor Cyan
& "C:\msys64\usr\bin\bash.exe" -lc "pacman -Syu --noconfirm"
& "C:\msys64\usr\bin\bash.exe" -lc "pacman -Syu --noconfirm"
Write-Host "`nDone. Now run build\build.cmd" -ForegroundColor Green
