# Using SPAD.neXt and FIPinator Live together

Many simmers already use SPAD.neXt for their yoke, throttle quadrant, radio panels and other gear. You can keep
doing that and let FIPinator Live drive your FIPs. This is how the person who made FIPinator flies every day.

FIPinator is FREE and open source (AGPL-3.0): free to use, change, share and fork.

## The everyday setup

- **SPAD.neXt** runs your yoke, GPS, throttle quadrant and other devices, exactly as before.
- **FIPinator Live** runs the FIPs.
- In SPAD.neXt, **switch off its support for the Logitech (Saitek) Flight Instrument Panels**, so it leaves the
  FIPs alone. Where exactly the switch is depends on your SPAD.neXt version; see SPAD.neXt's own help if you
  can't find it.
- Start FIPinator Live (before or after the sim). Your other SPAD.neXt devices keep working as usual.

Only one program can drive the FIPs at a time. Something we've seen in SPAD.neXt's own log: when Logitech's FIP
service is running, SPAD.neXt notes that it found it and turns its FIP support off by itself. That's what we
observed on one PC, not a promise; switching it off in SPAD.neXt yourself is the sure way.

FIPinator never changes SPAD.neXt's settings or files.

## Going back to SPAD.neXt on the FIPs

Close FIPinator Live and switch SPAD.neXt's FIP support back on. If you swap often, **Check my setup** in Live
has an optional **Share the FIPs...** step: it lets Live start and stop Logitech's FIP service by itself, so
SPAD.neXt can take the FIPs whenever Live is closed (Windows asks for permission once; it can be undone).

## Only use SPAD.neXt?

You don't need FIPinator Live: download **FIPinator gauges for SPAD.neXt** (a separate download) and add its
gauges folder in SPAD.neXt. That set is a port of FIPinator's gauges and is less tested than Live: some gauges
need work, and a few features are Live only (flipping pages with the left knob, flipping by itself, knob modes,
layouts, knobs that change a plane's own values, and gauges that read an add-on's own data). FIPinator 3000 can
also save changed gauges for SPAD.neXt, but that side is less tested too.

## If something doesn't work

- **The FIPs stay dark or show SPAD.neXt's pages while Live runs.** SPAD.neXt still has the FIPs: switch off its
  FIP support, then restart FIPinator Live.
- **Your other SPAD.neXt devices stopped working.** FIPinator only touches the FIPs; check SPAD.neXt's own device
  list and log.
- **A FIP stays dark after restarting the PC.** See [FIP and USB tips](../FIP-USB-TIPS.md).
