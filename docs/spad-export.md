# SPAD.neXt export: how it works and how to fix it

FIPinator 3000 saves a copy of each gauge for SPAD.neXt, so SPAD.neXt users get the same gauges.
This page is for anyone who forks FIPinator and wants to improve that part, with or without an AI helper.
The original author doesn't maintain it, so **you are welcome to fix it and share your fix**.

FIPinator Live doesn't need any of this: every gauge works in Live as it is. The SPAD copies are an extra.
Today 164 of the 165 shipped gauges go to SPAD.neXt; `Check` says why the others don't (yet).

## The rules (please keep them)

- **Public sources only.** Everything here is written from SPAD.neXt's public gauge schema
  (`gauge.xsd` in [github.com/SPAD-neXt/DemoCode](https://github.com/SPAD-neXt/DemoCode), MIT licence),
  SPAD.neXt's public docs ([docs.spadnext.com](https://docs.spadnext.com)), and Microsoft's public XML
  gauge docs ([Creating XML Gauges](https://learn.microsoft.com/en-us/previous-versions/microsoft-esp/cc526953(v=msdn.10))).
  SPAD.neXt's format uses the same names (Element, Image, Rotate, Shift, MaskImage, Nonlinearity).
- **Never copy** from paid or third-party gauges (fipgauges.com, SPAD.neXt's own gauges, PMDG, etc.), and
  never unpack them to see how they work.
- **Never write into SPAD.neXt's own folders or settings.** The copies go to our own folder, and the user
  adds that folder once in SPAD.neXt ("Add location").
- **These stay FIPinator Live only:** left-knob page flipping, flipping pages by itself, and S-button knob
  modes (S buttons choosing what the knobs set).

## Where things are

| What | Where |
|---|---|
| The export | `src/Fipinator.Core/SpadExport.cs` |
| What stops a gauge from going to SPAD | `SpadExport.Check` (plain-words list) |
| Knob names in SPAD's schema | `SpadExport.KnobBindings` |
| Turning cards (compass card, heading bug) | `SpadExport.CardElement` |
| Sliding parts (ILS bars, flags) | `SpadExport.SlideElement` |
| Needles, number displays, growing arcs | `NeedleElement`, `NumberElements`, `ArcElement` |
| Tests | `tests/Fipinator.Core.Tests/SpadExportTests.cs` |
| A small "SPAD player" for the tests | `tests/Fipinator.Core.Tests/SpadPlayer.cs`: reads our SPAD file, works out every part from sim values, draws it, and the test compares that with FIPinator's own drawing |
| Export one gauge by hand | `dotnet run --project src/Fipinator.Preview -- spad <gauge.json> <folder>` |
| The ready-made set | Made at install (`FIPinator3000.exe --make-spad-gauges`) into `Documents\FIPinator 3000\FIPinator SPAD Gauges (FREE)\FIPinator` |

## How a gauge becomes a SPAD gauge

The gauge's layers are walked in drawing order and cut into pieces:

- **Still parts** (dial face, ticks, numbers, screws) become pictures: the first one is `background.png`,
  later ones are see-through `coverN.png` pictures drawn over the moving parts below them.
- **Needles** are drawn alone, pointing right, cut to size (`needleN.png`). SPAD turns them around the pivot.
  The angle comes from a `Nonlinearity` table: points on a circle, at most 10 degrees apart.
- **Number displays** are one picture per digit (0-9, blank, minus), picked by value with SPAD's `Dynamic`
  image (`numberN_{VALUE}.png`). Thousands commas are pictures that show once the number is big enough. Formats with
  words or signs (`"0%"`, `"000°"`, `"0 KT"`, `"+0000;-0000;0000"`, `".00"`) are read by `NumberStyleOf`: a sign
  sits in the digit slot left of the first digit (pictures 12-14), words after the digits are a still picture, `%`
  multiplies by 100. SPAD doesn't round with `near`: use `0.5 + flr`.
- **Growing arcs** are 60 pictures from empty to full, picked by value.
- **Turning cards** (compass card, ADF card, turn coordinator airplane) are drawn upright on their own and
  turned by SPAD around the card's centre, like a big needle. A heading bug on the card turns by the card's
  angle plus its own. A card on an uneven scale (radar altimeter DH bug) gets a table of points instead.
- **Sliding parts** (ILS course and glideslope bars, warning flags, lights that slide away to hide) are drawn
  at every position they reach, about 2 pixels apart (at most 161 pictures), each already cut to the dial
  window, and SPAD picks one by value (`slideN_{VALUE}.png`, like growing arcs). SPAD's own `Shift` +
  `MaskImage` was tried first: on a real FIP, SPAD drew the mask pictures as white circles over the gauge.
- **Attitude horizons** (a part that slides inside a turning card) are one picture, level and uncut, that SPAD
  slides (`Shift`, pitch) and turns (`Rotate` in radians, bank) in the same element, like SPAD's bundled Saitek
  attitude gauge (`HorizonElement`). SPAD has no clip, so right after it come copies of everything drawn under it
  with the round window cut out: they hide what slides outside the window (the bundled gauge does the same
  with a picture on top). The windows must be circles round the turning centre (`HorizonProblem`).
- **HSIs**: card, heading bug and course pointer are cards; the CDI bar and TO/FROM flags are horizon-style
  elements (`Shift` + `Rotate`, the turns added up); a part that only slides out of sight inside a sliding part
  (the glideslope pointer's hide) adds a blank picture to that part's picture set (`Hides`).
- **Knobs** that send a plain sim event go in as `<Mouse>` blocks (see below); **S buttons** as `Button` lines in
  `GaugeSettings/Buttons`. A user's own SPAD.neXt button settings win over them.
- Worked-out values (formulas) are translated to SPAD's RPN by `ValueRpn`.

Everything goes into one `.zip` (pictures in its `1024` folder, which is where SPAD.neXt looks).

## What has been checked on a real FIP (2026-11-11)

Checked by the owner in SPAD.neXt with MSFS 2024, and in SPAD's log (read only):

- **Knobs work**: each knob is a `<Mouse>` block (the old Microsoft FS gauge way SPAD's schema describes:
  `Mouse` > `Area` > `Area` with a `DownArrow` or `UpArrow` cursor and a `Click Event`), one block per knob,
  right knob first, `DownArrow` = anticlockwise. SPAD's log shows the events sent and accepted. `Button` lines
  in `GaugeSettings/Buttons` (`FIP_DIAL_RIGHT_...` or `FIP_RIGHTDIAL_...`) did **nothing** for knobs.
- **ILS bars** (picture sets) "spot on"; **attitude horizon**, **HSI** (card, bug, course, CDI bar,
  glideslope), **RMI cards**, altimeter and engine needles all worked.
- **Two direction bugs** were found in flight and fixed in Live and SPAD alike: the course needle's left/right
  (a positive `HSI CDI NEEDLE` means the course is to the left, so we negate it) and the attitude's pitch
  (nose up is negative). Bank direction is right (sim's positive bank = left; checked 2026-11-11). SPAD slides a
  positive `Shift` `Scale Y` upward, so horizons get a negated `Scale Y` (seen on a FIP, 2026-11-11).

Still to check on a FIP: which `<Mouse>` block SPAD gives to which knob on a gauge with two different knobs
(heading indicator: right knob = gyro, left = heading bug; swap the order in `Export` if they're the wrong way
round), S buttons on our own gauges (`Button` lines; only the template gauges use them), and growing arcs.

What we've learned about SPAD and the sim, with sources, is in [sim-interface-notes.md](sim-interface-notes.md).

## Glass horizons, hiding parts and two-way slides (2026-11-11)

- **Box windows**: a horizon may sit in a rectangle (737/Saab EADI) as long as the window is fixed on the screen
  and nothing turning is drawn under it; the cover trick works for any fixed shape (`HorizonProblem`).
- **Parts that move alike share one picture**: the sky/ground and the pitch ladder in its own round window (same
  turns, same pitch) are drawn as one horizon picture, the ladder kept inside its window (`SameHorizonParts`).
  Drawn on a sheet five times the FIP's size, so steep pitch and bank never show an edge.
- **Hiding parts**: a part that only slides out of sight ("choose(condition, 0, 400)", recognised from the formula
  even when the sample values never reach it: `HideShifts`) adds a blank picture to the picture set of whatever
  it holds. A number on a hiding part blanks every digit, sign, point, comma and word (`NumberElements`, `hidden`).
- **Two-way slides** (the ADI's localizer pointer moves sideways and up): a grid of pictures, about 3 px apart,
  picked by both values (`SlideElement`).

## Still to do (1 gauge not in SPAD yet: `Check` lists it)

- **The 737 standby** ("Jet horizon (compact)"): its speed and altitude tapes roll, and the numbers on them change
  as they roll ("a number riding on a sliding part"). Idea: the tape's numbers as digit elements that also slide
  (`Shift` + a `Rotate` of 0, the combination SPAD has shown it handles) inside a box window covered like the
  EADI's. Not tried yet.

## How to check a change

1. Run the tests: `dotnet test tests/Fipinator.Core.Tests`. `Every_spad_gauge_redraws_like_live` redraws every
   gauge that goes to SPAD the SPAD way and compares it with FIPinator's own drawing; it fails (and keeps both
   pictures in `%TEMP%\fipinator-spad-check`) when they differ. Our SPAD player follows what SPAD has been seen to
   do; when a FIP shows otherwise, fix the player too, so the same mistake can't pass twice.
2. Export it by hand (see the table above), add that folder in SPAD.neXt once, and look at it on a FIP with the
   sim running. SPAD.neXt's log says which picture it couldn't find.
3. Write down what you tested and on what (PROGRESS.md has examples), and what you couldn't test.
