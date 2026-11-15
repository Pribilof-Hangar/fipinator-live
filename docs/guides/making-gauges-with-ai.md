# AI README: making a brand-new gauge with AI

FIPinator 3000, FIPinator Live's companion, is for organizing and changing gauges: setting up your panel, changing colours and needles,
combining gauges. Making a gauge **from scratch**, or from a screenshot of a cockpit, is a different job: you need
to read the plane's data, draw the instrument and check it against the cockpit. An AI assistant does that well.

**Claude is recommended**: FIPinator and its gauges were built and tested with it. Any AI that can read and write
files can follow the same guide. If you don't use AI, skip this page: everything else in FIPinator works without
it.

FIPinator is FREE and open source (AGPL-3.0): free to use, change, share and fork.

## What to give the AI

- This project's files (a copy of the FIPinator repository from GitHub).
- A screenshot of the instrument you want, from the sim's cockpit, zoomed in if you can.
- The plane's name, and whether it's a default plane or an add-on (PMDG, FSS, and so on).
- The **gauge-making guide** (`docs/guides/gauge-making-guide.md`; FIPinator 3000 opens it with its "Gauge-making
  guide" button). It has the steps and everything we learned the hard way.

Then ask something like: *"Make a FIPinator gauge of this instrument for the FIP. Follow the gauge-making guide."*

## Other files that help

- `docs/gauge-format.md`: what a gauge file can hold.
- `docs/gauge-cookbook.md`: worked examples of common gauge parts.
- `docs/sim-interface-notes.md`: sim values and knob events already checked, per plane.

## When it's made

The AI writes a gauge file (`.json`). Put it in your **My gauges** folder (`Documents\FIPinator 3000\Gauges`),
then:

1. Open it in FIPinator 3000 (**Change how a gauge looks**) to check it and tweak colours or positions.
2. Put it on a FIP with **Set up your panel**.
3. Fly with it in FIPinator Live and compare it with the cockpit. If something reads wrong, tell the AI what the
   FIP showed and what the cockpit showed at the same moment.

## Built from scratch

FIPinator was written from scratch: its own code talks straight to Logitech's FIP driver and to MSFS through
SimConnect, instead of building on an existing tool. That keeps it small, free and easy to change, but it's young.
Expect quirks and bugs that long-established tools have already ironed out. If you find one, report it, or fix it
and share the fix.
