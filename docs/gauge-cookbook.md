# Gauge cookbook

How the DC-6 gauges were made, as reusable recipes. Use it to build gauges for another
aircraft by hand today, and as the basis for templates and help text in FIPinator 3000 (the gauge maker).
File format details: [gauge-format.md](gauge-format.md). Working examples: `gauges/dc6/`.

## The process that worked

1. **Look at the real instrument.** Take a screenshot of the cockpit gauge in the sim and crop it
   large (keep screenshots private; they're the aircraft maker's artwork). Note: what's fixed,
   what turns, what slides, what's a number readout, and the scale markings.
2. **Read the scale by eye.** For each labelled mark, estimate its angle (clockwise from 12
   o'clock) from the dial center. Uneven dials become `points` scales; dials that turn more than
   once use angles past 360. Being within a few degrees is fine; the flight check catches the rest.
3. **Pick the sim values** (see the table below). Prefer what the cockpit instrument itself
   shows (e.g. `HSI CDI NEEDLE`, not `NAV CDI`), and use `expr` for any math.
4. **Draw it as original artwork** in the same style: plate, bezel, dial, ticks, labels, moving
   parts, hub, glare. Don't trace or copy the aircraft maker's images.
5. **Preview at several values** with
   `dotnet run --project src/Fipinator.Preview -- <gauge.json> --set name=value --zoom 2`, side by side.
   Check: nothing overlaps (labels vs. ticks, text vs. inner rings), moving parts read clearly.
6. **Compare it with the cockpit, adjust, then put it on a FIP.**
7. **Check in flight against the cockpit**, recording the sim's numbers at the same time
   (`dotnet run --project src/Fipinator.FipTool -- watch <a gauge listing the values>`). Directions (pitch, bank, glideslope)
   are the usual things that need flipping: a one-line scale change.

## Recipes

| Gauge | Built from | Key ideas |
|---|---|---|
| Airspeed (two-turn) | `points` scale past 360°; outer labels for turn 1, inner dotted ring + labels for turn 2 | Dots = `ticks` with tiny `length` and wide `width`; colored ranges with `arc` |
| Vertical speed | `points` scale with 0 at 270° (9 o'clock); `labels` with `texts` to show ".5", "1"… | Same numbers both sides of zero via `texts` |
| Altimeter | Three `needle`s on one value with `wrap` scales (1,000 / 10,000 / 100,000 ft per turn) | Pressure window: dark `rect` + `text` with `value` and `format: "0.00"` |
| RMI | `group` rotated by heading with scale 0→−360 (the card); fixed `triangle` ticks on the bezel; `arrow` / `doubleArrow` needles | Needle angle relative to the nose: ADF = `ADF RADIAL`; VOR = `wrap360(radial + 180 − heading)` |
| Heading bug | A small two-block shape in a `group` rotated by the bug value, *inside* the card group | Knobs: `HEADING_BUG_INC/DEC`; digit box with the rounded value |
| Course / ILS | Card group rotated by OBS; vertical bar in a `group` with `moveX`; horizontal bar with `moveY`; both in a `clip`ped group | Deviation scales map ±127 to ±70 px; digit box for the course |
| Attitude (standard) | Outer `group` rotated by bank, containing the bank ring and an inner `clip`ped `group` moved by pitch (3 px per degree) | Fixed yellow pointer and airplane drawn last, outside the moving groups |
| DME / GPS readout | Text layers with `value` and `format`; dashes shown instead when there's no signal | See "show/hide" below |

## Tricks worth reusing

- **Show or hide a part by a value:** put it in a `group` with `moveX` driven by
  `choose(signal, 0, 400)` and a `px` scale (−1000→−1000 … 1000→1000). Hidden = moved far off the
  dial (with a `clip`, it disappears). Used for NAV/GS flags, TO/FROM triangles, the GPS light,
  and "- - -" when there's no DME. (A proper `visible` field would be cleaner.)
- **Round for a digit box:** `wrap360(v + 0.5 - (v + 0.5) % 1)` gives the nearest whole degree,
  with 360 shown as 000.
- **Keep needles readable:** draw digit boxes *before* needles and bars, so moving parts pass
  over the box instead of hiding under it.
- **Readability on the FIP:** the screen is small; thicker needles (3+ px), labels 14 px or more,
  and strong colors (yellow for VOR, orange for the heading bug) help. The owner asked for
  thicker and brighter more than once.
- **No signal:** RMI needles park at 3 o'clock (`choose(signal, bearing, 90)`).
- **At rest without the sim:** FIPinator feeds 0 for every sim value and shows "NO SIM DATA";
  design gauges so that state looks sensible (needles at zero, flags showing).

## Sim values that proved right (MSFS 2024, PMDG DC-6)

| What | Value (unit) | Notes |
|---|---|---|
| Indicated airspeed | `AIRSPEED INDICATED` (knots) | |
| Vertical speed | `VERTICAL SPEED` (feet per minute) | |
| Altitude / pressure | `INDICATED ALTITUDE` (feet), `KOHLSMAN SETTING HG` (inHg) | |
| Attitude | `ATTITUDE INDICATOR PITCH DEGREES`, `... BANK DEGREES` (degrees) | In MSFS nose up is negative pitch; the direction was fixed and checked in flight on 2026-11-11 (see sim-interface-notes.md). |
| Heading | `PLANE HEADING DEGREES MAGNETIC` (degrees) | |
| Heading bug | `AUTOPILOT HEADING LOCK DIR` (degrees) | |
| VOR radial | `NAV RADIAL:n` (degrees) | Reported −180…180; radial *from* the station |
| Course deviation | `HSI CDI NEEDLE`, `HSI GSI NEEDLE` (number, ±127) | Follow GPS when it drives NAV 1; `NAV CDI:1` doesn't. A positive CDI value means the course is to the left, so our gauges negate it (fixed 2026-11-11). GSI negative = below the glideslope = bar up |
| GPS drives NAV 1 | `GPS DRIVES NAV1` (bool) | |
| Signals | `NAV HAS NAV:n`, `NAV HAS GLIDE SLOPE:1`, `NAV HAS DME:2`, `ADF SIGNAL:n` | |
| DME | `NAV DME:2` (nautical miles) | |
| GPS | `GPS WP DISTANCE` (nautical miles), `GPS WP ETE` (seconds), `GPS GROUND SPEED` (knots), `GPS IS ACTIVE WAY POINT` (bool) | |
| ADF bearing | `ADF RADIAL:n` (degrees) | Assumed relative to the nose; not yet checked with an NDB |
| Not recognised | `CIRCUIT GPS ON` | |

Knob events that work in the DC-6: `KOHLSMAN_INC/DEC`, `VOR1_OBI_INC/DEC`, `HEADING_BUG_INC/DEC`.

## Mistakes we made (so the Gauge Maker can prevent them)

- Glideslope bar upside down: always test directions in flight, not just in previews.
- Reading `NAV CDI` instead of the HSI value: the bar was pinned during GPS flying.
- Labels and text overlapping rings or ticks: preview at extreme values (0, max, both sides).
- Symbols missing from the font (arrows showed as boxes): stick to plain characters or check the
  font has them.
- DME bearing stuck on 180: with a DME signal but no VOR, `NAV RADIAL` reads 0. Anything worked
  out from a radial must check `NAV HAS NAV`, not just `NAV HAS DME`.
