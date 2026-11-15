# FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
# https://github.com/Pribilof-Hangar/fipinator-live
# Builds the two installers players download (developer machine only):
#   publish\installers\FIPinator-Live-Setup-<version>.exe   (the main one)
#   publish\installers\FIPinator-3000-Setup-<version>.exe   (the optional gauge maker)
#   powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 [-Version 0.9.0]
# Needs: the .NET 10 SDK, Inno Setup 6 (ISCC.exe), and the MSFS 2024 SDK (for SimConnect.dll).
# SimConnect.dll is Microsoft's: it's copied from the MSFS 2024 SDK into the published apps at build time
# (owner's decision, 2026-11-11, see TODO section 5) and is never committed to git.
# The SPAD.neXt gauge set is not in these installers: it's its own download (tools\make-spad-repo.ps1).
param([string]$Version = "0.9.0")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$release = Join-Path $root "publish\release"
$out = Join-Path $root "publish\installers"

# 1. Tools this script needs, with a plain message if one is missing.
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 isn't installed (it builds the installers). Install it with: winget install JRSoftware.InnoSetup" }
$sdk = @($env:MSFS2024_SDK, $env:MSFS_SDK) | Where-Object { $_ } | Select-Object -First 1
$simConnect = if ($sdk) { Join-Path $sdk "SimConnect SDK\lib\SimConnect.dll" } else { $null }
if (-not $simConnect -or -not (Test-Path $simConnect)) { throw "SimConnect.dll wasn't found: install the MSFS 2024 SDK (it sets MSFS2024_SDK)." }

# 2. The tests must pass before anything ships.
dotnet test --nologo -v quiet
if ($LASTEXITCODE) { throw "Tests failed: not building installers." }

# 3. Publish both apps (one self-contained exe each, plus their gauges and font).
if (Test-Path $release) { Remove-Item $release -Recurse -Force }
# FIPinator 3000 is built too when its source is here (the FIPinator Live repo alone has only Live).
$has3000 = Test-Path (Join-Path $root "src\Fipinator.GaugeMaker")
$apps = [System.Collections.Generic.List[object]]::new()
$apps.Add(@("src\Fipinator.App", "FIPinatorLive"))
if ($has3000) { $apps.Add(@("src\Fipinator.GaugeMaker", "FIPinator3000")) }
foreach ($p in $apps) {
    $dir = Join-Path $release $p[1]
    dotnet publish $p[0] -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:Version=$Version -o $dir --nologo -v quiet
    if ($LASTEXITCODE) { throw "Building $($p[0]) failed." }
    # Microsoft's SimConnect.dll next to the program (the apps look there first), and who made what.
    Copy-Item $simConnect $dir
    Copy-Item (Join-Path $root "LICENSE") (Join-Path $dir "LICENSE.txt")
    @"
FIPinator is free and open source (GNU AGPL-3.0, see LICENSE.txt).
Source code: https://github.com/Pribilof-Hangar/fipinator-live (FIPinator 3000: https://github.com/Pribilof-Hangar/fipinator-3000)

Included from others:
- SimConnect.dll: Microsoft Corporation, from the Microsoft Flight Simulator 2024 SDK. It lets FIPinator
  talk to the sim. Not made by FIPinator, and not covered by FIPinator's licence.
- Barlow Condensed font: The Barlow Project Authors, SIL Open Font License 1.1 (see OFL.txt).

FIPinator is not affiliated with Microsoft, Logitech, PMDG or SPAD.neXt.
"@ | ForEach-Object { [System.IO.File]::WriteAllText((Join-Path $dir "THIRD-PARTY-NOTICES.txt"), $_, (New-Object System.Text.UTF8Encoding $false)) }
}

# 4. The installers.
New-Item -ItemType Directory -Force $out | Out-Null
# 3000 first: Live's installer carries 3000's installer for its "Also install FIPinator 3000" tick box.
$setups = if ($has3000 -and (Test-Path "installer\FIPinator3000.iss")) { @("installer\FIPinator3000.iss", "installer\FIPinatorLive.iss") } else { @("installer\FIPinatorLive.iss") }
foreach ($iss in $setups) {
    & $iscc "/DAppVersion=$Version" "/Q" $iss
    if ($LASTEXITCODE) { throw "Inno Setup failed on $iss." }
}
Get-ChildItem $out -Filter "*$Version.exe" | ForEach-Object { "{0}  {1:N0} MB" -f $_.Name, ($_.Length / 1MB) }
