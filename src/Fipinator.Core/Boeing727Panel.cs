// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fipinator.Core;

/// <summary>
/// Boeing 727 (FSS 727-200F) flight-panel instruments drawn after the owner's cockpit screenshot (2026-11-12), in black
/// and white (the sim's panel shows them lit amber; the owner wants them plain): the radio altimeter with its DH bug
/// and knob, and the IVSI.
/// </summary>
public static class Boeing727Panel
{
    private const string Ink = "#F0F0EE", Face = "#0B0B0B", Case = "#2F3133", CaseEdge = "#151617";

    private static JsonArray Pt(double x, double y) => new(x, y);
    private static string N(double v) => v.ToString(CultureInfo.InvariantCulture);

    private static JsonObject T(string text, double x, double y, double size, string color = Ink) =>
        new() { ["type"] = "text", ["text"] = text, ["x"] = Math.Round(x, 2), ["y"] = Math.Round(y, 2), ["size"] = size, ["color"] = color };

    /// <summary>A square instrument case with a round black dial in it, four screws.</summary>
    private static JsonArray Body(double cx, double cy, double r)
    {
        var layers = new JsonArray
        {
            new JsonObject { ["type"] = "rect", ["x"] = 0, ["y"] = 0, ["width"] = 320, ["height"] = 240, ["fill"] = "#202224" },
            new JsonObject { ["type"] = "rect", ["x"] = cx - 118, ["y"] = 2, ["width"] = 236, ["height"] = 236, ["radius"] = 16, ["fill"] = Case, ["stroke"] = CaseEdge, ["strokeWidth"] = 2 },
            new JsonObject { ["type"] = "circle", ["center"] = Pt(cx, cy), ["r"] = r + 6, ["fillTop"] = "#1C1D1E", ["fillBottom"] = "#050505" },
            new JsonObject { ["type"] = "circle", ["center"] = Pt(cx, cy), ["r"] = r, ["fill"] = Face },
        };
        foreach (var (x, y) in new[] { (cx - 106, 14.0), (cx + 106, 14.0), (cx - 106, 226.0), (cx + 106, 226.0) })
            layers.Add(new JsonObject { ["type"] = "screw", ["x"] = x, ["y"] = y, ["kind"] = "steel" });
        return layers;
    }

    private static string Finish(string name, string description, JsonObject values, JsonObject scales, JsonArray layers, JsonObject? controls, string font)
    {
        var gauge = new JsonObject
        {
            ["format"] = 1,
            ["name"] = name,
            ["author"] = "Made with FIPinator 3000",
            ["description"] = description,
            ["canvas"] = new JsonObject { ["width"] = 320, ["height"] = 240, ["background"] = "#202224" },
            ["center"] = new JsonArray(160, 120),
            ["font"] = font,
            ["values"] = values,
            ["scales"] = scales,
            ["layers"] = layers,
        };
        if (controls is not null) gauge["controls"] = controls;
        return gauge.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// The 727's radio altimeter: 0 at the top, 0-500 ft evenly round to 5 at the bottom, then 1,000 / 1,500 / 2,000 /
    /// 2,500 squeezed up the left side, the dark part from 2,500 back to 0; DH bug (yellow triangle) and the DH knob
    /// (the FIP's right knob, 10 ft a click), the green DH light and the TEST button. Needle and bug read the FSS 727's
    /// own values, which are their places on the dial as a share of a full turn (checked for the bug: 25 clicks =
    /// 0.25 = 250 ft).
    /// </summary>
    public static string RadioAltimeter(string font = "BarlowCondensed-Medium.ttf")
    {
        const double cx = 160, cy = 120, r = 104;
        var layers = Body(cx, cy, r);
        JsonArray C() => Pt(cx, cy);
        // Dark part of the dial: above 2,500 ft.
        layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = "ft", ["from"] = 2500, ["to"] = 2700, ["radius"] = Math.Round(r * 0.5, 2), ["width"] = r, ["color"] = "#2A2A2A", ["center"] = C() });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "ft", ["every"] = 20, ["from"] = 0, ["to"] = 500, ["radius"] = r - 2, ["length"] = 7, ["width"] = 1.4, ["color"] = Ink, ["center"] = C() });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "ft", ["every"] = 100, ["from"] = 0, ["to"] = 500, ["radius"] = r - 2, ["length"] = 15, ["width"] = 3, ["color"] = Ink, ["center"] = C() });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "ft", ["every"] = 100, ["from"] = 600, ["to"] = 2500, ["radius"] = r - 2, ["length"] = 7, ["width"] = 1.4, ["color"] = Ink, ["center"] = C() });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "ft", ["at"] = new JsonArray(1000, 1500, 2000, 2500), ["radius"] = r - 2, ["length"] = 15, ["width"] = 3, ["color"] = Ink, ["center"] = C() });
        var texts = new JsonObject { ["0"] = "0", ["100"] = "1", ["200"] = "2", ["300"] = "3", ["400"] = "4", ["500"] = "5", ["1000"] = "10", ["1500"] = "15", ["2000"] = "20", ["2500"] = "25" };
        layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = "ft", ["at"] = new JsonArray(0, 100, 200, 300, 400, 500, 1000, 1500, 2000, 2500), ["texts"] = texts, ["radius"] = r - 32, ["size"] = 24, ["color"] = Ink, ["center"] = C() });
        layers.Add(T("X", cx - 40, cy + 2, 15));
        layers.Add(T("100 FEET", cx + 30, cy + 2, 14));
        layers.Add(T("RADIO", cx + 4, cy + 34, 26));
        layers.Add(T("ALT", cx + 4, cy + 60, 26));

        // DH bug: a yellow triangle at the rim, turning with the FSS 727's marker value.
        layers.Add(new JsonObject
        {
            ["type"] = "group", ["rotate"] = new JsonObject { ["value"] = "dh", ["scale"] = "turn" }, ["center"] = C(),
            ["layers"] = new JsonArray(
                new JsonObject { ["type"] = "ticks", ["scale"] = "turn", ["at"] = new JsonArray(0), ["radius"] = r + 4, ["length"] = 14, ["width"] = 16, ["color"] = "#F2C21A", ["shape"] = "triangle", ["center"] = C() },
                new JsonObject { ["type"] = "ticks", ["scale"] = "turn", ["at"] = new JsonArray(0), ["radius"] = r + 1, ["length"] = 8, ["width"] = 8, ["color"] = Face, ["shape"] = "triangle", ["center"] = C() }),
        });
        layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "turn", ["value"] = "needle", ["length"] = r - 10, ["width"] = 7, ["tail"] = 0, ["color"] = "#F6F6F4", ["shadow"] = false, ["center"] = C() });
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = C(), ["r"] = 8, ["fill"] = "#141414" });

        // Green DH light (top left), TEST button (bottom left), DH knob (bottom right), as on the case.
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = Pt(cx - 98, 30), ["r"] = 9, ["fill"] = "#103A16", ["stroke"] = "#0A0A0A" });
        layers.Add(new JsonObject
        {
            ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = "light_hide", ["scale"] = "px" },
            ["layers"] = new JsonArray(new JsonObject { ["type"] = "circle", ["center"] = Pt(cx - 98, 30), ["r"] = 9, ["fill"] = "#43E05A", ["stroke"] = "#0A0A0A" }),
        });
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = Pt(cx - 98, 204), ["r"] = 6, ["fill"] = "#141414", ["stroke"] = "#3A3A3A" });
        layers.Add(T("TEST", cx - 80, 228, 10));
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = Pt(cx + 98, 206), ["r"] = 15, ["fillTop"] = "#2A2A2A", ["fillBottom"] = "#080808", ["stroke"] = "#4A4A4A" });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "turn", ["at"] = new JsonArray(0.5), ["radius"] = 9, ["length"] = 15, ["width"] = 14, ["color"] = Ink, ["shape"] = "triangle", ["center"] = Pt(cx + 98, 200) });

        // Off flag, while the radio altimeter has no reading.
        layers.Add(new JsonObject
        {
            ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = "flag_hide", ["scale"] = "px" },
            ["layers"] = new JsonArray(
                new JsonObject { ["type"] = "rect", ["x"] = cx + 34, ["y"] = cy - 42, ["width"] = 34, ["height"] = 18, ["fill"] = "#D0302A" },
                T("OFF", cx + 51, cy - 28, 13)),
        });

        var values = new JsonObject
        {
            ["needle"] = new JsonObject { ["sim"] = "L:FSS_B727_CP_RADIO_ALT_NEEDLE", ["unit"] = "number", ["default"] = 0.12 },
            ["dh"] = new JsonObject { ["sim"] = "L:FSS_B727_CP_RADIO_ALT_DH_MARKER", ["unit"] = "number", ["default"] = 0.2 },
            ["light"] = new JsonObject { ["sim"] = "L:FSS_B727_CP_RADIO_ALT_WARNING_LIGHT", ["unit"] = "number", ["default"] = 0 },
            ["flag"] = new JsonObject { ["sim"] = "L:FSS_B727_CP_RADIO_ALT_FLAG_VISIBLE", ["unit"] = "number", ["default"] = 0 },
            ["light_hide"] = new JsonObject { ["expr"] = "choose(light - 0.5, 0, 400)" },
            ["flag_hide"] = new JsonObject { ["expr"] = "choose(flag - 0.5, 0, 400)" },
        };
        var scales = new JsonObject
        {
            // Where each height sits on the dial, degrees clockwise from 12 o'clock (measured from the screenshot).
            ["ft"] = new JsonObject { ["points"] = new JsonArray(Row(0, 0), Row(500, 180), Row(1000, 228), Row(1500, 275), Row(2000, 299), Row(2500, 324), Row(2700, 360)) },
            ["turn"] = new JsonObject { ["min"] = 0, ["max"] = 1, ["startAngle"] = 0, ["endAngle"] = 360 },
            ["px"] = new JsonObject { ["min"] = 0, ["max"] = 400, ["startAngle"] = 0, ["endAngle"] = 400 },
        };
        var controls = new JsonObject
        {
            ["rightKnobClockwise"] = new JsonObject { ["set"] = "L:FSS_B727_CP_RADIO_ALT_DH_KNOB", ["add"] = 1 },
            ["rightKnobAnticlockwise"] = new JsonObject { ["set"] = "L:FSS_B727_CP_RADIO_ALT_DH_KNOB", ["add"] = -1 },
        };
        return Finish("727 Radio altimeter", "Boeing 727 (FSS) radio altimeter in black and white: 0-500 ft round to the bottom, up to 2,500 ft up the left, yellow DH bug, green DH light. The right knob sets the DH (10 ft a click), like the cockpit's knob (FIPinator Live).",
            values, scales, layers, controls, font);
    }

    /// <summary>
    /// The 727's IVSI: 0 at 9 o'clock, up above and down below, marked .5, 1, 2, 4 and 6 (thousand ft/min) on the
    /// uneven scale of the real one (6 up and 6 down meet at 3 o'clock). Reads the sim's VERTICAL SPEED.
    /// </summary>
    public static string Ivsi(string font = "BarlowCondensed-Medium.ttf")
    {
        const double cx = 160, cy = 120, r = 104;
        var layers = Body(cx, cy, r);
        JsonArray C() => Pt(cx, cy);
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "vs", ["every"] = 100, ["from"] = -1000, ["to"] = 1000, ["radius"] = r - 2, ["length"] = 7, ["width"] = 1.4, ["color"] = Ink, ["center"] = C() });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "vs", ["every"] = 500, ["from"] = -6000, ["to"] = 6000, ["radius"] = r - 2, ["length"] = 9, ["width"] = 1.8, ["color"] = Ink, ["center"] = C() });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "vs", ["at"] = new JsonArray(-6000, -4000, -2000, -1000, -500, 0, 500, 1000, 2000, 4000, 6000), ["radius"] = r - 2, ["length"] = 17, ["width"] = 3.2, ["color"] = Ink, ["center"] = C() });
        var texts = new JsonObject { ["-4000"] = "4", ["-2000"] = "2", ["-1000"] = "1", ["0"] = "0", ["1000"] = "1", ["2000"] = "2", ["4000"] = "4", ["6000"] = "6" };
        layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = "vs", ["at"] = new JsonArray(-4000, -2000, -1000, 0, 1000, 2000, 4000, 6000), ["texts"] = texts, ["radius"] = r - 34, ["size"] = 34, ["color"] = Ink, ["center"] = C() });
        layers.Add(T(".5", cx - 82, cy - 38, 15));
        layers.Add(T("UP", cx - 62, cy - 30, 14));
        layers.Add(T("DOWN", cx - 62, cy + 40, 14));
        layers.Add(T(".5", cx - 82, cy + 50, 15));
        layers.Add(T("1000 FT PER MIN", cx + 6, cy - 24, 12));
        layers.Add(T("IVSI", cx + 4, cy - 2, 26));
        layers.Add(T("VERTICAL", cx + 4, cy + 34, 17));
        layers.Add(T("SPEED", cx + 4, cy + 52, 17));
        layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "vs", ["value"] = "vs", ["length"] = r - 8, ["width"] = 5, ["tail"] = 74, ["color"] = "#F6F6F4", ["tailColor"] = "#F6F6F4", ["shadow"] = false, ["center"] = C() });
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = C(), ["r"] = 10, ["fill"] = "#141414" });

        var values = new JsonObject
        {
            ["vs_raw"] = new JsonObject { ["sim"] = "VERTICAL SPEED", ["unit"] = "feet per minute", ["default"] = 1200 },
            ["vs"] = new JsonObject { ["expr"] = "clamp(vs_raw, -6000, 6000)" },
        };
        var scales = new JsonObject
        {
            // Degrees clockwise from 12 o'clock, measured from the screenshot: 0 at 9 o'clock, both 6s at 3 o'clock.
            ["vs"] = new JsonObject
            {
                ["points"] = new JsonArray(Row(-6000, 90), Row(-4000, 146), Row(-2000, 190), Row(-1000, 220), Row(-500, 240), Row(0, 270),
                    Row(500, 300), Row(1000, 320), Row(2000, 350), Row(4000, 394), Row(6000, 450)),
            },
        };
        return Finish("727 IVSI", "Boeing 727 IVSI (vertical speed) in black and white: 0 at 9 o'clock, up above and down below, .5 / 1 / 2 / 4 / 6 thousand ft/min on the 727's uneven scale.",
            values, scales, layers, null, font);
    }

    private static JsonArray Row(double value, double angle) => new(value, angle);
}
