# Sim and SPAD.neXt interface notes

Facts about how gauges talk to MSFS 2024 and how SPAD.neXt FIP gauges are put together, in our own words.
Names and behaviour only: no copied code or file contents. Each fact says where it was confirmed.

Sources used so far:
- **SPAD schema**: SPAD.neXt's public `gauge.xsd` (github.com/SPAD-neXt/DemoCode, MIT).
- **SPAD docs**: docs.spadnext.com, "Gauges and Extensions".
- **Bundled Saitek gauges**: the free gauges that ship with SPAD.neXt
  (`C:\Program Files\SPAD.neXt\Gauges\Saitek\SaitekGauges.snz`, a plain zip), read in place, read only.
- **Owner's profile**: the owner's own SPAD.neXt profiles (Documents\SPAD.neXt\profiles), read only.
- **SPAD log**: `%APPDATA%\SPAD.neXt\logs\spad.log`, read only.
- **FIP test**: tried on the owner's FIP in SPAD.neXt.

Not used: the paid gauges in Documents\SPAD.neXt\gauges (`.snp`) are packed in a protected format, so they
weren't opened. SPAD's picture cache (conf\cache\Gauges) holds only pictures.

## Knobs and S buttons in a SPAD FIP gauge

| Fact | Where confirmed |
|---|---|
| `GaugeSettings/Buttons/Button BoundTo="FIP_DIAL_RIGHT_CLOCKWISE"` (and the `FIP_RIGHTDIAL_...` spelling) with a plain event name did nothing; SPAD's log never mentions them | FIP test, SPAD log (2026-11-11) |
| Gauges whose knobs work make SPAD log `Loaded Embedded Buttonconfig: FIP_RIGHTDIAL` / `FIP_LEFTDIAL` / `FIP_BUTTON_S1`... | SPAD log |
| SPAD's docs: a gauge can carry a button setting as a file named after the control (`FIP_RIGHTDIAL.bml` ...) in the gauge's main folder, made by setting the control up in SPAD and exporting it. Control names: `FIP_BUTTON_S1`-`S6`, `FIP_RIGHTDIAL`, `FIP_LEFTDIAL`, `FIP_BUTTON_PAGEUP`, `FIP_BUTTON_PAGEDOWN` | SPAD docs |
| The old FS gauge way: `<Mouse>` > `<Area>` > two `<Area>`s, each with `<Cursor Type="DownArrow"/>` or `UpArrow` and `<Click Event="..." Repeat="Yes"/>`. The schema allows only those two cursor types, so it describes a two-way control (a knob turn) | SPAD schema |
| The bundled altimeter, heading, ADF, VOR and HSI gauges set their knobs that way, with plain sim event names (`KOHLSMAN_DEC`/`_INC`, `HEADING_BUG_DEC`/`_INC`, `VOR1_OBI_...`, `ADF_CARD_...`). DownArrow = the DEC event in most of them. Some use one `<Mouse>` per knob, the HSI uses one `<Mouse>` with two outer `<Area>`s | Bundled Saitek gauges |
| `<Mouse>` knob blocks work (confirmed again after a fix, owner: "knobs work", 2026-11-11): SPAD sent our knob events (`KOHLSMAN_INC`/`_DEC`, `GYRO_DRIFT_...`, `HEADING_BUG_DEC`, `VOR1_OBI_...`, `FLAPS_INCR`/`_DECR`) to the sim, logged as `TransmitClientEvent` and `ClientEvent SIMCONNECT:...` | SPAD log, FIP test (2026-11-11) |
| `GEAR_UP`/`GEAR_DOWN` were sent too (our S-button `Button` lines on a gear gauge, so S buttons as `Button` lines seem to work) | SPAD log (2026-11-11), not yet confirmed by the owner |
| Which `<Mouse>` block (or area) SPAD gives to which knob | **Not known yet** (test kit, below) |
| In a profile, a button setting is: control (`BoundTo`) > `EventDefinition Trigger="..."` (e.g. `PRESS`, `TUNER_CLOCKWISE`, `TUNER_COUNTERCLOCKWISE`) > actions such as sending a sim event (`EventActionControl`) or changing a value by a step (`EventActionChangeValue ... ValueOperation="Increment"`). Sim names carry a prefix: `SIMCONNECT:KOHLSMAN SETTING HG`, `MSFS:AS1000_...` | Owner's profile |
| Page buttons: when a gauge is added to a FIP, SPAD itself gives it `FIP_BUTTON_PAGEUP` = `SPAD_neXt.Gauge_Next` and `FIP_BUTTON_PAGEDOWN` = `SPAD_neXt.Gauge_Prev` in the profile (ours included), so up/down flip through the gauges on that FIP. Our gauges must not set the page buttons | Owner's profile (autosave, 2026-11-11) |
| A knob-turn setting exported from SPAD (the `.bml` layout) | **Not seen yet**: needs the owner to export one from their own SPAD |

## Sim events (MSFS 2024)

| Fact | Where confirmed |
|---|---|
| The attitude indicator's bars knob events are `ATTITUDE_BARS_POSITION_UP` / `_DOWN` (and `_SET`, -100..100). `ATTITUDE_BARS_POSITION_INC`/`_DEC` are only the SDK's internal key names: sent by name, the sim answers `NAME_UNRECOGNIZED` | MSFS 2024 SDK docs (Aircraft Instrumentation Events); SPAD log (2026-11-11) |

## Directions of sim values (checked in flight, 2026-11-11)

| Fact | Where confirmed |
|---|---|
| `ATTITUDE INDICATOR PITCH DEGREES` / `PLANE PITCH DEGREES`: nose up is negative (climb +1,400 ft/min = -5, dive -2,500 ft/min = +7). Our attitude gauges now use this (fixed 2026-11-11) | Sim reading in flight (fipinator-fip watch) |
| `HSI CDI NEEDLE` / `NAV CDI`: a **positive value means the needle goes right** (the course is to the right: fly right). Sim read +18.6 on the DC-6 ILS while the DC-6's own course indicator said fly right (2026-11-12); our gauges read it straight (positive = right), as they did before 2026-11-11. A 2026-11-11 change had reversed it in every gauge after a "left/right opposite" report from SPAD.neXt; that was wrong for FIPinator Live and is undone. (A free SPAD HSI negating it may be down to SPAD sliding parts the opposite way, as found for the horizon: unchecked.) | Sim reading + owner's DC-6 cockpit vs FIPinator Live (2026-11-12) |
| `ATTITUDE INDICATOR BANK DEGREES` / `PLANE BANK DEGREES`: a positive value is a **left** bank (bank -5 to -7 came with the heading rising 243 to 248, i.e. turning right). FIPinator Live turns the horizon clockwise for a positive value, which is right for a left bank | Sim reading in flight, Asobo 737 MAX (fipinator-fip watch, bank + heading, 2026-11-11) |
| Asobo 737 MAX N1/N2 display shows the **corrected** `TURB ENG CORRECTED N1` / `N2`: at low power its screen read 41.1 / 41.6 while corrected read 41.4 / 41.8 and plain 40.0 / 40.5 (2026-11-11); same at idle on 2026-11-10 (screen 36.2, corrected, plain 35.4). An earlier "cockpit lower" reading in cruise was the screen lagging. Our 737 MAX gauges use corrected | Sim reading + owner reading the cockpit and the FIP (2026-11-11) |
| Asobo 727 N1 also shows the **plain** `TURB ENG N1`: at idle plain 37.7, corrected 38.7, cockpit read 38. Our 727 gauges already use plain N1 | Sim reading + owner reading the cockpit (2026-11-11) |
| `HSI GSI NEEDLE` / `NAV GSI` (glideslope): a **positive value means the needle goes down** in our gauges, which matched the 727's own glideslope on an ILS (owner, 727 HSI in FIPinator Live, 2026-11-12). The ILS/course and HSI gauges already did this; the 727 and 737 MAX ADIs' glideslope pointer did the opposite and was changed to match (one value, one direction; the ADIs themselves not yet flown on a glideslope) | Owner's FIP vs the 727 cockpit (2026-11-12) |
| `AUTOPILOT FLIGHT DIRECTOR PITCH` / `BANK` directions (727 / 737 MAX ADI bars: `fd_y = -(fdp - pitch) * 3`, `fd_x = (fdb - bank) * 1.5`) | **Not checked yet** against a cockpit flight director |
| Decision height knob: `INCREASE_DECISION_HEIGHT` / `DECREASE_DECISION_HEIGHT` (1 m per click; `DECISION_HEIGHT_SET` in meters). `DECISION_HEIGHT_INC`/`_DEC` are only the SDK's internal names (`KEY_DECISION_HEIGHT_INC`), like the attitude bars | MSFS 2024 SDK docs (Aircraft Miscellaneous Events) and SDK headers; not yet tried on a FIP |

## SPAD's formula language (RPN)

| Fact | Where confirmed |
|---|---|
| SPAD doesn't apply `near` (round): a picture number worked out with it came out as 37.77, and SPAD asked for `slide1_37.77105.png` (316 times). Use `0.5 + flr` instead | SPAD log (2026-11-11) |
| `flr`, `abs`, `min`, `max`, `+ - * /`, `<`, `>`, `if{ } els{ }` work (digits, needles, cards show right) | FIP test |
| SPAD writes a missing picture as a warning every frame and shows the gauge's first picture instead | SPAD log |

## Moving parts in a SPAD FIP gauge

| Fact | Where confirmed |
|---|---|
| An `Element` with `Image` + `Rotate`/`Value` and no `Nonlinearity` turns by the value in radians around the image's `Axis`, placed at the element's `Position` | Bundled Saitek gauges (attitude card) |
| `Shift` with `Value Minimum/Maximum` and `Scale Y="..."` moves the picture by value x scale pixels; one element can have `Shift` and `Rotate` together (the bundled attitude horizon: pitch slides it, bank turns it) | Bundled Saitek gauges |
| `Shift` can also use a `Nonlinearity` table of value -> X,Y position (the bundled turn ball) | Bundled Saitek gauges |
| Our horizon (one element with `Shift` + `Rotate` in radians, then copies of what's under it with the window cut out) works in SPAD on a FIP | FIP test (2026-11-11) |
| A positive `Shift` `Scale Y` slides the picture **up** in SPAD (pulling up showed more ground until we negated it); positive `Rotate` radians turn clockwise; `Scale X` slides right | FIP test, Asobo 737 (2026-11-11) |
| Picture sets picked with `0.5 + flr` work: the ILS bars were "spot on" | FIP test (2026-11-11) |
| The bundled attitude gauge has no mask: a picture drawn on top with a round hole hides what slides outside the window. The turn ball is hidden the same way | Bundled Saitek gauges |
| Our `MaskImage` PNG (white = show) was drawn on screen as white circles instead of hiding anything. (The bundled altimeter does use a `MaskImage`, a BMP; how SPAD reads it isn't clear, so we don't use masks.) | FIP test (2026-11-11) |
| Picking a picture by value (`Image Dynamic="name_{VALUE}.png"` with a `Value`) works: our digits and the ILS bars | FIP test (ILS "looks better", 2026-11-11) |

## Learned from a free community HSI (Baron HSI, SPAD "Online Free" folder, read only, 2026-11-11)

| Fact | Where confirmed |
|---|---|
| An HSI is built from separate elements that all turn around the same centre: the compass card turns by minus the heading (radians); the heading bug and the course pointer each turn by their own value minus the heading. No card-on-card nesting | Installed free gauge |
| The course deviation bar is one element with both `Shift` (CDI needle, `Scale X` pixels per unit) and `Rotate` (course minus heading): the same slide-and-turn pattern as an attitude horizon | Installed free gauge |
| Flags (TO/FROM, NAV, HDG) hide by sliding off the screen with a `Shift` + `Nonlinearity` table of value -> X,Y position, or by `Visible`. No masks | Installed free gauge |
| `Shift` can carry `Delay PixelsPerSecond` to smooth the movement | Installed free gauge |
| The heading-bug knob is a `Mouse` block (`HEADING_BUG_DEC`/`_INC`), the first block in the file | Installed free gauge |

## How FIPinator uses this

- Sliding parts (ILS bars, flags): a set of pictures picked by value (no masks).
- Attitude horizon: one element with `Shift` + `Rotate` (radians), then copies of whatever was under it, with
  the round window cut out, drawn on top. Same idea as the bundled attitude gauge.
- Knobs: `<Mouse>` blocks, right knob first (work on a real FIP, 2026-11-11).

## Knob test kit (2026-11-11)

Three test gauges in `Documents\FIPinator 3000\FIPinator SPAD Gauges (FREE)\TEST KIT`, category "FIPinator:
TEST KIT". Each shows the baro setting (should move with the RIGHT knob; S1 = up) and the heading bug (LEFT knob):
- **A**: two `<Mouse>` blocks (right, then left), plain event names; S1 as a `Button` line with a plain name.
- **B**: one `<Mouse>` with two areas (right, then left); S1 as a `Button` line with `SIMCONNECT:`.
- **C**: no Mouse; `Button` lines `FIP_RIGHTDIAL_...`/`FIP_LEFTDIAL_...` and S1 with `SIMCONNECT:` names.

Result: not needed in the end. A real flight showed `<Mouse>` blocks (method A) work on every gauge; the
test gauges can be deleted from that folder. Which block is which knob on a two-knob gauge is still to check.

## PMDG DC-6: cylinder head temperature (2026-11-12)

`L:dc6_347_obj` to `L:dc6_350_obj` (CHT engines 1-4) are the needle's place on the dial, 0-350, over a dial that
reads -50 to 300 °C: °C = value - 50. Confirmed in flight: the DC-6's own gauge about 185 °C with the values at
223-238; the real dial (owner's screenshot) has marks below 0 down to -50 and ends at 300. Other DC-6 L: values may
be needle places too: when a gauge's numbers look off but its needle is right, check this first.

## FSS 727-200F: EPR bug knobs (2026-11-12)

The FSS 727's EPR set knobs (tooltips "EPR_Knob_1/2/3") are infinite knobs that add or subtract 1 per click to
`L:FSS_B727_PCP_EPR_KNOB1..3` (they rest at 0); the plane then moves the bugs, `L:FSS_B727_PCP_EPR_BUG1..3`, which
read 0.5 with the counter showing "200" (EPR 2.00): bug = (EPR - 1) / 2, so EPR bug = 1 + 2 x value (one reading;
check another setting). Source: the aircraft's openly readable model behavior file (Pilot_center_panel.xml), read
only, plus a sim reading. Writing an L: value through SimConnect (SetDataOnSimObject) works in MSFS 2024, and the plane counts each written change as one click, whatever its size: 10 separate +-1 writes moved the bug 10 steps, one +9 write moved it one (2026-11-12). FIPinator Live's knobs can now do this (`"set"` controls).

## FSS 727-200F: radio altimeter (2026-11-12)

The captain's round radio altimeter: needle `L:FSS_B727_CP_RADIO_ALT_NEEDLE`, DH bug `L:FSS_B727_CP_RADIO_ALT_DH_MARKER`,
DH knob `L:FSS_B727_CP_RADIO_ALT_DH_KNOB` (counts each change as one click, like the EPR knob), DH light
`L:FSS_B727_CP_RADIO_ALT_WARNING_LIGHT`, flag `L:FSS_B727_CP_RADIO_ALT_FLAG_VISIBLE`. The marker value is the bug's
place on the dial as a share of a full turn: 25 clicks = 0.25 = 3 o'clock = 250 ft on the dial (owner looked), so
one click = 10 ft at the low end. On the ground the needle value is 0 (dial at 0) while the sim's RADIO HEIGHT
reads 12 ft. The needle value is taken to work like the marker (share of a turn); check in flight. Source: the
aircraft's model behavior file (Captain_panel.xml), read only, plus sim readings.

## Cessna 404 Titan (MSFS 2024, classic gauges), 2026-11-12

Piston twin (turbocharged). Its gauges read the sim's standard values: at a little power the sim gave MP 16.0 inHg,
RPM 1,291, fuel flow 80 lb/hr, oil 52 psi / 154 °F, CHT 260 °F, EGT 240 °F, fuel 84 gal a side (6.0 lb/gal), and the
cockpit showed MP about 15.5, RPM about 1,300 (owner's screenshot, same moment). An earlier screenshot (MP about 11
while the sim read 4.9) was from a different moment. EGT gauge: marked 0-5 "25 °F/DIV", read about 2.5 with the sim
at 240 °F; how it maps (maybe EGT / 100) needs a second reading at another power. The cockpit's own files are packed
(fsarchive), not opened.

## PMDG 737-800 (MSFS 2024), 2026-11-12

| Fact | Where confirmed |
|---|---|
| The PMDG 737-800 fills the sim's standard values: airspeed, altitude, `KOHLSMAN SETTING HG`, vertical speed, pitch/bank, radio height, heading, turn ball, `TURB ENG N1/N2` (plain and corrected), EGT, fuel flow. Our speed, altitude, attitude, turn and engine gauges work as they are | Sim reading on the ground (fipinator-fip watch) |
| It does **not** fill `AUTOPILOT HEADING LOCK DIR` or `NAV OBS` (both read 0): its MCP heading and course live in PMDG's own data | Sim reading on the ground |
| PMDG shares its cockpit data through a SimConnect client data area named `PMDG_NG3_Data` (fields such as `MCP_Heading`, `MCP_Course[2]`, `MCP_Altitude`), only when the PMDG options file `737_Options.ini` (MSFS 2024: LocalState\WASM\MSFS2024\pmdg-aircraft-738\work) has `[SDK]` / `EnableDataBroadcast=1`. On the owner's PC it isn't set (2026-11-12) | PMDG's SDK header that ships with the plane (Documentation\SDK\PMDG_NG3_SDK.h); owner's ini read only |
| PMDG knobs are PMDG's own events, numbered from `THIRD_PARTY_EVENT_ID_MIN` = 69632 (0x11000): e.g. `EVT_MCP_HEADING_SELECTOR` (+390), `EVT_MCP_COURSE_SELECTOR_L` (+376) / `_R` (+409), `EVT_EFIS_CPT_BARO` (+365), `EVT_EFIS_CPT_MINIMUMS` (+355); a knob turn is sent with the mouse-wheel flags `MOUSE_FLAG_WHEEL_UP` (0x4000) / `_DOWN` (0x2000) as the event's value | PMDG SDK header |
| SPAD.neXt has its own PMDG 737 add-on (`PMDGNG3` value provider) and accepts PMDG event names like `PMDGNG3:EVT_OH_LIGHTS_TAXI` | SPAD log; owner's SPAD PMDG profile, read only |
| Engine limits for the 737-800's CFM56-7B: N1 104 %, N2 105 %, EGT 950 °C take-off / 925 °C max continuous (displayed), oil 140 °C max continuous, 155 °C for 45 min, oil pressure minimum 90 kPa | EASA TCDS E.004 issue 07 |
| The PMDG's engine display shows **plain** N1 (`TURB ENG N1`): cockpit 20.3 / 20.2, plain 20.2, corrected 20.8; our FIP (plain) read 20.3 | Owner's cockpit screenshot + FIP reading + sim reading (2026-11-12) |
| `AUTO BRAKE SWITCH CB` is **not** filled by the PMDG: it read 0 with the knob at RTO and still 0 with the knob at MAX. (The first RTO match was luck.) The autobrake needs PMDG's own data (`MAIN_AutobrakeSelector`) | Owner's screenshot (MAX) + sim reading (2026-11-12) |
| EGT and fuel flow: the PMDG's engine display uses its own numbers, not the sim's standard values. 2026-11-12, same moment: PMDG screen EGT 485 / FF 0.64; sim `ENG EXHAUST GAS TEMPERATURE:1` 390 °C, `ENG FUEL FLOW PPH:1` = `TURB ENG FUEL FLOW PPH:1` 952 lb/h (also tried GENERAL ENG EXHAUST GAS TEMPERATURE, TURB ENG ITT 599, ENG FUEL FLOW GPH 142). Our FIP showed 390 / 0.95. PMDG's SDK data block has no engine EGT or fuel flow, and its openly readable files show no other source. N1 does match (sim plain 20.4 = screen 20.4). (An earlier note that the FIP showed 484 was most likely a cockpit reading) | Owner's cockpit screenshot + FIP reading + sim readings; PMDG SDK header and package files, read only |
| Still no source for the PMDG's displayed EGT / fuel flow after a wider search (2026-11-12): sim `TURB ENG CORRECTED FF:1` read 671 lb/h against the screen's 640 (close, not equal); MobiFlight's public HubHop preset list (33,827 presets; 498 PMDG 737-700 and 15 PMDG 737-800 outputs) has no engine EGT or fuel flow output for the PMDG 737, only switches, MCP and APU EGT (`L:switch_35_73X` on the 737-700); forum reports say the same for older PMDG 737s | Sim reading; HubHop public API download; forum posts (avsim, simflight) |

## How another FIP program sends pictures (studied 2026-11-13, read only)

FIPToolKit (github.com/gflansburg/FIPToolKit, open source; read for facts, nothing copied): each gauge draws and
sends its picture inside its own lock (one draw/send at a time per gauge), only sends while its page is the one
showing, and ignores errors from DirectOutput's SetImage. In the parts read there was no lock shared across FIPs and
no frame-rate cap. Our own measurement (2026-11-13, 7 FIPs sending in parallel): about once a minute a SetImage call
hangs for 5 s, freezing that FIP. Tested the same day: a normal SetImage takes ~68 ms; one at a time across FIPs
stopped the hangs but each FIP then got ~2 pictures a second (needles stepped); a 50 ms frame gap alone changed
nothing; 3 FIPs at once is smooth with hangs down to a few per 20 minutes (kept). Then: skip the send when the
picture is byte-for-byte the one already on the FIP (in flight test).

