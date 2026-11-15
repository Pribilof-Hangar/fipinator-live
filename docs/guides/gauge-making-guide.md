# Gauge-making guide (for you and your AI)

How to make or fix a FIPinator gauge so it matches the cockpit. It's written for an AI assistant to follow, and
for you to read along. It was built and tested with Claude; any AI that can read and write files can follow it.

FIPinator is FREE and open source (AGPL-3.0): free to use, change, share and fork. Make whatever gauges you like.

Useful files in this project: `docs/sim-interface-notes.md` (sim values and knob events already checked, per
plane), `docs/gauge-format.md` (what a gauge file can hold), `docs/gauge-cookbook.md` (worked examples),
`docs/spad-export.md` (how gauges become SPAD.neXt gauges).

## Lessons learned (read these first)

What worked:
- Planes that share their needles as sim values (default planes, the PMDG DC-6's `L:` values) make great gauges
  quickly.
- Drawing from a zoomed-in cockpit screenshot and comparing yours side by side with it.
- Recording the sim's values while someone moves a lever or knob in the cockpit.
- Checking the sim, the logs and the files yourself before asking anyone.

What went wrong (don't repeat it):
- Tweaking an old gauge a little instead of drawing what the screenshot shows.
- Changing things on weak evidence: one report reversed a needle in every gauge and it was wrong. Get exact
  numbers at the same moment, from more than one reading.
- Putting a number on a gauge before checking it against the plane's own display.
- Chasing data a plane doesn't share (the PMDG 737's EGT and fuel flow): stop and say so.
- Labelling a dial straight from a plane value: the DC-6's CHT value is the needle's place (degrees + 50), so the
  numbers were 50 °C off while the needles were right.
- Sending several quick knob clicks as one change: some planes (the FSS 727) count each change as one click.
- Guessing at a hitch instead of measuring it: FIP freezes turned out to be Logitech's driver, found only by
  timing every step over a whole flight. Measure first, change one thing, test again.
- Keeping gauges that never matched. After a fair try, drop them: ship only what matches the cockpit.

If the same problem comes back twice: stop, write down in a few lines what was tried and what happened, check the
thing everyone is taking for granted with real data, then change one thing at a time.

## Where a gauge ends up

One gauge file (`.json`) is the gauge:
- **FIPinator 3000** shows it in its list, so give it a clear name, a plain description (what it shows, what it
  reads, any limits) and sample values that make a sensible preview.
- **FIPinator Live** flies it on the FIPs. This is the easiest place to check it against the cockpit.
- **SPAD.neXt**: 3000 exports the same file as a SPAD gauge. SPAD has its own quirks (below); don't let them hold
  up the gauge itself.

## 1. Check the plane's data first (a couple of minutes, before drawing)

- With the plane loaded, read the values the gauge would use:
  `dotnet run --project src/Fipinator.FipTool -- watch <probe.json> --seconds 5`, where `probe.json` is a small
  gauge file that only lists `values`. Compare with the plane's own display at the same moment.
- Try the likely candidates side by side (plain and corrected N1/N2, engine index :1 or :2, units).
- For things that move (flaps, speedbrake, knobs), record in the background (`--seconds 180`) while someone moves
  them, then read the recording.
- If no sim value matches the plane's display, say so and leave that part off. Study-level jets (PMDG, Asobo 737
  MAX) keep a lot of their data to themselves; read the PMDG notes in `docs/sim-interface-notes.md` first.
  Planes with round dials and standard values (Cessna, Baron, DC-3, Twin Otter, Caravan, Saab, 727) are the easy
  ones.

## 2. Draw it from the screenshot

- Crop and zoom the instrument from the screenshot: that's the reference.
- Draw the gauge to match it: layout, scale direction and spacing, colours, boxes, labels, knobs.
- Where the real instrument has knobs, give the gauge working FIP knobs (check the events in the sim).
- The plane's own config files and official documents are good sources for facts (flap angles from
  `flight_model.cfg`, limits from the type certificate).
- Compare the zoomed original with your render at the same readings before moving on.

## 2b. Check every direction

Directions are easy to get backwards and hard to see on the ground. For each needle or bar driven by a deviation
or a command (course needle, glideslope, flight director bars, horizon pitch and bank, turn needle):
- Check the sim's sign with a live reading while looking at the plane's own instrument ("value +18.6, cockpit
  says fly right"). Note it in `docs/sim-interface-notes.md` with the date and plane.
- One value, one direction, in every gauge: search `gauges/` for the sim name and make sure every gauge moves the
  same way for the same sign.
- Never reverse a direction on one report, and never from a SPAD.neXt test alone: check it in FIPinator Live
  against the cockpit first.
- Checked so far: course needle (positive = right), glideslope (positive = down), pitch and bank. Not checked:
  flight director bars.

## 2c. Add-on planes and their own knobs

- Many add-on planes keep their needles and knobs as plane values (`L:...`) instead of standard sim values. The
  names are often in the plane's readable model behavior files (`ModelBehaviorDefs/**/*.xml`: tooltips name the
  knob, the code lines name the value).
- A plane value may be a needle position, not the real unit. Check it against the cockpit.
- A FIP knob can change a plane value: `"set": "L:NAME", "add": 1` (several names, comma-separated, move together).
  Test it first with `fipinator-fip nudge L:NAME 1` and watch the cockpit. Keep the left knob free for flipping
  pages.
- Counters and bugs usually come from such a value: work out the formula from two readings (FSS 727: EPR = 1 + 2 x
  value).
- Offer a few layouts: one big dial with numbered needles, engines side by side, one dial per engine.

## 3. Sim facts that keep coming up

All in `docs/sim-interface-notes.md`. Short list: nose up is negative pitch; positive bank is a left bank; a
positive `HSI CDI NEEDLE` / `NAV CDI` means fly right; the knob events are `ATTITUDE_BARS_POSITION_UP/DOWN` and
`INCREASE_/DECREASE_DECISION_HEIGHT` (not the `_INC/_DEC` forms).

## 4. FIPinator 3000

- The gauge shows up in 3000's list with its name, description and a sensible preview.
- 3000's checks either pass for SPAD.neXt or say plainly why not.

## 5. SPAD.neXt quirks

Knobs are `<Mouse>` blocks; no `near` in formulas; no picture masks (sliding parts become picture sets); a
horizon's `Shift` Y is negated; only some number formats export; SPAD seems to drop decimals from some values;
`PMDG:` values can't go to SPAD. SPAD picks up changed gauges after a restart (or after picking the gauge again).

## 6. Finishing

1. Gauges in this project are made by generators in `src/Fipinator.Core` and written to `gauges/` with
   `src/Fipinator.Preview` (commands in `tools/make-classic-gauges.ps1`). A hand-made `.json` works too.
2. Render it at a few readings and look at it.
3. Run the tests: `dotnet test tests/Fipinator.Core.Tests -c Release`. They redraw every gauge the SPAD way.
4. Note what you learned in `docs/sim-interface-notes.md` (facts and where you confirmed them).

## 7. Testing in FIPinator Live

- Point a panel at your gauge file and restart the panel in Live; no reinstall needed for a gauge change.
- Live's log (`%LOCALAPPDATA%\FIPinator 3000\logs`) shows which gauge each FIP shows and every knob click.
- Hitches, freezes or steppy needles: read Live's once-a-minute log lines before changing anything.
  `Timing, last minute` (sim pauses; FIP sends over 80 ms, where about 5,000 ms means Logitech's driver stalled)
  and `Sends, last minute` (pictures per FIP and the average send time).
- Each picture takes Logitech's driver about 68 ms, so the FIPs share a small budget: Live lets 3 FIPs send at once
  and skips pictures that haven't changed. A value that jitters all the time takes time from the other FIPs.
- A FIP that freezes for about 5 seconds now and then is Logitech's driver, not your gauge: see
  `docs/FIP-USB-TIPS.md`.

## Built from scratch

FIPinator was written from scratch: its own code talks straight to Logitech's FIP driver and to MSFS through
SimConnect, instead of building on an existing tool. That keeps it small, free and easy to change, but it's young.
Expect quirks and bugs that long-established tools have already ironed out. If you find one, report it, or fix it
and share the fix.
