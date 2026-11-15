# FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
# https://github.com/Pribilof-Hangar/fipinator-live
# Rebuilds the generic classic gauge sets (gauges\classic\single, twin) with the classic pack. No four-engine
# set: the DC-6 set covers it (its extra gauges come from "dc6extras" below).
#   powershell -File tools\make-classic-gauges.ps1
# Typical aircraft numbers are in ClassicPack.Typical (src\Fipinator.Core\ClassicPack.cs). The
# attitude, altimeter and vertical speed come from gauges\dc6, re-cased in the Classic look.
# These folders are generated: change the code or this script, not the files.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
dotnet build src\Fipinator.Preview -v quiet --nologo
if ($LASTEXITCODE) { throw "Building the preview tool failed." }
$preview = Join-Path $root "src\Fipinator.Preview\bin\Debug\net10.0\fipinator-preview.exe"
foreach ($set in @(@(1, "single"), @(2, "twin"))) {
    $out = Join-Path $root "gauges\classic\$($set[1])"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    & $preview classic $set[0] $out (Join-Path $root "gauges\dc6")
    if ($LASTEXITCODE) { throw "Building the $($set[1]) set failed." }
}

# The DC-6 cockpit's directional gyro, turn and slip and clock, in the DC-6 look (gauges\dc6\heading.json ...).
& $preview dc6extras (Join-Path $root "gauges\dc6")
if ($LASTEXITCODE) { throw "Building the DC-6 extras failed." }

# Radios don't depend on the number of engines: one nav set for all, re-cased from the DC-6 set
# (same needles, knobs and stock sim values).
$nav = Join-Path $root "gauges\classic\nav"
if (Test-Path $nav) { Remove-Item $nav -Recurse -Force }
foreach ($g in @(@("course-ils", "Course / ILS"), @("rmi-vor-adf", "RMI VOR 2 + ADF 1"), @("rmi-vor12-adf12", "RMI VOR 1 + VOR 2 + ADF 1 + ADF 2"), @("rmi-vor", "RMI VOR"), @("rmi-adf", "RMI ADF"), @("dme-gps", "DME + GPS"))) {
    & $preview reskin classic (Join-Path $root "gauges\dc6\$($g[0]).json") (Join-Path $nav "$($g[0]).json") "Classic $($g[1])"
    if ($LASTEXITCODE) { throw "Re-casing $($g[0]) failed." }
}
# Turboprop engine gauges (TurbopropPack in src\Fipinator.Core\TurbopropPack.cs), one folder per plane.
foreach ($t in @(@("saab", "saab-340b"), @("caravan", "caravan"))) {
    $out = Join-Path $root "gauges\turboprop\$($t[1])"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    & $preview turboprop $t[0] $out
    if ($LASTEXITCODE) { throw "Building the $($t[1]) turboprop set failed." }
}
# Jet engine gauges (JetPack in src\Fipinator.Core\JetPack.cs): 737 MAX (modern and round looks), 727, 737-800 (PMDG).
foreach ($j in @(@("modern", "737-max"), @("classic", "737-max-round"), @("727", "727"), @("737-800", "737-800"))) {
    $out = Join-Path $root "gauges\jet\$($j[1])"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    & $preview jet $j[0] $out
    if ($LASTEXITCODE) { throw "Building the $($j[1]) jet set failed." }
}
# Ready-made templates (InstrumentTemplates): gear, flaps, trim, autopilot, radios, warnings, lights...
$tpl = Join-Path $root "gauges\templates"
if (Test-Path $tpl) { Remove-Item $tpl -Recurse -Force }
& $preview templates $tpl
if ($LASTEXITCODE) { throw "Building the templates failed." }

# DC-6 "all engines on one screen" page (MiniPanels.Dc6Engines). Its own folder: make-dc6-gauges.ps1
# rebuilds gauges\dc6\engines from the DC-6 Gauges designs and would remove it from there.
& $preview dc6mini (Join-Path $root "gauges\dc6\mini")
if ($LASTEXITCODE) { throw "Building the DC-6 mini panel failed." }
Write-Host "Done."
