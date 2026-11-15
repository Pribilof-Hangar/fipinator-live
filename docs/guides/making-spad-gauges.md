# How SPAD.neXt FIP gauges are made

A plain guide for anyone who wants to build or fix gauges for the Logitech/Saitek Flight Instrument Panel
(FIP) in SPAD.neXt, for MSFS 2024. Everything here was learned the honest way:

- from SPAD.neXt's **public** gauge rules (its `gauge.xsd` schema on GitHub, SPAD-neXt/DemoCode, MIT licence)
  and its documentation (docs.spadnext.com),
- from the free gauges that come with SPAD.neXt and free community gauges, **read** to see how they work,
- from Microsoft's MSFS 2024 SDK documentation (the sim's commands and values),
- and from testing on a real FIP and reading SPAD.neXt's own log.

You don't need to copy, unpack or reverse-engineer anyone's paid gauges to make good gauges. The ideas below are
the standard, public building blocks. Draw your own pictures, write your own file, and test it.

> FIPinator 3000 makes SPAD.neXt gauges for you automatically ("Using SPAD.neXt?" in the app). This guide is for
> people who want to understand them, fix them or build their own by hand. The facts with their sources are in
> [sim-interface-notes.md](../sim-interface-notes.md); how FIPinator's export works is in
> [spad-export.md](../spad-export.md).

## 1. What a SPAD.neXt gauge is

One **.zip** file holding:

- an **XML file** that says what is drawn where and what moves it,
- a **`1024` folder** with the pictures (PNG with see-through parts works well),
- optionally a thumbnail picture.

Put the .zip in a folder, then in SPAD.neXt's FIP gauge list choose "Add location" and pick that folder. Don't
unzip it.

The FIP screen is **320 x 240** pixels. Position (0, 0) is the top-left corner; Y goes **down**.

## 2. The building blocks

### Background

One picture for everything that never moves (the dial face, numbers, screws). It's drawn first.

### A needle (turns)

An `Element` with an `Image` and a `Rotate`:

- `Position`: where the needle's pivot sits on the screen.
- `Axis`: where the pivot is inside the needle picture.
- Draw the needle **pointing right** and say so (`PointsTo="East"`); then a `Nonlinearity` table can list, for
  each value, a point on the screen the needle should point at. Many points close together (every 10 degrees
  or so) keep it accurate on uneven dials.
- Without a table, the `Rotate` value is simply an angle in **radians**, clockwise.

### A turning card (compass card, heading bug, ADF card)

Exactly like a needle, just a bigger picture: the card is one picture that turns around the dial's centre.
A heading bug turns by "bug minus heading", the card by "minus heading". Separate elements, same centre.

### A part that slides (ILS bars, turn ball, flags)

`Shift`: either `Value` x `Scale` pixels (`Scale X` or `Scale Y`), or a `Nonlinearity` table of value -> screen
position. `Delay PixelsPerSecond` smooths the movement.

### Slide **and** turn in one part (attitude horizon, HSI course bar)

One element can have both `Shift` and `Rotate`: it slides first (in its own level frame), then turns around its
position. That's how an attitude horizon (pitch slides, bank turns) and an HSI's course deviation bar (deviation
slides, course turns) are made.

### Keeping moving parts inside a round window

There is no clipping. Instead, draw a **cover picture on top** with a hole where the window is: whatever slides
outside the window is hidden under the cover (the bezel). This is how SPAD.neXt's own attitude gauge does it.

> Tested: PNG `MaskImage` pictures were drawn on screen as white shapes instead of hiding anything. Use covers.

### Hiding something (flags, warning lights)

`Visible` with a yes/no formula, or slide it off the screen.

### Numbers (digital readouts)

SPAD.neXt picks a picture by value: `Image Dynamic="digit_{VALUE}.png"` with a `Value` formula. Make one picture
per digit (0-9, plus a blank and a minus sign) and one element per digit position; each element's formula works out
its own digit. The same "pick a picture by value" trick draws arcs that grow, or anything with a few fixed looks.

### Knobs

A `<Mouse>` block per knob, the old FS gauge way that SPAD.neXt's schema describes:

```xml
<Mouse>
  <Area>
    <Area><Cursor Type="DownArrow"/><Click Event="KOHLSMAN_DEC" Repeat="Yes"/></Area>
    <Area><Cursor Type="UpArrow"/><Click Event="KOHLSMAN_INC" Repeat="Yes"/></Area>
  </Area>
</Mouse>
```

`DownArrow` = turning the knob anticlockwise, `UpArrow` = clockwise. The event names are the sim's own commands
(MSFS 2024 SDK, "Event IDs"). A user's own knob settings in SPAD.neXt win over the gauge's.

> Tested: `Button BoundTo="FIP_RIGHTDIAL_..."` lines in `GaugeSettings` did nothing for knobs. `Mouse` blocks work.

### Page buttons

When you add several gauges to one FIP, SPAD.neXt itself makes the up/down page buttons flip between them. Leave
those buttons alone in your gauge.

## 3. Formulas (how values get in)

Values are written in the old FS gauge "RPN" style: the value first, then what to do with it.

| You want | Write |
|---|---|
| A sim value | `(A:AIRSPEED INDICATED, knots)` |
| A plane's own value | `(L:some_lvar, number)` |
| Add / subtract / multiply / divide | `a b +`, `a b -`, `a b *`, `a b /` |
| Round down | `flr` |
| Round to nearest | `0.5 + flr` (SPAD.neXt ignores `near`) |
| Bigger / smaller of two | `max`, `min` |
| If / else | `cond if{ a } els{ b }` |

> Tested: with `near`, SPAD.neXt asked for pictures like `slide_37.77.png` that don't exist and drew the gauge wrong.

## 4. Sim commands that catch people out (MSFS 2024)

- Use the **published event names**, not the SDK's internal key names. Example: the attitude bars knob is
  `ATTITUDE_BARS_POSITION_UP` / `_DOWN`; `..._INC` / `_DEC` are rejected by the sim ("name unrecognized").
- Study-level planes (PMDG and others) often ignore the standard commands for their own knobs and use their own
  events or L-vars instead. Check the plane maker's SDK notes.

## 5. Testing and fixing

1. Load the gauge in SPAD.neXt on a FIP and try every moving part and knob.
2. Read SPAD.neXt's log: `%APPDATA%\SPAD.neXt\logs\spad.log` (its times are UTC). It shows:
   - `Loading of ... failed` / `missing in archive`: a picture name or formula is wrong,
   - `TransmitClientEvent KOHLSMAN_INC`: your knob sent its command,
   - `NAME_UNRECOGNIZED`: the sim doesn't know that command name.
3. Change one thing at a time.

## 6. Playing fair

- **Do:** read SPAD.neXt's public schema and docs, Microsoft's SDK docs, and free gauges you have installed, to
  learn how things work. Note the facts (which value, which command, which building block).
- **Then:** close them and make your own pictures and your own file.
- **Don't:** copy other people's pictures, files or layouts, unpack or decrypt paid/protected gauges, or
  reverse-engineer programs. It isn't needed: everything above is public and enough to build any FIP gauge.

FIPinator 3000 is free and open source (AGPL-3.0). Its own gauges and SPAD export are original work, made this way.
