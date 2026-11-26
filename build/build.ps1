<#
.SYNOPSIS
  Builds DXMirror end to end: receiver (UxPlay + GStreamer via MSYS2), .NET app, tests and installer.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File build\build.ps1
  powershell -ExecutionPolicy Bypass -File build\build.ps1 -SkipReceiver -SkipInstaller
#>
param(
    [string]$Configuration = "Release",
    [string]$Msys2Root = "C:\msys64",
    [switch]$SkipReceiver,
    [switch]$SkipTests,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$artifacts = Join-Path $root "artifacts"
$publish = Join-Path $artifacts "publish"
$receiver = Join-Path $artifacts "receiver"
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

# ------------------------------------------------------------------ prerequisites
Step "Checking prerequisites"
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw ".NET SDK not found. Install the .NET 10 SDK: https://dotnet.microsoft.com/download" }
$sdks = & dotnet --list-sdks
if (-not ($sdks -match "^10\.")) { throw ".NET 10 SDK required (found: $($sdks -join ', '))" }
Write-Host "    .NET SDK: $(& dotnet --version)"

# ------------------------------------------------------------------ receiver
if (-not $SkipReceiver) {
    Step "Building receiver (UxPlay + GStreamer) in MSYS2 UCRT64"
    $bash = Join-Path $Msys2Root "usr\bin\bash.exe"
    if (-not (Test-Path $bash)) { throw "MSYS2 not found at $Msys2Root. Install it from https://www.msys2.org (default path C:\msys64)." }
    $unixRoot = (& $bash -lc "cygpath -u '$root'").Trim()
    $env:MSYSTEM = "UCRT64"
    $env:CHERE_INVOKING = "1"
    & $bash -lc "cd '$unixRoot' && ./build/build-uxplay.sh"
    if ($LASTEXITCODE -ne 0) { throw "Receiver build failed (exit $LASTEXITCODE)" }
} elseif (-not (Test-Path (Join-Path $receiver "bin\uxplay.exe"))) {
    Write-Warning "No receiver in $receiver - the app will build but cannot receive AirPlay until build-uxplay.sh has run."
}

# ------------------------------------------------------------------ tests
if (-not $SkipTests) {
    Step "Running unit tests"
    & dotnet test (Join-Path $root "tests\DXMirror.Core.Tests\DXMirror.Core.Tests.csproj") -c $Configuration --logger "console;verbosity=normal"
    if ($LASTEXITCODE -ne 0) { throw "Tests failed" }
}

# ------------------------------------------------------------------ app
Step "Publishing DXMirror ($Configuration, win-x64, self-contained)"
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
& dotnet publish (Join-Path $root "src\DXMirror.App\DXMirror.App.csproj") -c $Configuration -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
if (Test-Path (Join-Path $receiver "bin\uxplay.exe")) {
    if (-not (Test-Path (Join-Path $publish "receiver\bin\uxplay.exe"))) {
        Copy-Item -Recurse -Force $receiver (Join-Path $publish "receiver")
    }
}
Copy-Item -Force (Join-Path $root "LICENSE") (Join-Path $publish "LICENSE.txt")
Write-Host "    Portable build: $publish\DXMirror.exe"

# ------------------------------------------------------------------ installer
if (-not $SkipInstaller) {
    Step "Building installer (Inno Setup 6)"
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 not found. Install it: winget install JRSoftware.InnoSetup" }
    $version = (Select-Xml -Path (Join-Path $root "src\DXMirror.App\DXMirror.App.csproj") -XPath "//Version").Node.InnerText
    & $iscc "/DAppVersion=$version" "/DPublishDir=$publish" "/O$artifacts\installer" (Join-Path $root "installer\DXMirror.iss")
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
    Write-Host "    Installer: $(Join-Path $artifacts "installer\DXMirror-Setup-$version-x64.exe")"
}

Step "Done"
