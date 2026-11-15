# FIP and USB tips

Logitech (Saitek) FIPs are older USB 2 devices, and with several of them the PC and Logitech's FIP driver have a
couple of known quirks. None of this is required to use FIPinator: these are things to try if one of the quirks
bothers you. We've only tested what's marked **tested by us**; the rest is what other FIP users report.

**Our own setup, as a working example (tested by us):** all seven FIPs on one **powered USB hub with a power
switch for each port**. A stuck or dark FIP can be restarted by switching its port off and on, without touching
the PC or the sim; FIPinator picks it up again by itself.

## If a FIP freezes for a few seconds

**What you see:** now and then one FIP stops for about 5 seconds, then jumps to the right reading. The sim and
the other FIPs keep going.

**Why:** FIPinator hands each new picture to Logitech's FIP driver, and with many FIPs (seen with 7) the driver
sometimes gets stuck on one for about 5 seconds before giving up. The wait happens inside Logitech's driver, so
FIPinator can't shorten it; FIPinator Live already sends as few pictures as it can (3 FIPs at a time, skipping
pictures that haven't changed) to make it rare. On a 1.5-hour DC-6 flight with 7 FIPs it happened about once
every 3 to 4 minutes, on a different FIP each time (tested by us). It's harmless, and it's a known FIP quirk that
other FIP programs run into too.

**Things others report help (not tested by us):**
- Turn off USB selective suspend (step 3 below).
- Use a powered hub (step 4), on a USB 2 port rather than USB 3, or plug the hub into a port on the back of the
  PC (the motherboard) rather than a front-panel port.
- Split the FIPs across two USB ports.

**Check whether something helped:** FIPinator Live's log (Logs button) writes a "Timing, last minute" line when a
send to a FIP was slow; sends of about 5,000 ms are these freezes. Compare a flight before and after.

If something fixes it for you, please tell us in an issue on GitHub.

Further reading (other FIP users): [DCS forum FIP thread](https://forum.dcs.world/topic/115280-dcsflightpanels-dcsfp-thread-saitek-pro-flight-panels-dcs/page/14),
[MSFS forum: Logitech FIPs and Panels](https://forums.flightsimulator.com/t/logitech-fips-and-panels/371883),
[AVSIM: Saitek and USB hubs](https://www.avsim.com/forums/topic/487471-guidelines-for-saitek-and-usb-hubs/),
[AVSIM: FIPs freezing](https://www.avsim.com/forums/topic/415754-saitek-flight-instrument-panels-freezing/).

## When a FIP stays dark after a restart

On some PCs, especially through USB 3 hubs, one or more FIPs don't wake up after Windows starts. FIPinator
can't power a FIP on; these steps fix the cause. Press **Test FIPs** in FIPinator Live afterwards: it tells you
which FIPs answered. Try them in this order; most people only need the first two or three.

### 1. Quick fix when it happens

Unplug the dark FIP's USB cable and plug it back in, or switch its hub port (or the hub) off and on (tested by
us). FIPinator picks it up again by itself within a few seconds.

### 2. Turn off Windows "Fast startup"

Fast startup doesn't fully shut down, so USB devices can come back half-asleep.

Control Panel → **Power Options** → **Choose what the power buttons do** →
**Change settings that are currently unavailable** → untick **Turn on fast startup** → Save.

### 3. Stop Windows switching USB off to save power

- Control Panel → **Power Options** → **Change plan settings** (your current plan) →
  **Change advanced power settings** → **USB settings** → **USB selective suspend setting** →
  **Disabled** → OK. (To undo: set it back to Enabled.)
- **Device Manager** → **Universal Serial Bus controllers** → for each **USB Root Hub** and
  **Generic USB Hub**: right-click → **Properties** → **Power Management** → untick
  **Allow the computer to turn off this device to save power** → OK.

### 4. Give the FIPs enough power

Seven backlit screens is a lot for one USB port. Use a hub with **its own power supply**, and
consider splitting the FIPs across two USB ports rather than chaining everything through one.
(Seven FIPs through hubs on a single USB port is a common cause.)

### 5. BIOS settings (depends on your motherboard)

Names differ between makers; check your motherboard manual. Settings that commonly matter:
"ErP" or "Deep Sleep" (turn off, so USB keeps power), "USB power in S4/S5" (on), and
"Legacy USB support" (on). Change one at a time and test.

FIPinator doesn't change any of these settings for you.
