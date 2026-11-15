# FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
# https://github.com/Pribilof-Hangar/fipinator-live
# Makes the "FIPinator gauges for SPAD.neXt" repo: a folder ready to become its own GitHub repo.
#   powershell -ExecutionPolicy Bypass -File tools\make-spad-repo.ps1 [-Out publish\spad-gauges]
# Everything in it is generated from this repo. Never edit it by hand: fix the export or the gauges here, then
# run this again, so both stay in step. What it holds (and nothing else; the script checks):
#   README.md                       the front page
#   AI-README.md                    what had to be worked around in SPAD.neXt (tools\spad-repo\AI-README.md)
#   collage.png                     the tested DC-6 gauges at a glance
#   gauges\<group>\*.zip            one SPAD.neXt gauge each (users add the "gauges" folder in SPAD.neXt once)
#   gauges\<group>\README.md        that group: tested or not, a collage, each gauge with what its knobs do
#   previews\<group>\*.png          what each gauge looks like (plus <group>.png, the group's collage)
#   downloads\<group>.zip           one set, ready to unzip and add in SPAD.neXt (with the AI-README and licences)
#   downloads\All gauges.zip        every set in one download (the same, all groups)
#   LICENSE, OFL.txt
param([string]$Out = "publish\spad-gauges")
$ErrorActionPreference = "Stop"

# ---- Links: fill these in when the repos are public (empty = "coming soon", never a dead link). ----
$MainRepo = "https://github.com/Pribilof-Hangar/fipinator-live"     # created 2026-11-14 (private until the owner publishes)
$SpadRepo = "https://github.com/Pribilof-Hangar/fipinator-spad-gauges"   # created private 2026-11-11, Issues on
# ---- Groups flown with on real FIPs (the rest: made for fun, not tested). ----
$TestedGroups = @("DC-6")
# ---- Groups the owner has flown some of (in FIPinator Live; the SPAD versions come from the same files). ----
$PartlyTested = @("Triple jet (Boeing 727)", "Dual turboprop")
# ---- Gauges in a tested group that were added later and haven't been flown with yet. ----
$Untested = @()   # the 8 later DC-6 extras were flown with on 2026-11-12 and looked good
# ---- The front-page collage: tested DC-6 gauges, in this order. ----
$Highlights = @("DC-6 Airspeed", "DC-6 Attitude", "DC-6 Altimeter", "DC-6 Vertical Speed",
                "DC-6 Course - ILS", "DC-6 HSI", "DC-6 RMI (VOR 1 + VOR 2 + ADF 1 + ADF 2)", "DC-6 RPM 1-4")

$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$out = Join-Path $root $Out
# Empty the folder rather than delete it (Explorer or an editor may have it open).
if (Test-Path $out) { Get-ChildItem $out -Force | Remove-Item -Recurse -Force }
$gauges = Join-Path $out "gauges"
$previews = Join-Path $out "previews"
$downloads = Join-Path $out "downloads"
New-Item -ItemType Directory -Force $gauges, $previews, $downloads | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Drawing
$utf8 = New-Object System.Text.UTF8Encoding $false
function Write-Text($path, $text) { [System.IO.File]::WriteAllText($path, $text, $utf8) }
function Url($path) { ($path -replace '\\', '/') -replace ' ', '%20' -replace '\(', '%28' -replace '\)', '%29' }
function Link($url, $text) { if ($url) { "[$text]($url)" } else { "$text *(link coming soon)*" } }

# 1. The SPAD gauges, made by the export.
dotnet build src\Fipinator.Preview -v quiet --nologo
if ($LASTEXITCODE) { throw "Building the preview tool failed." }
& (Join-Path $root "src\Fipinator.Preview\bin\Debug\net10.0\fipinator-preview.exe") spadset (Join-Path $root "gauges") $gauges
if ($LASTEXITCODE) { throw "Making the SPAD gauges failed." }
# No PMDG 737 gauges in the SPAD set (owner, 2026-11-14): the PMDG 737 keeps its data to itself; known issue.
$pmdg737 = Join-Path $gauges "Boeing 737"
if (Test-Path $pmdg737) { Remove-Item $pmdg737 -Recurse -Force }

# 2. Each gauge's preview picture and description (both inside its zip).
$list = @()
foreach ($zip in Get-ChildItem $gauges -Recurse -Filter *.zip | Sort-Object FullName) {
    $group = $zip.Directory.Name
    $name = $zip.BaseName -replace '^FIPinator - ', ''
    $dir = Join-Path $previews $group
    New-Item -ItemType Directory -Force $dir | Out-Null
    $png = Join-Path $dir "$name.png"
    $description = ""
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zip.FullName)
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -eq "1024/preview.png") { [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $png, $true) }
            if ($entry.FullName -like "*.xml" -and $entry.FullName -notlike "*/*") {
                $reader = New-Object System.IO.StreamReader($entry.Open())
                try { [xml]$xml = $reader.ReadToEnd() } finally { $reader.Dispose() }
                $description = [string]$xml.Gauge.GaugeSettings.Description
            }
        }
    } finally { $archive.Dispose() }
    # Our own sentence about FIPinator and the licence is on the front page; keep the gauge's own words.
    $description = ($description -replace '\s*Made with FIPinator 3000\..*$', '').Trim()
    $list += [pscustomobject]@{ Group = $group; Name = $name; Png = $png; Description = $description }
}

# 3. Collages: pictures side by side in a grid (4 across), on a dark background.
function New-Collage($pngs, $path, $cols = 4) {
    $w = 320; $h = 240; $gap = 8
    $rows = [Math]::Ceiling($pngs.Count / $cols)
    $usedCols = [Math]::Min($cols, $pngs.Count)
    $bmp = New-Object System.Drawing.Bitmap(($usedCols * ($w + $gap) + $gap), ($rows * ($h + $gap) + $gap))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.Clear([System.Drawing.Color]::FromArgb(24, 24, 27))
        for ($i = 0; $i -lt $pngs.Count; $i++) {
            $img = [System.Drawing.Image]::FromFile($pngs[$i])
            try { $g.DrawImage($img, $gap + ($i % $cols) * ($w + $gap), $gap + [Math]::Floor($i / $cols) * ($h + $gap), $w, $h) } finally { $img.Dispose() }
        }
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $g.Dispose(); $bmp.Dispose() }
}
$top = foreach ($h in $Highlights) { ($list | Where-Object { $_.Name -eq $h } | Select-Object -First 1).Png }
$top = @($top | Where-Object { $_ })
if ($top.Count -lt $Highlights.Count) { Write-Warning "Some front-page gauges weren't found; check the names in `$Highlights." }
New-Collage $top (Join-Path $out "collage.png")

# 4. Each group: a collage (up to 8), a README, and a one-download zip.
$groups = $list | Group-Object Group | Sort-Object Name
foreach ($g in $groups) {
    $name = $g.Name
    $tested = $TestedGroups -contains $name
    $partly = $PartlyTested -contains $name
    $collage = Join-Path $previews "$name.png"
    New-Collage @($g.Group | Select-Object -First 8 | ForEach-Object { $_.Png }) $collage
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("# $name")
    [void]$sb.AppendLine("")
    if ($tested) {
        [void]$sb.AppendLine("**Flown with on real FIPs** (SPAD.neXt, MSFS 2024, the PMDG DC-6). These are the gauges this project was made for.")
        $later = @($g.Group | Where-Object { $Untested -contains $_.Name } | ForEach-Object { $_.Name })
        if ($later.Count -gt 0) { [void]$sb.AppendLine("A few were added later and **haven't been flown with yet**: $($later -join ', '). Marked *(not tested)* below.") }
    } elseif ($partly) {
        [void]$sb.AppendLine("**Partly flown with**: some of these were flown with in FIPinator Live (the SPAD.neXt versions are made from the same files), not all of them. Testers welcome: if something's off, fix it in your fork and share it.")
    } else {
        [void]$sb.AppendLine("**Made for fun, not tested on a FIP yet.** Testers welcome: if something's off, fix it in your fork and share it.")
    }
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("![$name](../../previews/$(Url "$name.png"))")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("$($g.Count) gauges. **Download this set: [$name.zip](../../downloads/$(Url "$name.zip"))**, unzip it, and add the folder in SPAD.neXt (see the [front page](../../README.md)). Or get every set at once: [All gauges.zip](../../downloads/All%20gauges.zip).")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Gauge | | About it |")
    [void]$sb.AppendLine("|---|---|---|")
    foreach ($item in $g.Group) {
        $about = if ($item.Description) { $item.Description -replace '\|', '/' } else { "" }
        $mark = if ($tested -and ($Untested -contains $item.Name)) { " *(not tested)*" } else { "" }
        [void]$sb.AppendLine("| **$($item.Name)**$mark | <img src=""../../previews/$(Url "$name/$($item.Name).png")"" width=""200"" alt=""$($item.Name)""> | $about |")
    }
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("Free and open source (AGPL-3.0), made from scratch. See the [front page](../../README.md).")
    Write-Text (Join-Path $gauges "$name\README.md") $sb.ToString()
}

# 5. The front page.
$count = $list.Count
$groupLines = ($groups | ForEach-Object {
    $t = if ($TestedGroups -contains $_.Name) { " (flown with on real FIPs)" } elseif ($PartlyTested -contains $_.Name) { " (partly flown with)" } else { "" }
    "- [**$($_.Name)**](gauges/$(Url $_.Name)/README.md): $($_.Count) gauges$t"
}) -join "`n"
$readme = @"
![FIPinator gauges for SPAD.neXt: DC-6 gauges](collage.png)

# FIPinator gauges for SPAD.neXt

**Free. Open source. Yours to change.**
$count gauges for Logitech/Saitek Flight Instrument Panels in SPAD.neXt, with knobs set up and ready to use.
No cost, no account, no catch. Licence: AGPL-3.0: use it, change it, share it. If you share a changed version,
share its source too, so it stays free for everyone.

## What's a FIP?

A FIP is the **Logitech (formerly Saitek) Flight Instrument Panel**: a small USB screen (320 x 240) with six
buttons and two knobs that sits in your home cockpit and shows one instrument at a time. It's **hardware you
have to own**, and Logitech **no longer makes it**, so today they're mostly found second-hand. These gauges are
for people who already have FIPs and run them with SPAD.neXt. They're not a free on-screen panel by themselves
(though see "No FIPs?" below).

## Made from scratch

Every gauge here was drawn and coded from scratch for this project. The look and markings come from the real
instruments and the aircraft in MSFS 2024 itself. The live values and knob commands come from the sim's own
official interface (SimConnect).

## The story

I made FIPinator for my own PMDG DC-6 in MSFS 2024, and that's mostly what I fly. The DC-6 gauges are the ones
I really know work. I've also flown some with the Boeing 727 and the Saab 340B and fixed what I found. The rest
(classic singles and twins, Cessna 404 Titan, Caravan, 737 MAX, radios, autopilot and more) I made for fun.
**Not every gauge has been tested**: most work, some may need work, and the basic layout of every gauge is already
there, so fixing one is usually a small job. If you know your way around SPAD.neXt gauges, or have an AI helper
(the AI-README is written for it), you can fix them.

For my own daily flying I use **FIPinator Live**, the app I made from scratch that these gauges come from: it
runs my FIPs, and I use SPAD.neXt for my other gear (yoke, GPS, throttle quadrant) with the FIPs switched off in
SPAD.neXt. That's not because SPAD.neXt is bad (it's great); I built Live myself and it works for me. These SPAD
versions are for everyone who'd rather keep everything in SPAD.neXt.

Compared with the paid FIP gauges I've used, these are pretty generic. I just wanted some made for the DC-6 that
worked; all the cool bells and whistles I'll leave to the paid developers, who do great work.

I got what I wanted out of this project, so **I have no plans to keep this repo updated.** If I get bored, or if
it catches on, I might come back with my AI to look at issues and make a few more gauges, but **no promises**.
It's for the world now: use it, mod it, personalise it, fork it, make it better. I like to think there are some
great people out there who can take this to a new level.

## No PMDG 737 gauges

There are no PMDG 737 gauges here. The PMDG 737 keeps most of its data (engine numbers, autopilot panel, knobs,
autobrake) to itself, so gauges for it never matched the cockpit. That's a known limit and unlikely to change
soon. The Asobo 737 MAX gauges (Dual jet groups) do work with that plane.

## Known issues

- Some numbers lose their decimals in SPAD.neXt (for example N1 shows 20 when the plane shows 20.4). Needles and
  whole numbers aren't affected. Not fixed yet; FIPinator Live shows them right.
- On the HSIs, whether SPAD.neXt slides the course bar the right way (left/right) hasn't been checked on a FIP yet.
  The ILS/course gauges and FIPinator Live are checked.

## Testers wanted

Try them in your planes. If something's off, **open an issue** and say which gauge, which plane and what happens.
Issues are open to everyone: someone else may have the same problem, someone may already know the fix and post
it, and I might get to it myself now and then. Found a fix? Post it in the issue, or fix it in your fork and
share it. Others will thank you.

## Made with AI

All of this was built with an AI assistant (Claude). The hard part is done and documented: how the gauges work,
how they become SPAD.neXt gauges, and the sim facts learned the hard way (which way the needles really point,
which knob commands work). **Point your own AI at this repo and at the FIPinator project** and ask it to change a
gauge: it should find its way around quickly.

**There's an AI helper readme for your AI to look at, if you choose to: [AI-README.md](AI-README.md).** It lists
everything that had to be worked around to make these gauges work in SPAD.neXt. How the SPAD.neXt versions are made:
$(Link $(if ($MainRepo) { "$MainRepo/blob/main/docs/spad-export.md" } else { "" }) "how the SPAD.neXt export works").

## How to use them

No installer: just a zip.

1. **Download** [All gauges.zip](downloads/All%20gauges.zip): every set in one small zip (about 13 MB). That's what
   most people should grab: any gauge can be used in any plane you like, or changed to fit yours. (Each set also
   has its own zip in the [downloads](downloads) folder.) Every zip has the AI-README and the licences inside.
2. **Unzip it** somewhere that stays (for example ``Documents\FIPinator SPAD gauges``), not inside SPAD.neXt's own
   folder. Don't unzip the gauge files inside it: SPAD.neXt reads them as they are.
3. In SPAD.neXt, open a FIP's gauge list > **Add location** > pick the folder you unzipped.
4. Pick a gauge: they're listed under "FIPinator: ...". Page up/down flips between the gauges on a FIP.

To update: download again and replace the folder.

## Works with

Made for **Windows 11 and MSFS 2024**. The gauges use the sim's standard values and commands, so they *might*
work in MSFS 2020, but nobody has tried. Study-level planes (PMDG and others) may ignore some standard knob
commands.

**No FIPs?** The paid version of SPAD.neXt can show FIP gauges in a window on another screen, with no FIP
hardware at all ("virtual FIPs"). Just a suggestion: I haven't tried it with these gauges, and I'm not sure the
option still works; I've only seen it in this [YouTube tutorial](https://www.youtube.com/watch?v=Wn3m-spXex4).
But with AI, just about anything is possible now.

## Not all of them are realistic

Most gauges follow the real instrument. Some obviously don't, on purpose: they're made for pilots with only a
few FIPs, to squeeze more onto one screen and make flying feel good. For example, an RMI with four needles
(VOR 1, VOR 2, ADF 1 and ADF 2) or all four engines' numbers on one page. Use what you like and skip the rest.

## The original: FIPinator

These are a port of the gauges in **FIPinator Live**, a free app that runs them on your FIPs without SPAD.neXt,
plus extras SPAD.neXt can't do (flip gauges with the left knob, flip them by themselves, knob modes, layouts). Its
accessory, **FIPinator 3000**, changes panels and gauges and can save them for SPAD.neXt too.
$(Link $MainRepo "FIPinator Live") | $(Link "https://github.com/Pribilof-Hangar/fipinator-3000" "FIPinator 3000") | $(Link $(if ($MainRepo) { "$MainRepo/releases" } else { "" }) "download FIPinator Live")

## The gauges

$groupLines

## Licences

- Gauges: GNU AGPL-3.0 (``LICENSE``).
- The digits are drawn in the Barlow Condensed font: SIL Open Font License 1.1 (``OFL.txt``).
- All artwork original. Not made by or affiliated with SPAD.neXt, Logitech, Microsoft or PMDG.
"@
Write-Text (Join-Path $out "README.md") $readme
Copy-Item (Join-Path $root "LICENSE") (Join-Path $out "LICENSE")
Copy-Item (Join-Path $PSScriptRoot "spad-repo\AI-README.md") (Join-Path $out "AI-README.md")
Copy-Item (Join-Path $root "assets\fonts\OFL.txt") (Join-Path $out "OFL.txt")

# 6. The downloads: each set, and all of them, as folders ready to add in SPAD.neXt, each with a short how-to, the
#    AI-README and the licences inside.
$howTo = @"
FIPinator gauges for SPAD.neXt (free and open source, AGPL-3.0)

1. Keep this folder somewhere that stays (not inside SPAD.neXt's own folder). Don't unzip the gauge files in it.
2. In SPAD.neXt, open a FIP's gauge list, choose "Add location", and pick this folder.
3. Pick a gauge: they're listed under "FIPinator: ...". Page up/down flips between the gauges on a FIP.

Changing a gauge with your AI? Give it AI-README.md first.
"@
function New-Download($zipName, $folderName, $groupNames) {
    $stage = Join-Path ([System.IO.Path]::GetTempPath()) ("fipinator-spad-" + [guid]::NewGuid().ToString("N"))
    $top = Join-Path $stage $folderName
    try {
        foreach ($n in $groupNames) {
            New-Item -ItemType Directory -Force (Join-Path $top $n) | Out-Null
            Copy-Item (Join-Path $gauges "$n\*.zip") (Join-Path $top $n)
        }
        Copy-Item (Join-Path $out "AI-README.md"), (Join-Path $out "LICENSE"), (Join-Path $out "OFL.txt") $top
        Write-Text (Join-Path $top "HOW TO USE.txt") $howTo
        # Entry names with "/" (Windows PowerShell's CreateFromDirectory writes "\", which some unzip tools mishandle).
        $zip = [System.IO.Compression.ZipFile]::Open((Join-Path $downloads $zipName), [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($f in Get-ChildItem $top -Recurse -File) {
                $entry = $folderName + "/" + $f.FullName.Substring($top.Length + 1).Replace("\", "/")
                [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal)
            }
        } finally { $zip.Dispose() }
    } finally { if (Test-Path $stage) { Remove-Item $stage -Recurse -Force } }
}
foreach ($g in $groups) { New-Download "$($g.Name).zip" "FIPinator SPAD gauges - $($g.Name)" @($g.Name) }
New-Download "All gauges.zip" "FIPinator SPAD gauges" @($groups | ForEach-Object { $_.Name })

# 7. Nothing else gets in: only these kinds of files, in these places.
$bad = Get-ChildItem $out -Recurse -File | Where-Object {
    $rel = $_.FullName.Substring($out.Length + 1)
    -not ($rel -in @("README.md", "AI-README.md", "LICENSE", "OFL.txt", "collage.png") -or
          $rel -match '^gauges\\[^\\]+\\(FIPinator - .+\.zip|README\.md)$' -or
          $rel -match '^previews\\[^\\]+\.png$' -or $rel -match '^previews\\[^\\]+\\[^\\]+\.png$' -or
          $rel -match '^downloads\\[^\\]+\.zip$')
}
if ($bad) { throw "Unexpected files in the SPAD repo (nothing personal or extra may go in): $($bad.FullName -join ', ')" }
"$count gauges in $($groups.Count) groups: $out"
