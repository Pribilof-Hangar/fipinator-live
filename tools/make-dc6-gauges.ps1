# FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
# https://github.com/Pribilof-Hangar/fipinator-live
# Rebuilds FIPinator's PMDG DC-6 engine and overhead gauges from the owner's DC-6 Gauges designs.
#   powershell -File tools\make-dc6-gauges.ps1 -Defs <path to the DC-6 Gauges project's web\gauge-defs.js>
# 1. tools\import-dc6-gauges.js converts every DC-6 Gauges design. Gauges for one engine or a pair
#    (BMEP 1, RPM 1-2, ...) go to gauges\dc6\parts: building blocks the apps never show.
# 2. The parts are combined into 1-4 gauges (all four engines on one gauge) in gauges\dc6\engines
#    and gauges\dc6\overhead, with the same combining the Gauge Maker's "Combine gauges" uses.
# The engines, overhead and parts folders are generated: edit the DC-6 Gauges designs or this
# script, not the files.
param([Parameter(Mandatory = $true, HelpMessage = "Path to the DC-6 Gauges project's web\gauge-defs.js")][string]$Defs)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$dc6 = Join-Path $root "gauges\dc6"
Set-Location $root

foreach ($folder in "engines", "overhead", "parts", "combos") {
    $p = Join-Path $dc6 $folder
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}
node tools\import-dc6-gauges.js $Defs $dc6
if ($LASTEXITCODE) { throw "Import failed." }

dotnet build src\Fipinator.Preview -v quiet --nologo
if ($LASTEXITCODE) { throw "Building the preview tool failed." }
$preview = Join-Path $root "src\Fipinator.Preview\bin\Debug\net10.0\fipinator-preview.exe"
$parts = Join-Path $dc6 "parts"

function Combine($how, $name, $out, [string[]]$from) {
    & $preview combine $how $name (Join-Path $dc6 $out) @($from | ForEach-Object { Join-Path $parts $_ })
    if ($LASTEXITCODE) { throw "Combining $name failed." }
}

# All four engines' needles on one dial.
Combine needles "DC-6 Manifold pressure 1-4" "engines\manifold-pressure-1-4.json" "engines\manifold-pressure-1-2.json", "engines\manifold-pressure-3-4.json"
Combine needles "DC-6 RPM 1-4"               "engines\rpm-1-4.json"               "engines\rpm-1-2.json", "engines\rpm-3-4.json"
Combine needles "DC-6 Fuel flow 1-4"         "engines\fuel-flow-1-4.json"         "engines\fuel-flow-1-2.json", "engines\fuel-flow-3-4.json"
Combine needles "DC-6 BMEP 1-4"              "engines\bmep-1-4.json"              "engines\bmep-1.json", "engines\bmep-2.json", "engines\bmep-3.json", "engines\bmep-4.json"

# Twin dials (engines 1-2 and 3-4 on separate dials in the cockpit): side by side.
foreach ($pair in @(
    @("fuel-pressure", "Fuel pressure"), @("oil-pressure", "Oil pressure"), @("oil-temperature", "Oil temperature"),
    @("cylinder-head-temperature", "Cylinder head temperature"), @("water-pressure", "Water pressure"),
    @("carburetor-air-temperature", "Carburetor air temperature"))) {
    Combine side "DC-6 $($pair[1]) 1-4" "engines\$($pair[0])-1-4.json" "engines\$($pair[0])-1-2.json", "engines\$($pair[0])-3-4.json"
}
Combine side "DC-6 Oil quantity 1-4" "overhead\oil-quantity-1-4.json" "overhead\oil-quantity-1-2.json", "overhead\oil-quantity-3-4.json"
Combine side "DC-6 Water tanks 1-4"  "overhead\water-tanks-1-4.json"  "overhead\water-tanks-1-2.json", "overhead\water-tanks-3-4.json"

Write-Host "Done. Gauges: $((Get-ChildItem $dc6\engines, $dc6\overhead -Filter *.json).Count); parts: $((Get-ChildItem $parts -Recurse -Filter *.json).Count)."
