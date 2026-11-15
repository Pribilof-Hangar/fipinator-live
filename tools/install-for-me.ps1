# FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
# https://github.com/Pribilof-Hangar/fipinator-live
# Developer install on this PC, until there's a real installer: builds both apps and installs them
# for the current user, with desktop and Start menu shortcuts.
#   FIPinator Live  -> %LOCALAPPDATA%\Programs\FIPinator Live\FIPinatorLive.exe
#   FIPinator 3000  -> %LOCALAPPDATA%\Programs\FIPinator 3000\FIPinator3000.exe   (the gauge maker)
# Run it through Explorer (tools\install-for-me.cmd) so it isn't inside any sandbox. It closes both
# apps first. Settings, panels, gauges and logs (Documents\FIPinator 3000, %LOCALAPPDATA%\FIPinator 3000)
# are never touched. Also cleans up the old names (FIPinator.exe, FIPinator Gauge Maker).
param([switch]$NoBuild)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$log = Join-Path $root "publish\install-log.txt"
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
Start-Transcript -Path $log -Force | Out-Null
try {
    Set-Location $root
    $live = Join-Path $root "publish\FIPinatorLive"
    $maker = Join-Path $root "publish\FIPinator3000"
    # FIPinator 3000 only when its source is here (the FIPinator Live repo alone has only Live).
    $has3000 = Test-Path (Join-Path $root "src\Fipinator.GaugeMaker")
    if (-not $NoBuild) {
        $apps = [System.Collections.Generic.List[object]]::new()
        $apps.Add(@("src\Fipinator.App", $live))
        if ($has3000) { $apps.Add(@("src\Fipinator.GaugeMaker", $maker)) }
        foreach ($p in $apps) {
            if (Test-Path $p[1]) { Remove-Item $p[1] -Recurse -Force }
            dotnet publish $p[0] -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $p[1] --nologo -v quiet
            if ($LASTEXITCODE) { throw "Building $($p[0]) failed." }
        }
    }

    # Close both apps (old and new names) and wait for them to finish.
    $names = "FIPinatorLive", "FIPinator3000", "FIPinator", "FIPinatorGaugeMaker"
    Get-Process $names -ErrorAction SilentlyContinue | ForEach-Object { $_.CloseMainWindow() | Out-Null }
    for ($i = 0; $i -lt 20 -and (Get-Process $names -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 500 }
    if (Get-Process $names -ErrorAction SilentlyContinue) { throw "FIPinator is still running. Close it and run this again." }

    $programs = Join-Path $env:LOCALAPPDATA "Programs"
    $liveDir = Join-Path $programs "FIPinator Live"
    $makerDir = Join-Path $programs "FIPinator 3000"
    # Program files only (these folders hold nothing of the user's).
    foreach ($d in @($liveDir) + $(if ($has3000) { @($makerDir, (Join-Path $programs "FIPinator Gauge Maker")) } else { @() })) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
    Copy-Item $live $liveDir -Recurse
    if ($has3000) { Copy-Item $maker $makerDir -Recurse }

    # Shortcuts: new names in, old names out.
    $shell = New-Object -ComObject WScript.Shell
    foreach ($folder in [Environment]::GetFolderPath("Desktop"), [Environment]::GetFolderPath("Programs")) {
        foreach ($old in "FIPinator.lnk", "FIPinator Gauge Maker.lnk") { $o = Join-Path $folder $old; if (Test-Path $o) { Remove-Item $o } }
        $shortcuts = [System.Collections.Generic.List[object]]::new()
        $shortcuts.Add(@("FIPinator Live", "$liveDir\FIPinatorLive.exe", "Runs your gauge panels on the Logitech FIPs"))
        if ($has3000) { $shortcuts.Add(@("FIPinator 3000", "$makerDir\FIPinator3000.exe", "Make and tweak FIP gauges")) }
        foreach ($s in $shortcuts) {
            $link = $shell.CreateShortcut((Join-Path $folder "$($s[0]).lnk"))
            $link.TargetPath = $s[1]
            $link.WorkingDirectory = Split-Path $s[1]
            $link.IconLocation = "$($s[1]),0"
            $link.Description = $s[2]
            $link.Save()
        }
    }
    # SPAD.neXt users: the SPAD versions of the built-in gauges, in Documents\FIPinator 3000\FIPinator SPAD
    # Gauges (FREE)\FIPinator. Does nothing without SPAD.neXt; a problem here never stops the install.
    if ($has3000) { try {
        $p = Start-Process "$makerDir\FIPinator3000.exe" -ArgumentList "--make-spad-gauges" -Wait -PassThru -WindowStyle Hidden
        Write-Host "SPAD gauges step finished (exit code $($p.ExitCode))."
    } catch { Write-Host "SPAD gauges step skipped: $($_.Exception.Message)" } }
    Write-Host $(if ($has3000) { "Installed FIPinator Live and FIPinator 3000." } else { "Installed FIPinator Live." })
}
finally { Stop-Transcript | Out-Null }
