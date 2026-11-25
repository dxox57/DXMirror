# Builds the publishable package: Desktop\DXMirror-<version>.zip
# Run after a successful build\build.ps1:  powershell -ExecutionPolicy Bypass -File .\tools\make-release.ps1
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
[xml]$proj = Get-Content (Join-Path $root 'src\DXMirror.App\DXMirror.App.csproj')
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$setup = Join-Path $root "artifacts\installer\DXMirror-Setup-$version-x64.exe"
if (-not (Test-Path $setup)) { throw "Installer not found: $setup (run build\build.ps1 first)" }
if ((Get-Item $setup).LastWriteTime -lt (Get-Item (Join-Path $root 'installer\DXMirror.iss')).LastWriteTime) {
  throw "Installer is older than installer\DXMirror.iss - the last build did not finish. Run build\build.ps1 again."
}

$desktop = [Environment]::GetFolderPath('Desktop')
$name = "DXMirror-$version"
$stage = Join-Path $env:TEMP "$name-release"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

Write-Host "==> Installer"
Copy-Item $setup $stage
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
"$hash  DXMirror-Setup-$version-x64.exe" | Out-File (Join-Path $stage 'SHA256.txt') -Encoding ascii
Copy-Item (Join-Path $root 'tools\release\LIESMICH.txt') $stage
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'LICENSE.txt')
Copy-Item (Join-Path $root 'artifacts\receiver\licenses') (Join-Path $stage 'Lizenzen') -Recurse

Write-Host "==> Source code"
$src = Join-Path $env:TEMP "$name-source"
if (Test-Path $src) { Remove-Item $src -Recurse -Force }
New-Item -ItemType Directory -Force $src | Out-Null
foreach ($dir in 'src','tests','tools','installer','third_party') {
  robocopy (Join-Path $root $dir) (Join-Path $src $dir) /E /XD bin obj /XF *.bak *.tmp /NFL /NDL /NJH /NJS /NP | Out-Null
  if ($LASTEXITCODE -ge 8) { throw "copy of $dir failed" }
}
New-Item -ItemType Directory -Force (Join-Path $src 'build') | Out-Null
Copy-Item (Join-Path $root 'build\*') (Join-Path $src 'build') -Include *.ps1,*.sh,*.cmd
foreach ($f in 'README.md','BUILD.md','ARCHITECTURE.md','TESTING.md','CHANGELOG.md','TODO.md','LICENSE','DXMirror.slnx') {
  if (Test-Path (Join-Path $root $f)) { Copy-Item (Join-Path $root $f) $src }
}
New-Item -ItemType Directory -Force (Join-Path $stage 'Quellcode') | Out-Null
Compress-Archive -Path (Join-Path $src '*') -DestinationPath (Join-Path $stage "Quellcode\$name-source.zip") -CompressionLevel Optimal

Write-Host "==> ZIP"
$zip = Join-Path $desktop "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $stage, $src -Recurse -Force
Write-Host ""
Write-Host "Fertig: $zip"
Write-Host "SHA256 des Installers: $hash"
