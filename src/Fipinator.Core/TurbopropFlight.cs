// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Text.Json.Nodes;

namespace Fipinator.Core;

/// <summary>
/// Flight instruments of a 1980s turboprop/regional cockpit (reference: the Saab 340B captain's panel):
/// a wide-range airspeed indicator with a moving maximum-speed (barber pole) needle, and an
/// electronic attitude display (EADI) drawn flat, the way the cockpit's screen shows it.
/// Sim values are stock MSFS 2024 SimVars.
/// </summary>
public static class TurbopropFlight
{
    /// <summary>
    /// Airspeed 40-350 kt, low speeds opened up (40-160 over half the dial), no colour arcs, and a red
    /// and white striped needle at the sim's AIRSPEED BARBER POLE (the plane's maximum speed, which
    /// changes with altitude).
    /// </summary>
    public static string Airspeed(string name, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf") =>
        Airspeed(name, look, font, jet: false);

    /// <summary>
    /// Jet Mach/airspeed indicator: like <see cref="Airspeed"/> but 40-400 kt, plus a Mach window
    /// (AIRSPEED MACH, shown as .78) that stays blank below Mach .40, as on 727 / 737 era Boeings.
    /// The striped needle follows AIRSPEED BARBER POLE, which drops at altitude as the Mach limit takes over.
    /// </summary>
    public static string MachAirspeed(string name, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf") =>
        Airspeed(name, look, font, jet: true);

    private static string Airspeed(string name, GaugeLook look, string font, bool jet)
    {
        var g = new GaugeParts(look);
        var layers = g.Case();
        var ink = g.InkColor;
        JsonArray Pt(double v, double a) => new(v, a);
        var scales = new JsonObject
        {
            ["kt"] = new JsonObject
            {
                ["points"] = new JsonArray([Pt(0, 8), Pt(40, 15), Pt(60, 50), Pt(80, 85), Pt(100, 112), Pt(120, 138), Pt(140, 162), Pt(160, 185),
                    Pt(180, 205), Pt(200, 222), .. (jet ? new[] { Pt(250, 255), Pt(300, 284), Pt(350, 311), Pt(400, 336) } : new[] { Pt(250, 260), Pt(300, 295), Pt(350, 330) })]),
            },
        };
        JsonObject Ticks(double every, double from, double to, double length, double width) =>
            new() { ["type"] = "ticks", ["scale"] = "kt", ["every"] = every, ["from"] = from, ["to"] = to, ["radius"] = g.R(104), ["length"] = length, ["width"] = width, ["color"] = ink };
        layers.Add(Ticks(5, 40, 200, 8, 1.5));
        var top = jet ? 400 : 350;
        layers.Add(Ticks(10, 200, top, 8, 1.5));
        layers.Add(Ticks(20, 40, 200, 16, 2.8));
        layers.Add(Ticks(50, 250, top, 16, 2.8));
        var labels = new JsonArray(40, 60, 80, 100, 120, 140, 160, 180, 200, 250, 300, 350);
        if (jet) labels.Add(400);
        layers.Add(new JsonObject
        {
            ["type"] = "labels", ["scale"] = "kt", ["at"] = labels,
            ["radius"] = g.R(78), ["size"] = 20, ["color"] = ink,
        });
        layers.Add(g.Text("KNOTS", 0, -26, 12));
        if (jet)
        {
            // Mach window under the hub: digits only above Mach .40, blank below (like the real ones).
            var c = g.P(0, 36);
            var (cx, cy) = ((double)c[0]!, (double)c[1]!);
            layers.Add(new JsonObject { ["type"] = "rect", ["x"] = cx - 28, ["y"] = cy - 13, ["width"] = 56, ["height"] = 26, ["radius"] = 3, ["fill"] = "#0A0A0A", ["stroke"] = "#6A6A6A", ["strokeWidth"] = 1.2 });
            layers.Add(new JsonObject
            {
                ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = "mach_hide", ["scale"] = "px" },
                ["layers"] = new JsonArray(new JsonObject { ["type"] = "text", ["value"] = "mach", ["format"] = ".00", ["x"] = cx, ["y"] = cy + 8, ["size"] = 22, ["color"] = ink }),
            });
            layers.Add(g.Text("MACH", 0, 18, 10));
        }

        // Barber pole: a striped bar from near the hub to the scale, turned to the maximum speed.
        var stripes = new JsonArray();
        var (poleTop, bottom, seg) = (g.R(98), g.R(24), 7.0);
        var i = 0;
        for (var y = poleTop; y > bottom; y -= seg, i++)
            stripes.Add(new JsonObject { ["type"] = "rect", ["x"] = 157, ["y"] = Math.Round(120 - y, 2), ["width"] = 6, ["height"] = Math.Round(Math.Min(seg, y - bottom), 2), ["fill"] = i % 2 == 0 ? GaugeParts.Red : "#F2F2F2" });
        stripes.Add(new JsonObject { ["type"] = "rect", ["x"] = 157, ["y"] = Math.Round(120 - poleTop, 2), ["width"] = 6, ["height"] = Math.Round(poleTop - bottom, 2), ["stroke"] = "#000000", ["strokeWidth"] = 0.8 });
        layers.Add(new JsonObject { ["type"] = "group", ["rotate"] = new JsonObject { ["value"] = "vmo", ["scale"] = "kt" }, ["layers"] = stripes });

        layers.Add(g.Needle("kt", "ias", 96));
        foreach (var h in g.Hub()) layers.Add(h);
        layers.Add(g.Glass());

        var values = new JsonObject
        {
            ["ias"] = new JsonObject { ["sim"] = "AIRSPEED INDICATED", ["unit"] = "knots", ["default"] = 165 },
            ["vmo"] = new JsonObject { ["sim"] = "AIRSPEED BARBER POLE", ["unit"] = "knots", ["default"] = 250 },
        };
        if (jet)
        {
            values["mach"] = new JsonObject { ["sim"] = "AIRSPEED MACH", ["unit"] = "mach", ["default"] = 0.78 };
            values["mach_hide"] = new JsonObject { ["expr"] = "choose(mach - 0.4, 0, 400)" };
            scales["px"] = new JsonObject { ["min"] = 0, ["max"] = 400, ["startAngle"] = 0, ["endAngle"] = 400 };
            values["ias"]!["default"] = 280;
            values["vmo"]!["default"] = 340;
            return g.Gauge(name, "Mach/airspeed 40-400 kt with a Mach window (blank below Mach .40) and a striped maximum-speed needle that follows the sim's AIRSPEED BARBER POLE.", values, scales, layers, font);
        }
        return g.Gauge(name, "Airspeed 40-350 kt (low speeds spread out) with a striped maximum-speed needle that follows the sim's AIRSPEED BARBER POLE.", values, scales, layers, font);
    }

    /// <summary>
    /// Electronic attitude display: flat blue sky and brown ground filling the screen, pitch ladder
    /// (4 px per degree) and sky pointer turning with bank under a fixed bank scale, a fixed airplane
    /// symbol, radio altitude (top right, shown below 2,500 ft), decision height (bottom right) and a
    /// slip indicator. Pitch and bank use the same values and directions as the round attitude gauges.
    /// </summary>
    /// <param name="standby">737NG standby attitude style: a smaller square screen in a grey case with its knob,
    /// white airplane symbol, no radio altitude, decision height or slip indicator.</param>
    public static string Eadi(string name, string font = "BarlowCondensed-Medium.ttf", bool standby = false)
    {
        const string white = "#F4F4F4", sky = "#3D87D2", ground = "#A9714A", green = "#3BE07A";
        var symbol = standby ? white : "#FFE01A";
        // The screen: the whole FIP, or (standby) a square in the middle of the case.
        var (wx, wy, ww, wh) = standby ? (60.0, 22.0, 200.0, 196.0) : (16.0, 8.0, 288.0, 224.0);
        var k = standby ? 0.8 : 1.0; // bank scale size
        var layers = standby
            ? new JsonArray
            {
                new JsonObject { ["type"] = "rect", ["x"] = 36, ["y"] = 0, ["width"] = 248, ["height"] = 240, ["radius"] = 10, ["fill"] = "#3B3F43", ["stroke"] = "#1C1E20", ["strokeWidth"] = 2 },
                new JsonObject { ["type"] = "rect", ["x"] = 52, ["y"] = 14, ["width"] = 216, ["height"] = 212, ["radius"] = 6, ["fill"] = "#202224" },
                new JsonObject { ["type"] = "rect", ["x"] = wx, ["y"] = wy, ["width"] = ww, ["height"] = wh, ["fill"] = "#000000" },
            }
            : new JsonArray
            {
                new JsonObject { ["type"] = "rect", ["x"] = 4, ["y"] = 1, ["width"] = 312, ["height"] = 238, ["radius"] = 12, ["fill"] = "#2A2A2A", ["stroke"] = "#111111", ["strokeWidth"] = 1.5 },
                new JsonObject { ["type"] = "rect", ["x"] = 16, ["y"] = 8, ["width"] = 288, ["height"] = 224, ["radius"] = 4, ["fill"] = "#000000" },
            };

        // Pitch ladder: lines every 2.5 degrees, long numbered lines every 10, up to 20 each way.
        var horizon = new JsonArray
        {
            new JsonObject { ["type"] = "rect", ["x"] = -400, ["y"] = -900, ["width"] = 1120, ["height"] = 1020, ["fill"] = sky },
            new JsonObject { ["type"] = "rect", ["x"] = -400, ["y"] = 120, ["width"] = 1120, ["height"] = 1020, ["fill"] = ground },
            new JsonObject { ["type"] = "rect", ["x"] = -400, ["y"] = 119, ["width"] = 1120, ["height"] = 2.5, ["fill"] = white },
        };
        var ladder = new JsonArray();
        for (var p = -20.0; p <= 20; p += 2.5)
        {
            if (p == 0) continue;
            var y = 120 - p * 4;
            var w = p % 10 == 0 ? 64 : p % 5 == 0 ? 30 : 14;
            ladder.Add(new JsonObject { ["type"] = "rect", ["x"] = 160 - w / 2.0, ["y"] = Math.Round(y - 1, 2), ["width"] = w, ["height"] = 2, ["fill"] = white });
            if (p % 10 != 0) continue;
            var text = Math.Abs(p).ToString(System.Globalization.CultureInfo.InvariantCulture);
            ladder.Add(new JsonObject { ["type"] = "text", ["text"] = text, ["x"] = 122, ["y"] = Math.Round(y + 6, 2), ["size"] = 16, ["color"] = white, ["align"] = "right" });
            ladder.Add(new JsonObject { ["type"] = "text", ["text"] = text, ["x"] = 198, ["y"] = Math.Round(y + 6, 2), ["size"] = 16, ["color"] = white, ["align"] = "left" });
        }
        var banked = new JsonArray
        {
            new JsonObject { ["type"] = "group", ["moveY"] = new JsonObject { ["value"] = "pitch", ["scale"] = "pitch" }, ["layers"] = horizon },
            // The ladder only shows in the middle, clear of the bank scale and the readouts.
            new JsonObject
            {
                ["type"] = "group", ["clip"] = standby ? 70 : 90,
                ["layers"] = new JsonArray(new JsonObject { ["type"] = "group", ["moveY"] = new JsonObject { ["value"] = "pitch", ["scale"] = "pitch" }, ["layers"] = ladder }),
            },
            // Sky pointer: turns with the horizon, under the fixed bank scale.
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(0), ["radius"] = 92 * k, ["length"] = -12 * k, ["width"] = 15 * k, ["color"] = white, ["shape"] = "triangle" },
        };
        var fixedParts = new JsonArray
        {
            new JsonObject { ["type"] = "group", ["rotate"] = new JsonObject { ["value"] = "bank", ["scale"] = "bank" }, ["layers"] = banked },
            // Fixed bank scale: 10, 20 (short), 30, 60 (long), 45 (small triangles), zero index.
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(10, 20, 340, 350), ["radius"] = 104 * k, ["length"] = 9 * k, ["width"] = 2.5, ["color"] = white },
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(30, 60, 300, 330), ["radius"] = 106 * k, ["length"] = 16 * k, ["width"] = 3, ["color"] = white },
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(45, 315), ["radius"] = 104 * k, ["length"] = 9 * k, ["width"] = 9 * k, ["color"] = white, ["shape"] = "triangle" },
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(0), ["radius"] = 108 * k, ["length"] = 16 * k, ["width"] = 18 * k, ["color"] = white, ["shape"] = "triangle" },
        };
        if (!standby)
        {
            // Horizon reference marks at the screen edges.
            fixedParts.Add(new JsonObject { ["type"] = "rect", ["x"] = 18, ["y"] = 116, ["width"] = 12, ["height"] = 8, ["stroke"] = white, ["strokeWidth"] = 1.5 });
            fixedParts.Add(new JsonObject { ["type"] = "rect", ["x"] = 290, ["y"] = 116, ["width"] = 12, ["height"] = 8, ["stroke"] = white, ["strokeWidth"] = 1.5 });
        }
        layers.Add(new JsonObject
        {
            ["type"] = "group", ["clipBox"] = new JsonArray(wx, wy, ww, wh),
            ["layers"] = fixedParts,
        });

        // Fixed airplane symbol.
        var parts = standby
            ? new[] { Rect(110, 117, 36, 6), Rect(174, 117, 36, 6), Rect(140, 117, 6, 16), Rect(174, 117, 6, 16), Rect(155, 115, 10, 10) }
            : [Rect(94, 117, 44, 6), Rect(182, 117, 44, 6), Rect(132, 117, 6, 18), Rect(182, 117, 6, 18), Rect(155, 115, 10, 10)];
        foreach (var o in parts)
            layers.Add(o);
        JsonObject Rect(double x, double y, double w, double h) =>
            new() { ["type"] = "rect", ["x"] = x, ["y"] = y, ["width"] = w, ["height"] = h, ["radius"] = 1.5, ["fill"] = symbol, ["stroke"] = "#000000", ["strokeWidth"] = 1.2 };

        if (standby)
        {
            // The case's knob (bottom right) and small light (bottom left): drawn only.
            layers.Add(new JsonObject { ["type"] = "circle", ["center"] = new JsonArray(250, 226), ["r"] = 11, ["fillTop"] = "#55595D", ["fillBottom"] = "#1A1B1C", ["stroke"] = "#111111" });
            layers.Add(new JsonObject { ["type"] = "circle", ["center"] = new JsonArray(70, 229), ["r"] = 3.5, ["fill"] = "#2FA84F" });
            return StandbyGauge(name, layers, font);
        }

        // Radio altitude (top right), only below 2,500 ft; decision height (bottom right).
        layers.Add(new JsonObject
        {
            ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = "ra_hide", ["scale"] = "px" },
            ["layers"] = new JsonArray(new JsonObject { ["type"] = "text", ["value"] = "ra", ["format"] = "0", ["x"] = 296, ["y"] = 34, ["size"] = 20, ["color"] = green, ["align"] = "right" }),
        });
        layers.Add(new JsonObject { ["type"] = "text", ["text"] = "DH", ["x"] = 240, ["y"] = 224, ["size"] = 16, ["color"] = green, ["align"] = "right" });
        layers.Add(new JsonObject { ["type"] = "text", ["value"] = "dh", ["format"] = "0", ["x"] = 296, ["y"] = 224, ["size"] = 18, ["color"] = green, ["align"] = "right" });

        // Slip indicator under the airplane.
        layers.Add(new JsonObject { ["type"] = "rect", ["x"] = 132, ["y"] = 208, ["width"] = 56, ["height"] = 16, ["radius"] = 8, ["fill"] = "#101010", ["stroke"] = white, ["strokeWidth"] = 1 });
        layers.Add(new JsonObject { ["type"] = "rect", ["x"] = 151, ["y"] = 206, ["width"] = 2, ["height"] = 20, ["fill"] = white });
        layers.Add(new JsonObject { ["type"] = "rect", ["x"] = 167, ["y"] = 206, ["width"] = 2, ["height"] = 20, ["fill"] = white });
        layers.Add(new JsonObject
        {
            ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = "ball", ["scale"] = "ball" },
            ["layers"] = new JsonArray(new JsonObject { ["type"] = "circle", ["center"] = new JsonArray(160, 216), ["r"] = 6, ["fill"] = white }),
        });

        var gauge = new JsonObject
        {
            ["format"] = 1,
            ["name"] = name,
            ["author"] = "Made with FIPinator 3000",
            ["description"] = "Electronic attitude display (EADI style): flat sky and ground, pitch ladder every 2.5 degrees (to 20), bank scale 10/20/30/45/60, radio altitude below 2,500 ft, decision height, slip indicator.",
            ["canvas"] = new JsonObject { ["width"] = 320, ["height"] = 240, ["background"] = "#151515" },
            ["center"] = new JsonArray(160, 120),
            ["font"] = font,
            ["values"] = new JsonObject
            {
                ["pitch"] = new JsonObject { ["sim"] = "ATTITUDE INDICATOR PITCH DEGREES", ["unit"] = "degrees", ["default"] = -5 }, // nose up is negative in MSFS
                ["bank"] = new JsonObject { ["sim"] = "ATTITUDE INDICATOR BANK DEGREES", ["unit"] = "degrees", ["default"] = 12 },
                ["ra"] = new JsonObject { ["sim"] = "RADIO HEIGHT", ["unit"] = "feet", ["default"] = 850 },
                ["dh"] = new JsonObject { ["sim"] = "DECISION HEIGHT", ["unit"] = "feet", ["default"] = 200 },
                ["ball"] = new JsonObject { ["sim"] = "TURN COORDINATOR BALL", ["unit"] = "position", ["default"] = 0 },
                ["ra_hide"] = new JsonObject { ["expr"] = "choose(2500 - ra, 0, 400)" },
            },
            ["scales"] = new JsonObject
            {
                ["bank"] = new JsonObject { ["min"] = -180, ["max"] = 180, ["startAngle"] = -180, ["endAngle"] = 180 },
                ["pitch"] = new JsonObject { ["min"] = -90, ["max"] = 90, ["startAngle"] = 360, ["endAngle"] = -360 }, // MSFS reports nose up as negative pitch (checked in flight 2026-11-11): nose up moves the horizon down
                ["dial"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = 360, ["wrap"] = true },
                ["px"] = new JsonObject { ["min"] = 0, ["max"] = 400, ["startAngle"] = 0, ["endAngle"] = 400 },
                ["ball"] = new JsonObject { ["min"] = -127, ["max"] = 127, ["startAngle"] = -20, ["endAngle"] = 20 },
            },
            ["layers"] = layers,
        };
        return gauge.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    private static string StandbyGauge(string name, JsonArray layers, string font) => new JsonObject
    {
        ["format"] = 1,
        ["name"] = name,
        ["author"] = "Made with FIPinator 3000",
        ["description"] = "Standby attitude (737NG style): small square screen with sky and ground, pitch ladder every 2.5 degrees (to 20), bank scale 10/20/30/45/60 with sky pointer, white airplane symbol.",
        ["canvas"] = new JsonObject { ["width"] = 320, ["height"] = 240, ["background"] = "#151515" },
        ["center"] = new JsonArray(160, 120),
        ["font"] = font,
        ["values"] = new JsonObject
        {
            ["pitch"] = new JsonObject { ["sim"] = "ATTITUDE INDICATOR PITCH DEGREES", ["unit"] = "degrees", ["default"] = -5 }, // nose up is negative in MSFS
            ["bank"] = new JsonObject { ["sim"] = "ATTITUDE INDICATOR BANK DEGREES", ["unit"] = "degrees", ["default"] = 12 },
        },
        ["scales"] = new JsonObject
        {
            ["bank"] = new JsonObject { ["min"] = -180, ["max"] = 180, ["startAngle"] = -180, ["endAngle"] = 180 },
            ["pitch"] = new JsonObject { ["min"] = -90, ["max"] = 90, ["startAngle"] = 360, ["endAngle"] = -360 }, // nose up moves the horizon down
            ["dial"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = 360, ["wrap"] = true },
        },
        ["layers"] = layers,
    }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}
