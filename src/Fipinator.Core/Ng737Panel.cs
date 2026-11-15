// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fipinator.Core;

/// <summary>
/// Gauges from the 737NG's (PMDG 737-800's) centre panel that the other packs don't have.
/// </summary>
public static class Ng737Panel
{
    /// <summary>The flap lever's detents in order (FLAPS HANDLE INDEX 0-8).</summary>
    private static readonly string[] Detents = ["UP", "1", "2", "5", "10", "15", "25", "30", "40"];

    /// <summary>
    /// The 737NG upper engine display, compacted for one FIP: TAT and the two N1 dials as on the
    /// PMDG 737-800's screen (zero at 3 o'clock, the scale running clockwise round the bottom to the red line at
    /// about 10-11 o'clock, a grey wedge from zero to the needle, the number in a box at the top right that turns red
    /// past the red line), and the fuel quantity box (left, centre, right, total, pounds x 1000) at the bottom. EGT
    /// and fuel flow are left off: the PMDG shows its own values, which it doesn't share (owner's call, 2026-11-12). Drawn by us after the PMDG cockpit (owner's screenshot, 2026-11-12).
    /// </summary>
    public static string UpperEngines(JetProfile plane, string font = "BarlowCondensed-Medium.ttf")
    {
        const string cream = "#E9E5D8", cyan = "#5CC8F0", wedge = "#6E6874", redInk = "#FF3B30", amber = "#F5A623";
        var layers = new JsonArray();
        var values = new JsonObject();
        // Scale angles (degrees clockwise from 12 o'clock): zero at 3 o'clock; N1 100 % at about 9:30.
        var scales = new JsonObject
        {
            ["n1s"] = new JsonObject { ["min"] = 0, ["max"] = 110, ["startAngle"] = 90, ["endAngle"] = 90 + 110 * 1.95 },
        };

        void Dial(string key, int e, double cx, double cy, double r, string sim, string unit, double sample, double top, double red, double? caution,
            double[] ticks, double[] labels, string format, double boxW)
        {
            var v = $"{key}{e}";
            values[v] = new JsonObject { ["sim"] = ClassicPack.Engine(sim, e), ["unit"] = unit, ["default"] = Math.Round(sample * (1 + (e - 1) * 0.02), 1) };
            var s = $"{key}s";
            layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = s, ["from"] = 0, ["toValue"] = v, ["radius"] = Math.Round(r / 2, 2), ["width"] = r, ["color"] = wedge, ["center"] = Pt(cx, cy) });
            layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = s, ["from"] = 0, ["to"] = top, ["radius"] = r, ["width"] = 2.2, ["color"] = cream, ["center"] = Pt(cx, cy) });
            if (ticks.Length > 0)
                layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = s, ["at"] = new JsonArray([.. ticks.Select(t => (JsonNode)t)]), ["radius"] = r, ["length"] = 5, ["width"] = 2, ["color"] = cream, ["center"] = Pt(cx, cy) });
            if (labels.Length > 0)
                layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = s, ["at"] = new JsonArray([.. labels.Select(t => (JsonNode)t)]), ["divideBy"] = 10, ["radius"] = Math.Round(r - 12, 2), ["size"] = 12, ["color"] = cream, ["center"] = Pt(cx, cy) });
            if (caution is double c)
                layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = s, ["at"] = new JsonArray(c), ["radius"] = r + 7, ["length"] = 7, ["width"] = 3, ["color"] = amber, ["center"] = Pt(cx, cy) });
            layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = s, ["at"] = new JsonArray(red), ["radius"] = r + 8, ["length"] = 9, ["width"] = 3, ["color"] = redInk, ["center"] = Pt(cx, cy) });
            layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = s, ["value"] = v, ["length"] = r + 4, ["width"] = 3, ["tail"] = 0, ["color"] = cream, ["center"] = Pt(cx, cy) });
            // The number: a box at the top right, its bottom edge just above the dial's middle.
            var bh = Math.Round(r * 0.55, 1);
            var (bx, by) = (cx + 2, cy - r * (key == "n1" ? 0.95 : 0.62));
            layers.Add(new JsonObject { ["type"] = "rect", ["x"] = Math.Round(bx, 2), ["y"] = Math.Round(by, 2), ["width"] = boxW, ["height"] = bh, ["fill"] = "#000000", ["stroke"] = cream, ["strokeWidth"] = 1.5 });
            MiniPanels.AddReadout(layers, values, v, format, bx + boxW - 3, by + bh * 0.84, Math.Round(bh * 0.86, 1), red, cream);
        }

        layers.Add(new JsonObject { ["type"] = "text", ["text"] = "TAT", ["x"] = 6, ["y"] = 15, ["size"] = 13, ["color"] = cyan, ["align"] = "left" });
        values["tat"] = new JsonObject { ["sim"] = "TOTAL AIR TEMPERATURE", ["unit"] = "celsius", ["default"] = -1 };
        layers.Add(new JsonObject { ["type"] = "text", ["value"] = "tat", ["format"] = "0", ["x"] = 54, ["y"] = 15, ["size"] = 14, ["color"] = cream, ["align"] = "right" });
        layers.Add(new JsonObject { ["type"] = "text", ["text"] = "C", ["x"] = 57, ["y"] = 15, ["size"] = 12, ["color"] = cream, ["align"] = "left" });

        for (var e = 1; e <= 2; e++)
        {
            var cx = e == 1 ? 62.0 : 218.0;
            Dial("n1", e, cx, 100, 56, plane.N1Sim, "percent", 85, plane.N1Red, plane.N1Red, null,
                [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100], [0, 20, 40, 60, 80, 100], "0.0", 84);
        }
        layers.Add(new JsonObject { ["type"] = "text", ["text"] = "N1", ["x"] = 154, ["y"] = 162, ["size"] = 17, ["color"] = cyan });

        // Fuel quantity box: tanks in pounds x 1000 (the sim keeps gallons; times the fuel's weight per gallon).
        values["fuel_lb_gal"] = new JsonObject { ["sim"] = "FUEL WEIGHT PER GALLON", ["unit"] = "pounds", ["default"] = 6.7 };
        (string Key, string Sim, double Sample)[] tanks = [("fuel_l", "FUEL TANK LEFT MAIN QUANTITY", 1288), ("fuel_c", "FUEL TANK CENTER QUANTITY", 814), ("fuel_r", "FUEL TANK RIGHT MAIN QUANTITY", 1288)];
        layers.Add(new JsonObject { ["type"] = "rect", ["x"] = 70, ["y"] = 206, ["width"] = 244, ["height"] = 32, ["radius"] = 6, ["fill"] = "#000000", ["stroke"] = cyan, ["strokeWidth"] = 1.5 });
        layers.Add(new JsonObject { ["type"] = "rect", ["x"] = 112, ["y"] = 200, ["width"] = 160, ["height"] = 12, ["fill"] = "#000000" });
        layers.Add(new JsonObject { ["type"] = "text", ["text"] = "FUEL QTY - LBS X 1000", ["x"] = 192, ["y"] = 210, ["size"] = 11, ["color"] = cyan });
        for (var t = 0; t < tanks.Length; t++)
        {
            values[$"{tanks[t].Key}_gal"] = new JsonObject { ["sim"] = tanks[t].Sim, ["unit"] = "gallons", ["default"] = tanks[t].Sample };
            values[tanks[t].Key] = new JsonObject { ["expr"] = $"{tanks[t].Key}_gal * fuel_lb_gal / 1000" };
            layers.Add(new JsonObject { ["type"] = "text", ["value"] = tanks[t].Key, ["format"] = "0.00", ["x"] = 136 + t * 64, ["y"] = 231, ["size"] = 18, ["color"] = cream, ["align"] = "right" });
        }
        values["fuel_total_lb"] = new JsonObject { ["sim"] = "FUEL TOTAL QUANTITY WEIGHT", ["unit"] = "pounds", ["default"] = 22700 };
        values["fuel_total"] = new JsonObject { ["expr"] = "fuel_total_lb / 1000" };
        layers.Add(new JsonObject { ["type"] = "text", ["text"] = "TOTAL", ["x"] = 8, ["y"] = 232, ["size"] = 12, ["color"] = cyan, ["align"] = "left" });
        layers.Add(new JsonObject { ["type"] = "text", ["value"] = "fuel_total", ["format"] = "0.0", ["x"] = 64, ["y"] = 233, ["size"] = 19, ["color"] = cream, ["align"] = "right" });

        scales["px"] = new JsonObject { ["min"] = 0, ["max"] = 400, ["startAngle"] = 0, ["endAngle"] = 400 };
        var gauge = new JsonObject
        {
            ["format"] = 1,
            ["name"] = $"{plane.Name} N1 and fuel",
            ["author"] = "Made with FIPinator 3000",
            ["description"] = $"{plane.Name} upper engine display, compact: TAT, the N1 dials with their numbers, and fuel per tank and total (lb x 1000). No EGT or fuel flow: the add-on keeps its own and doesn't share them (see docs/sim-interface-notes.md). Limits: {plane.Source}.",
            ["canvas"] = new JsonObject { ["width"] = 320, ["height"] = 240, ["background"] = "#000000" },
            ["center"] = new JsonArray(160, 120),
            ["font"] = font,
            ["values"] = values,
            ["scales"] = scales,
            ["layers"] = layers,
        };
        return gauge.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    // ---- Shared bits for the PMDG-look standby instruments ----

    private const string Cream = "#EDE9DC", PanelGrey = "#4B4F53", PlateGrey = "#43474B", LabelBlue = "#5AA0E0";

    private static JsonArray Pt(double x, double y) => new(x, y);

    private static JsonObject T(string text, double x, double y, double size, string color = Cream, string? align = null)
    {
        var o = new JsonObject { ["type"] = "text", ["text"] = text, ["x"] = Math.Round(x, 2), ["y"] = Math.Round(y, 2), ["size"] = size, ["color"] = color };
        if (align is not null) o["align"] = align;
        return o;
    }

    private static JsonObject Box(double x, double y, double w, double h, string fill, string? stroke = null, double radius = 0, double strokeWidth = 1.5)
    {
        var o = new JsonObject { ["type"] = "rect", ["x"] = Math.Round(x, 2), ["y"] = Math.Round(y, 2), ["width"] = Math.Round(w, 2), ["height"] = Math.Round(h, 2), ["fill"] = fill };
        if (radius > 0) o["radius"] = radius;
        if (stroke is not null) { o["stroke"] = stroke; o["strokeWidth"] = strokeWidth; }
        return o;
    }

    /// <summary>A grey instrument case on the panel, four screws in the corners.</summary>
    private static JsonArray Case(double x, double w)
    {
        var layers = new JsonArray
        {
            Box(0, 0, 320, 240, PanelGrey),
            Box(x, 1, w, 238, PlateGrey, "#2C2F32", 22, 2),
        };
        foreach (var (sx, sy) in new[] { (x + 14, 14.0), (x + w - 14, 14.0), (x + 14, 226.0), (x + w - 14, 226.0) })
            layers.Add(new JsonObject { ["type"] = "screw", ["x"] = sx, ["y"] = sy, ["kind"] = "steel" });
        return layers;
    }

    private static string Finish(string name, string description, JsonObject values, JsonObject scales, JsonArray layers, string font) => new JsonObject
    {
        ["format"] = 1,
        ["name"] = name,
        ["author"] = "Made with FIPinator 3000",
        ["description"] = description,
        ["canvas"] = new JsonObject { ["width"] = 320, ["height"] = 240, ["background"] = PanelGrey },
        ["center"] = new JsonArray(160, 120),
        ["font"] = font,
        ["values"] = values,
        ["scales"] = scales,
        ["layers"] = layers,
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>
    /// 737NG standby altimeter/airspeed, drawn after the PMDG 737-800's: the altimeter in the top of the round
    /// dial (pointer once round per 1,000 ft, notched counter window with a hatched box below 10,000 ft, MB
    /// and IN HG drums at the sides, "ALT"), a line across, and the airspeed strip below it (knots scale sliding
    /// past a fixed pointer, red/white stripes below 30 kt), BARO knob bottom left. Reads the sim's second
    /// altimeter (INDICATED ALTITUDE:2, KOHLSMAN SETTING:2), which the PMDG fills (checked on the ground
    /// 2026-11-12). The knobs send the standard KOHLSMAN_INC/DEC: whether the PMDG's standby listens is untested.
    /// </summary>
    public static string StandbyAltimeter(string name, string font = "BarlowCondensed-Medium.ttf")
    {
        const double cx = 160, cy = 120, ay = 92; // dial centre; the altimeter scale's centre sits higher
        var layers = Case(22, 276);
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = Pt(cx, cy), ["r"] = 116, ["fillTop"] = "#2E3134", ["fillBottom"] = "#1A1C1E" });
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = Pt(cx, cy), ["r"] = 108, ["fill"] = "#050505" });
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = Pt(270, 30), ["r"] = 6, ["fill"] = "#6B5A2E", ["stroke"] = "#2A2418" });

        // Altimeter scale: marks every 20 ft, bars every 100, numbers 0-9.
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "k", ["every"] = 20, ["from"] = 0, ["to"] = 980, ["radius"] = 64, ["length"] = 6, ["width"] = 1.8, ["color"] = Cream, ["center"] = Pt(cx, ay) });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "k", ["every"] = 100, ["from"] = 0, ["to"] = 900, ["radius"] = 64, ["length"] = 11, ["width"] = 3.5, ["color"] = Cream, ["center"] = Pt(cx, ay) });
        layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = "k", ["every"] = 100, ["from"] = 0, ["to"] = 900, ["divideBy"] = 100, ["radius"] = 47, ["size"] = 19, ["color"] = Cream, ["center"] = Pt(cx, ay) });
        layers.Add(T("100", 146, 46, 8, LabelBlue));
        layers.Add(T("FEET", 175, 46, 8, LabelBlue));

        // Notched counter window, the hatched box (shown below 10,000 ft) and the altitude.
        layers.Add(Box(122, 72, 78, 18, "#050505", Cream));
        layers.Add(Box(142, 66, 54, 30, "#050505", Cream));
        layers.Add(Box(143, 73, 52, 16, "#050505"));
        var hatch = new JsonArray();
        for (var row = 0; row < 5; row++)
            for (var col = 0; col < 4; col++)
                if ((row + col) % 2 == 0) hatch.Add(Box(126 + col * 3, 74 + row * 2.8, 3, 2.8, Cream));
        layers.Add(new JsonObject { ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = "hatch_hide", ["scale"] = "px" }, ["layers"] = hatch });
        layers.Add(new JsonObject { ["type"] = "text", ["value"] = "counter", ["format"] = "#,0000", ["x"] = 194, ["y"] = 89, ["size"] = 20, ["color"] = Cream, ["align"] = "right" });

        layers.Add(T("ALT", cx, 121, 14));
        layers.Add(T("MB", 46, 124, 10, LabelBlue));
        layers.Add(new JsonObject { ["type"] = "text", ["value"] = "hpa", ["format"] = "0", ["x"] = 46, ["y"] = 145, ["size"] = 18, ["color"] = Cream });
        layers.Add(T("IN HG", 274, 124, 10, LabelBlue));
        layers.Add(new JsonObject { ["type"] = "text", ["value"] = "inhg100", ["format"] = "0", ["x"] = 274, ["y"] = 145, ["size"] = 18, ["color"] = Cream });
        layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "k", ["value"] = "hundreds", ["length"] = 58, ["width"] = 2.4, ["tail"] = 0, ["color"] = Cream, ["center"] = Pt(cx, ay) });
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = Pt(cx, ay), ["r"] = 3, ["fill"] = Cream });
        layers.Add(Box(52, 158, 216, 2.5, Cream));

        // Airspeed strip: 1.3 px per knot from 30 kt, read at the fixed pointer in the middle.
        const double px = 1.3;
        layers.Add(Box(98, 165, 124, 32, "#0B0B0B", "#2A2A2A"));
        var tape = new JsonArray();
        for (var kt = -40; kt < 30; kt += 6)
            tape.Add(Box(cx + (kt - 30) * px, 168, 3.9, 26, (kt / 6) % 2 == 0 ? "#E23B2E" : "#F2F2F2"));
        for (var kt = 30; kt <= 400; kt += 10)
        {
            var x = Math.Round(cx + (kt - 30) * px, 2);
            tape.Add(Box(x - 1, kt % 20 == 0 ? 183 : 187, 2, kt % 20 == 0 ? 11 : 7, Cream));
            if (kt % 20 == 0 && kt >= 40) tape.Add(T(kt.ToString(System.Globalization.CultureInfo.InvariantCulture), x, 181, 14));
        }
        layers.Add(new JsonObject
        {
            ["type"] = "group", ["clipBox"] = new JsonArray(99, 166, 122, 30),
            ["layers"] = new JsonArray(new JsonObject { ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = "ias_c", ["scale"] = "ias_px" }, ["layers"] = tape }),
        });
        layers.Add(Box(cx - 2, 198, 4, 10, Cream, radius: 1));
        layers.Add(T("IAS", 124, 214, 15, LabelBlue));
        layers.Add(T("KNOTS", 198, 212, 9, LabelBlue));

        // BARO knob, bottom left.
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = Pt(42, 208), ["r"] = 20, ["fillTop"] = "#D2D0C8", ["fillBottom"] = "#7A7872", ["stroke"] = "#3A3A38" });
        layers.Add(T("BARO", 42, 211, 8, "#3A3A38"));

        var values = new JsonObject
        {
            ["alt"] = new JsonObject { ["sim"] = "INDICATED ALTITUDE:2", ["unit"] = "feet", ["default"] = 3740 },
            ["inhg"] = new JsonObject { ["sim"] = "KOHLSMAN SETTING HG:2", ["unit"] = "inHg", ["default"] = 29.71 },
            ["hpa"] = new JsonObject { ["sim"] = "KOHLSMAN SETTING MB:2", ["unit"] = "millibars", ["default"] = 1006 },
            ["ias"] = new JsonObject { ["sim"] = "AIRSPEED INDICATED", ["unit"] = "knots", ["default"] = 140 },
            ["inhg100"] = new JsonObject { ["expr"] = "inhg * 100" },
            ["hundreds"] = new JsonObject { ["expr"] = "(alt % 1000 + 1000) % 1000" },
            ["counter"] = new JsonObject { ["expr"] = "alt - alt % 20" },
            ["hatch_hide"] = new JsonObject { ["expr"] = "choose(10000 - alt, 0, 400)" },
            ["ias_c"] = new JsonObject { ["expr"] = "clamp(ias, 30, 400)" },
        };
        var scales = new JsonObject
        {
            ["k"] = new JsonObject { ["min"] = 0, ["max"] = 1000, ["startAngle"] = 0, ["endAngle"] = 360, ["wrap"] = true },
            ["ias_px"] = new JsonObject { ["min"] = 30, ["max"] = 400, ["startAngle"] = 0, ["endAngle"] = Math.Round(-370 * px, 2) },
            ["px"] = new JsonObject { ["min"] = 0, ["max"] = 400, ["startAngle"] = 0, ["endAngle"] = 400 },
        };
        var json = Finish(name, "737NG standby altimeter and airspeed: pointer once round per 1,000 ft, counter window, MB and IN HG, and an airspeed strip sliding past a pointer. Knobs set the baro.", values, scales, layers, font);
        return ClassicExtras.WithControls(json, ClassicExtras.Knobs("KOHLSMAN_INC", "KOHLSMAN_DEC"));
    }

    /// <summary>
    /// 737NG radio magnetic indicator, drawn after the PMDG 737-800's: octagonal black face, compass card turning
    /// with heading, fixed index triangles, the single needle to VOR 1 and the double needle to VOR 2 (both white;
    /// each rests at 3 o'clock with no signal), orange warning flags at 9 o'clock (VOR 1) and 3 o'clock (VOR 2)
    /// while that VOR has no signal, VOR/ADF labels and the two selector knobs (drawn; they don't switch).
    /// </summary>
    public static string Rmi(string name, string font = "BarlowCondensed-Medium.ttf")
    {
        const double cx = 160, cy = 112;
        var layers = Case(20, 280);
        layers.Add(Box(58, 6, 204, 206, "#2E3134", radius: 34));
        layers.Add(Box(64, 12, 192, 194, "#060606", radius: 30));

        var card = new JsonArray
        {
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["every"] = 5, ["from"] = 0, ["to"] = 355, ["radius"] = 92, ["length"] = 8, ["width"] = 2, ["color"] = Cream, ["center"] = Pt(cx, cy) },
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["every"] = 10, ["from"] = 0, ["to"] = 350, ["radius"] = 92, ["length"] = 14, ["width"] = 3, ["color"] = Cream, ["center"] = Pt(cx, cy) },
            new JsonObject { ["type"] = "labels", ["scale"] = "dial", ["every"] = 30, ["from"] = 0, ["to"] = 330, ["divideBy"] = 10, ["radius"] = 68, ["size"] = 22, ["color"] = Cream, ["center"] = Pt(cx, cy) },
        };
        layers.Add(new JsonObject { ["type"] = "group", ["rotate"] = new JsonObject { ["value"] = "heading", ["scale"] = "card" }, ["center"] = Pt(cx, cy), ["layers"] = card });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(0), ["radius"] = 100, ["length"] = 10, ["width"] = 11, ["color"] = Cream, ["shape"] = "triangle", ["center"] = Pt(cx, cy) });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(45, 90, 135, 180, 225, 270, 315), ["radius"] = 102, ["length"] = 7, ["width"] = 8, ["color"] = Cream, ["shape"] = "triangle", ["center"] = Pt(cx, cy) });

        // Orange flags while a VOR has no signal.
        void Flag(string hide, double from, double to) => layers.Add(new JsonObject
        {
            ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = hide, ["scale"] = "px" },
            ["layers"] = new JsonArray(new JsonObject { ["type"] = "arc", ["scale"] = "dial", ["from"] = from, ["to"] = to, ["radius"] = 82, ["width"] = 7, ["color"] = "#E8921E", ["center"] = Pt(cx, cy) }),
        });
        Flag("vor1_flag", 245, 290);
        Flag("vor2_flag", 70, 115);

        layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "dial", ["value"] = "vor2_needle", ["style"] = "doubleArrow", ["length"] = 84, ["tail"] = 78, ["width"] = 4.5, ["color"] = Cream, ["center"] = Pt(cx, cy) });
        layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "dial", ["value"] = "vor1_needle", ["style"] = "arrow", ["length"] = 84, ["tail"] = 78, ["width"] = 6, ["color"] = Cream, ["center"] = Pt(cx, cy) });
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = Pt(cx, cy), ["r"] = 9, ["fill"] = "#111111" });

        layers.Add(T("VOR", 42, 128, 12));
        layers.Add(T("VOR", 278, 128, 12));
        layers.Add(T("ADF <--   ==> ADF", cx, 226, 12));
        // The two selector knobs (single arrow left, double arrow right): drawn only.
        foreach (var (kx, lines) in new[] { (48.0, 1), (272.0, 2) })
        {
            layers.Add(new JsonObject { ["type"] = "circle", ["center"] = Pt(kx, 206), ["r"] = 20, ["fillTop"] = "#5C6064", ["fillBottom"] = "#1E2022", ["stroke"] = "#111111" });
            for (var l = 0; l < lines; l++)
                layers.Add(Box(kx - 1 + (lines == 2 ? (l == 0 ? -3 : 3) : 0), 194, 2, 22, Cream));
        }

        var values = new JsonObject
        {
            ["heading"] = new JsonObject { ["sim"] = "PLANE HEADING DEGREES MAGNETIC", ["unit"] = "degrees", ["default"] = 300 },
            ["vor1_radial"] = new JsonObject { ["sim"] = "NAV RADIAL:1", ["unit"] = "degrees", ["default"] = 120 },
            ["vor1_signal"] = new JsonObject { ["sim"] = "NAV HAS NAV:1", ["unit"] = "bool", ["default"] = 1 },
            ["vor2_radial"] = new JsonObject { ["sim"] = "NAV RADIAL:2", ["unit"] = "degrees", ["default"] = 200 },
            ["vor2_signal"] = new JsonObject { ["sim"] = "NAV HAS NAV:2", ["unit"] = "bool", ["default"] = 0 },
            ["vor1_needle"] = new JsonObject { ["expr"] = "choose(vor1_signal, wrap360(vor1_radial + 180 - heading), 90)" },
            ["vor2_needle"] = new JsonObject { ["expr"] = "choose(vor2_signal, wrap360(vor2_radial + 180 - heading), 90)" },
            ["vor1_flag"] = new JsonObject { ["expr"] = "choose(vor1_signal, 400, 0)" },
            ["vor2_flag"] = new JsonObject { ["expr"] = "choose(vor2_signal, 400, 0)" },
        };
        var scales = new JsonObject
        {
            ["card"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = -360 },
            ["dial"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = 360, ["wrap"] = true },
            ["px"] = new JsonObject { ["min"] = 0, ["max"] = 400, ["startAngle"] = 0, ["endAngle"] = 400 },
        };
        return Finish(name, "737NG radio magnetic indicator: compass card, single needle to VOR 1, double needle to VOR 2, orange flags while a VOR has no signal.", values, scales, layers, font);
    }

    /// <summary>
    /// The 737NG autobrake and flaps corner, drawn after the PMDG 737-800's: AUTO BRAKE selector (RTO, OFF, 1, 2,
    /// 3, MAX) with its DISARM light and ANTISKID label, the flap position dial (UP 1 2 5 10 15 25 30 40 on its
    /// uneven scale) and the LE FLAPS TRANSIT / EXT lights. The selector and the lights read the PMDG's own data
    /// (PMDG: values: the PMDG doesn't fill the sim's AUTO BRAKE SWITCH CB), so this one is for FIPinator Live with
    /// the PMDG's data sharing on; <see cref="Flaps"/> is the version without them, for SPAD.neXt. The flap needle
    /// follows the flaps as they travel (see <see cref="FlapsPanel"/>).
    /// </summary>
    public static string AutobrakeFlaps(string name, string font = "BarlowCondensed-Medium.ttf") => FlapsPanel(name, autobrake: true, font);

    /// <summary>
    /// The 737NG flap position dial and LE FLAPS lights on their own, from the sim's standard values only, so it also
    /// works in SPAD.neXt. The needle follows the flaps as they travel; LE FLAPS EXT lights once the flaps are out
    /// (TRANSIT stays dark: only the PMDG's own data knows it).
    /// </summary>
    public static string Flaps(string name, string font = "BarlowCondensed-Medium.ttf") => FlapsPanel(name, autobrake: false, font);

    /// <summary>
    /// The flap needle reads the sim's real trailing-edge flap angle (TRAILING EDGE FLAPS LEFT ANGLE), which travels
    /// smoothly, through a table from the PMDG 737-800's flight model (its flaps-position angles: 0, 8.888, 18.333, 25,
    /// 31.111, 33.333, 35, 36.666, 40 degrees for UP, 1, 2, 5, 10, 15, 25, 30, 40). Checked against a recording
    /// 2026-11-12: the angle moves with the PMDG's own flap needle, a little behind it.
    /// </summary>
    private static string FlapsPanel(string name, bool autobrake, string font)
    {
        var layers = new JsonArray { Box(0, 0, 320, 240, PanelGrey) };
        double Rad(double deg) => deg * Math.PI / 180;
        var values = new JsonObject
        {
            ["flaps"] = new JsonObject { ["sim"] = "TRAILING EDGE FLAPS LEFT ANGLE", ["unit"] = "degrees", ["default"] = 25 },
        };
        JsonObject Lit(string hide, double x, string line1, string line2, string fill, string stroke, string ink) => new()
        {
            ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = hide, ["scale"] = "px" },
            ["layers"] = new JsonArray(Box(x, 204, 78, 30, fill, stroke, 3), T(line1, x + 39, 217, 10, ink), T(line2, x + 39, 229, 10, ink)),
        };
        const string green = "#7CFF94", greenFill = "#0D2A14", greenLine = "#43E86A", amber = "#FFB24A", amberFill = "#3A2208", amberLine = "#F08A24";

        (string Label, double Angle)[] positions = [("RTO", -125), ("OFF", -90), ("1", -42), ("2", 0), ("3", 42), ("MAX", 90)];
        if (autobrake)
        {
            layers.Add(Box(4, 4, 146, 192, "#474B4F", "#3A3E42", 4, 1));
            layers.Add(T("AUTO BRAKE", 77, 20, 15));
            layers.Add(Box(30, 28, 94, 28, "#1A1B1C", "#0C0C0C", 3));
            layers.Add(T("AUTOBRAKE", 77, 40, 8, "#3E4042"));
            layers.Add(T("DISARM", 77, 51, 8, "#3E4042"));
            layers.Add(new JsonObject
            {
                ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = "disarm_hide", ["scale"] = "px" },
                ["layers"] = new JsonArray(Box(30, 28, 94, 28, amberFill, amberLine, 3), T("AUTOBRAKE", 77, 40, 9, amber), T("DISARM", 77, 52, 9, amber)),
            });
            const double kx = 77, ky = 124;
            foreach (var (label, angle) in positions)
            {
                var (sn, cs) = (Math.Sin(Rad(angle)), -Math.Cos(Rad(angle)));
                layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "ab_dial", ["at"] = new JsonArray(angle < 0 ? angle + 360 : angle), ["radius"] = 44, ["length"] = 10, ["width"] = 2.5, ["color"] = Cream, ["center"] = Pt(kx, ky) });
                layers.Add(T(label, kx + 56 * sn, ky + 56 * cs + 5, label.Length > 1 ? 13 : 14));
            }
            layers.Add(new JsonObject { ["type"] = "circle", ["center"] = Pt(kx, ky), ["r"] = 30, ["fillTop"] = "#DAD8D0", ["fillBottom"] = "#8C8A84", ["stroke"] = "#55544F" });
            layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "ab", ["value"] = "ab", ["length"] = 34, ["width"] = 6, ["tail"] = 0, ["color"] = "#2A2A28", ["center"] = Pt(kx, ky) });
            layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "ab", ["value"] = "ab", ["length"] = 32, ["width"] = 3, ["tail"] = 0, ["color"] = "#F6F2E4", ["center"] = Pt(kx, ky) });
            layers.Add(T("ANTISKID", 77, 182, 14));
            layers.Add(Box(10, 204, 120, 30, "#1A1B1C", "#0C0C0C", 3));
            layers.Add(T("ANTI-SKID", 70, 217, 9, "#3E4042"));
            layers.Add(T("INOP", 70, 228, 9, "#3E4042"));
            values["ab"] = new JsonObject { ["sim"] = "PMDG:MAIN_AutobrakeSelector", ["unit"] = "number", ["default"] = 1 };
            values["disarm"] = new JsonObject { ["sim"] = "PMDG:MAIN_annunAUTO_BRAKE_DISARM", ["unit"] = "number", ["default"] = 0 };
            values["disarm_hide"] = new JsonObject { ["expr"] = "choose(disarm - 0.5, 0, 400)" };
        }

        // Flap position dial: beside the autobrake, or in the middle on its own.
        var fx = autobrake ? 236.0 : 160.0;
        const double fy = 100, fr = 70;
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = Pt(fx, fy), ["r"] = fr + 10, ["fillTop"] = "#5E6266", ["fillBottom"] = "#232527", ["stroke"] = "#151617" });
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = Pt(fx, fy), ["r"] = fr + 2, ["fill"] = "#070707" });
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = Pt(fx, fy), ["r"] = 24, ["fill"] = "#141414" });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "flaps", ["at"] = new JsonArray([.. SimFlapAngles.Select(x => (JsonNode)x)]), ["radius"] = fr - 1, ["length"] = 7, ["width"] = 8, ["color"] = Cream, ["center"] = Pt(fx, fy) });
        for (var i = 0; i < Detents.Length; i++)
        {
            var a = Rad(FlapAngles[i]);
            layers.Add(T(Detents[i], fx + 52 * Math.Sin(a), fy - 52 * Math.Cos(a) + 6, 16));
        }
        layers.Add(T("FLAPS", fx - 30, fy + 42, 10));
        layers.Add(new JsonObject { ["type"] = "needle", ["style"] = "pointer", ["scale"] = "flaps", ["value"] = "flaps", ["length"] = fr - 10, ["width"] = 7, ["tail"] = 0, ["color"] = Cream, ["center"] = Pt(fx, fy) });
        layers.Add(new JsonObject { ["type"] = "screw", ["x"] = 306, ["y"] = 20, ["kind"] = "steel" });

        // LE FLAPS lights: TRANSIT and EXT, dark, with lit copies shown when they're on.
        var lx = autobrake ? 158.0 : 81.0;
        layers.Add(Box(lx, 204, 78, 30, "#1A1B1C", "#0C0C0C", 3));
        layers.Add(T("LE FLAPS", lx + 39, 217, 9, "#3E4042"));
        layers.Add(T("TRANSIT", lx + 39, 228, 9, "#3E4042"));
        layers.Add(Box(lx + 80, 204, 78, 30, "#1A1B1C", "#0C0C0C", 3));
        layers.Add(T("LE FLAPS", lx + 119, 217, 9, "#3E4042"));
        layers.Add(T("EXT", lx + 119, 228, 9, "#3E4042"));
        if (autobrake)
        {
            values["le_transit"] = new JsonObject { ["sim"] = "PMDG:MAIN_annunLE_FLAPS_TRANSIT", ["unit"] = "number", ["default"] = 0 };
            values["le_ext"] = new JsonObject { ["sim"] = "PMDG:MAIN_annunLE_FLAPS_EXT", ["unit"] = "number", ["default"] = 1 };
            values["le_transit_hide"] = new JsonObject { ["expr"] = "choose(le_transit - 0.5, 0, 400)" };
            values["le_ext_hide"] = new JsonObject { ["expr"] = "choose(le_ext - 0.5, 0, 400)" };
            layers.Add(Lit("le_transit_hide", lx, "LE FLAPS", "TRANSIT", amberFill, amberLine, amber));
        }
        else
            values["le_ext_hide"] = new JsonObject { ["expr"] = "choose(flaps - 0.5, 0, 400)" };
        layers.Add(Lit("le_ext_hide", lx + 80, "LE FLAPS", "EXT", greenFill, greenLine, green));

        var scales = new JsonObject
        {
            ["flaps"] = new JsonObject { ["points"] = new JsonArray([.. SimFlapAngles.Select((x, i) => (JsonNode)new JsonArray(x, FlapAngles[i]))]) },
            ["px"] = new JsonObject { ["min"] = 0, ["max"] = 400, ["startAngle"] = 0, ["endAngle"] = 400 },
        };
        if (autobrake)
        {
            scales["ab"] = new JsonObject { ["points"] = new JsonArray([.. positions.Select((p, i) => (JsonNode)new JsonArray(i, p.Angle))]) };
            scales["ab_dial"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = 360, ["wrap"] = true };
        }
        return Finish(name, autobrake
            ? "737NG autobrake and flaps (reads the plane's own data, FIPinator Live only): AUTO BRAKE selector RTO-MAX with DISARM light, flap dial UP to 40 following the flaps, LE FLAPS TRANSIT and EXT lights. Needs FIPinator Live, with data sharing switched on in the add-on's options."
            : "737NG flap position dial: UP to 40, the needle following the flaps as they move, LE FLAPS EXT light.", values, scales, layers, font);
    }

    /// <summary>The flap dial's marks, degrees clockwise from 12 o'clock, as on the PMDG 737-800's dial (UP at 9 o'clock, 15 at 3, 40 at 6).</summary>
    private static readonly double[] FlapAngles = [-90, -52, -18, 15, 50, 90, 125, 155, 180];

    /// <summary>The sim's trailing-edge flap angle at each lever setting, from the PMDG 737-800's flight model.</summary>
    private static readonly double[] SimFlapAngles = [0, 8.888, 18.333, 25, 31.111, 33.333, 35, 36.666, 40];
}
