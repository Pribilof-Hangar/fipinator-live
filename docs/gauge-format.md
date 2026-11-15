# Gauge file format (version 1)

A gauge is one JSON file. It describes what the gauge looks like, in drawing order
(back to front), and which sim values move it. FIPinator draws it at the FIP's native
size (320×240) with smooth, anti-aliased edges.

This format is early and will change while version 1 is built. When a gauge file has mistakes
(an unknown value or scale, a formula that can't be read), FIPinator lists them all when it loads.

## Coordinates and angles

- Positions are in screen pixels: `x` from the left, `y` from the top. The FIP is 320×240.
- Angles are in degrees, **clockwise from 12 o'clock**. 0 is straight up, 90 is 3 o'clock.
- Colors are hex strings: `"#RRGGBB"`, or `"#AARRGGBB"` with transparency.

## Top level

```json
{
  "format": 1,
  "name": "DC-6 Airspeed",
  "author": "Your name",
  "license": "FREE and open source (AGPL-3.0): free to use, change, share and fork.",
  "description": "What it is and which aircraft it's for",
  "canvas": { "width": 320, "height": 240, "background": "#5A6064" },
  "center": [160, 120],
  "font": "../../assets/fonts/BarlowCondensed-Medium.ttf",
  "values": { },
  "scales": { },
  "layers": [ ]
}
```

| Field | Meaning |
|---|---|
| `format` | Format version. Always `1` for now. |
| `license` | The gauge's licence line. Every gauge that ships with FIPinator has the one above (a test checks it); for gauges you make, it's yours to set or leave out. Optional. |
| `canvas` | Size and background color. |
| `center` | Default dial center for layers that don't set their own. |
| `font` | A `.ttf` file, relative to the gauge file. Optional. |
| `values` | The live values this gauge uses (see below). |
| `scales` | How a value maps to a needle angle (see below). |
| `layers` | What to draw, back to front. |

## Values

Each value has a name you choose, and says where it comes from in the sim.

```json
"values": {
  "ias": { "sim": "AIRSPEED INDICATED", "unit": "knots", "default": 0 }
}
```

- `sim`: a SimConnect variable name. Aircraft-specific ones use `L:` names, for example
  `"L:SOME_PMDG_VALUE"`.
- `unit`: the SimConnect unit.
- `default`: used when no sim data has arrived yet, and in previews.

A value can also be worked out from other values with `expr` instead of `sim`:

```json
"vor1_bearing": { "expr": "wrap360(vor1_radial + 180 - heading)" }
```

Formulas can use numbers, value names, `+ - * / %`, parentheses, and `wrap360(x)` (0 to under
360), `wrap180(x)` (-180 to under 180), `abs(x)`, `min(a, b)`, `max(a, b)`, `clamp(x, low, high)`,
and `choose(test, a, b)` (a when test is above 0, otherwise b).

## Scales

A scale turns a value into an angle. Values outside `min`–`max` are held at the end stop.

```json
"scales": {
  "speed": { "min": 0, "max": 300, "startAngle": 0, "endAngle": 330 }
}
```

**Uneven scales:** list `[value, angle]` points instead, in increasing value order; angles in
between are filled in evenly. Angles can go past 360 for dials that turn more than once.

```json
"speed": { "points": [[40, 32], [80, 100], [240, 360], [400, 565]] }
```

**Wrapping scales:** `"wrap": true` makes the scale go round and round instead of stopping, for
example an altimeter hand that turns once per 1000 ft:
`{ "min": 0, "max": 1000, "startAngle": 0, "endAngle": 360, "wrap": true }`.

When a scale is used to slide a group (`moveX` / `moveY`), its "angle" is a distance in pixels.

**Bent scales:** some dials' scale line isn't a circle around the needle's pivot, for example
the DC-6's twin pressure dials, whose needles pivot at the dial's edge. Give each point a third
number, its distance from the pivot: `[value, angle, distance]` (all points, not some). On a bent
scale, a layer's `radius` (ticks, labels, arcs) and a needle's `length` are measured **from the
scale line** instead of from the pivot: `0` is on the line, `5` is 5 px further out, `-14` is 14 px
further in. Arcs follow the bend.

```json
"press": { "points": [[0, 150.2, 74.6], [100, 89.4, 59.0], [200, 31.7, 76.1]] }
```

## Layers

Every layer has a `"type"`. Fields marked * are required.

### `rect`
`x`*, `y`*, `width`*, `height`*, `radius`, `fill`, `stroke`, `strokeWidth`

### `circle`
`r`*, `center`, `fill`, or a top-to-bottom gradient with `fillTop` + `fillBottom`, `stroke`, `strokeWidth`

### `screw`
`x`*, `y`*, `kind`: `"boss"` (rubber corner block), `"slot"` (slotted screw), `"steel"`
(bright slotted steel screw), or `"calibration"` (small screw on the dial face)

### `ticks`
Tick marks around a dial.
`scale`*, `radius`*, `length`*, `width`*, `color`, `center`, and either
`every` (with optional `from` / `to`) or `at` (a list of exact values).
`shape`: `"line"` (default) or `"triangle"`: a filled marker `width` wide at `radius`, pointing
toward the center (or outward with a negative `length`). A very short `length` with a wide
round line makes a dot.

### `labels`
Numbers around a dial.
`scale`*, `radius`*, `size`*, `color`, `center`, and either `at` (list of values) or
`every` (with optional `from` / `to`). Optional `divideBy` shows `value / divideBy`
(for example 1000 feet shown as `1`). Optional `texts` replaces some labels, for example
`{ "0": "N", "90": "E" }`. `orient`: `"upright"` (default) keeps text level; `"radial"` turns
each label so its top faces outward, as on a compass card.

### `arc`
A colored band, for example a green operating range.
`scale`*, `from`*, `to`*, `radius`*, `width`*, `color`, `center`

### `text`
`text`*, `x`*, `y`*, `size`*, `color`, `align`: `"center"` (default), `"left"`, `"right"`.
Or show a live value instead of fixed text: `value` plus `format` (for example `"0.00"`).
`italic: true` slants the text (a maker's signature on a dial). Colours may carry transparency
as `#AARRGGBB`, for example `#D9EFE6CF` for 85% opaque cream.

### `group`
A set of layers drawn together that can turn and slide with values: compass cards, course
bars, glideslope pointers.
`layers`*, `rotate`: `{ "value", "scale" }` turns the group around its center;
`moveX` / `moveY`: `{ "value", "scale" }` slide it (pixels, right/down positive);
`clip`: only draw inside a circle of this radius (the dial window); `center`.
To place a whole gauge inside another (combined gauges): `offset` `[x, y]` moves the group,
`zoom` draws it bigger or smaller (0.67 = two-thirds size), `clipBox` `[x, y, width, height]`
keeps it inside a rectangle in its own coordinates. The Gauge Maker's "Combine gauges" writes these.

### `needle`
A pointer that turns with a value.
`scale`*, `value`*, `length`*, `width`*, `tail`, `color`, `tailColor`, `shadow` (true/false), `center`.
`style`: `"classic"` (default, tapered pointer), `"arrow"` (thin line, arrowhead, forked tail:
RMI needle 1), `"doubleArrow"` (two parallel lines: RMI needle 2), or `"pointer"` (outlined
blade with a dark counterweight arm behind the pivot, as on the DC-6 engine dials: `width` is half
the blade's width, `tail` the arm's length, `tailColor` the arm's colour). For the arrow styles,
`width` is the line thickness. `tag`: a short text such as an engine number, shown in a small
disc on the needle (two-needle engine dials).

### `hub`
The cap over the needle pivot.
`r`*, `fill`, `stroke`, `center`

### `glare`
A soft glass reflection over the dial. Put it last.
`r`*, `opacity` (default 0.2), `center`

## Planned (not built yet)

- Images (PNG artwork) as layers
- A `visible` field to show or hide a part of the gauge by a value

## Knobs and buttons (`controls`)

What the FIP's knobs and side buttons do while this gauge is showing. Each entry sends a sim
event (a standard MSFS event name, in capitals) to the user's aircraft.

```json
"controls": {
  "rightKnobClockwise":     { "event": "KOHLSMAN_INC" },
  "rightKnobAnticlockwise": { "event": "KOHLSMAN_DEC" },
  "leftKnobClockwise":      { "event": "KOHLSMAN_INC", "repeat": 10 },
  "button1":                { "event": "SOME_EVENT", "data": 1 }
}
```

Names: `rightKnobClockwise`, `rightKnobAnticlockwise`, `leftKnobClockwise`,
`leftKnobAnticlockwise`, `button1` to `button6`. `repeat` (1-50) sends the event several times
per click for a fast knob; `data` is a number some events need. Knobs send one event per click.
Leave `controls` out for a gauge whose knobs should do nothing.

## Knob modes (`modes`)

Some gauges have more to set than two knobs can do, for example heading and altitude. With
`modes`, each S button picks what the knobs set. Pressing that button chooses the mode.

```json
"modes": [
  { "name": "HDG", "button": "button1", "controls": {
      "rightKnobClockwise":     { "event": "HEADING_BUG_INC" },
      "rightKnobAnticlockwise": { "event": "HEADING_BUG_DEC" } } },
  { "name": "ALT", "button": "button2", "controls": {
      "rightKnobClockwise":     { "event": "AP_ALT_VAR_INC" },
      "rightKnobAnticlockwise": { "event": "AP_ALT_VAR_DEC" } } }
]
```

- `name`: a short name for the mode. `button`: `button1` to `button6` (each button picks only one mode).
- `controls`: the same shape as `controls` above. The chosen mode's controls come first, then the
  gauge's own `controls`.
- The chosen mode's button light is on.
- The gauge can show the chosen mode: declare a value named `knobMode` in `values` (needed when
  `modes` is used). FIPinator sets it to 1 for the first mode, 2 for the second, and so on.

## Button lights (`buttonLights`)

Lights an S button while a value is on.

```json
"buttonLights": { "button3": "landingLights" }
```

This lights S3 while the value `landingLights` is above 0.5. The value must be in `values`.

`modes` and `buttonLights` work in FIPinator Live only. They are not exported to SPAD.neXt.
