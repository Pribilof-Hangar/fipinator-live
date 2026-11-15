// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fipinator.Core;

/// <summary>
/// Cessna 404 Titan (MSFS 2024, classic gauges) engine instruments, drawn after the owner's cockpit screenshots
/// (2026-11-12): manifold pressure, RPM and fuel flow with L and R needles, the twin edgewise EGT and fuel quantity
/// gauges, and an oil/CHT gauge per engine. Values: the sim's standard ones, which matched the Titan's own gauges
/// (MP 16.0, RPM 1,291 at a little power). Scale angles are measured from the screenshots. Still to confirm: the EGT
/// gauge's scale (taken as EGT / 100 divisions: 240 °F showed about 2.4) and the fuel gauge against the cockpit.
/// </summary>
public static class Cessna404Panel
{
    private const string Ink = "#EDE9DF", Blue = "#6FA6DA", Green = "#3F9A52", Yellow = "#D8C23A", Red = "#D0302A", Face = "#0D0D0D";

    private static JsonArray Pt(double x, double y) => new(x, y);
    private static JsonArray Row(double v, double a) => new(v, a);
    private static string N(double v) => v.ToString(CultureInfo.InvariantCulture);

    private static JsonObject T(string text, double x, double y, double size, string color = Ink, string? align = null)
    {
        var o = new JsonObject { ["type"] = "text", ["text"] = text, ["x"] = Math.Round(x, 2), ["y"] = Math.Round(y, 2), ["size"] = size, ["color"] = color };
        if (align is not null) o["align"] = align;
        return o;
    }

    private static JsonObject Arc(string scale, double from, double to, double radius, double width, string color, double cx = 160, double cy = 120) =>
        new() { ["type"] = "arc", ["scale"] = scale, ["from"] = from, ["to"] = to, ["radius"] = radius, ["width"] = width, ["color"] = color, ["center"] = Pt(cx, cy) };

    private static JsonObject Ticks(string scale, double[] at, double radius, double length, double width, string color = Ink, double cx = 160, double cy = 120) =>
        new() { ["type"] = "ticks", ["scale"] = scale, ["at"] = new JsonArray([.. at.Select(a => (JsonNode)a)]), ["radius"] = radius, ["length"] = length, ["width"] = width, ["color"] = color, ["center"] = Pt(cx, cy) };

    private static JsonObject Labels(string scale, (double Value, string Text)[] labels, double radius, double size, double cx = 160, double cy = 120, string color = Ink)
    {
        var texts = new JsonObject();
        foreach (var (v, t) in labels) texts[N(v)] = t;
        return new JsonObject { ["type"] = "labels", ["scale"] = scale, ["at"] = new JsonArray([.. labels.Select(l => (JsonNode)l.Value)]), ["texts"] = texts, ["radius"] = radius, ["size"] = size, ["color"] = color, ["center"] = Pt(cx, cy) };
    }

    private static IEnumerable<double> Steps(double from, double to, double every)
    {
        for (var v = from; v <= to + 1e-9; v += every) yield return Math.Round(v, 4);
    }

    /// <summary>The Titan's panel around a round black dial (radius 108 at the FIP's middle) with four screws.</summary>
    private static JsonArray Body()
    {
        var layers = new JsonArray
        {
            new JsonObject { ["type"] = "rect", ["x"] = 0, ["y"] = 0, ["width"] = 320, ["height"] = 240, ["fill"] = "#3D4145" },
            new JsonObject { ["type"] = "circle", ["center"] = Pt(160, 120), ["r"] = 117, ["fillTop"] = "#26282A", ["fillBottom"] = "#0C0C0D", ["stroke"] = "#08090A", ["strokeWidth"] = 2 },
            new JsonObject { ["type"] = "circle", ["center"] = Pt(160, 120), ["r"] = 108, ["fill"] = Face },
        };
        foreach (var (x, y) in new[] { (36.0, 16.0), (284.0, 16.0), (36.0, 224.0), (284.0, 224.0) })
            layers.Add(new JsonObject { ["type"] = "screw", ["x"] = x, ["y"] = y, ["kind"] = "steel" });
        return layers;
    }

    /// <summary>Two white needles, the right engine's on top, each with its letter (L, R) on it.</summary>
    private static void Needles(JsonArray layers, string scale, string left, string right, double length)
    {
        foreach (var (v, tag) in new[] { (left, "L"), (right, "R") })
            layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = scale, ["value"] = v, ["length"] = length, ["width"] = 6, ["tail"] = 16, ["color"] = "#ECECE8", ["tailColor"] = "#1A1A1A", ["shadow"] = true, ["tag"] = tag, ["center"] = Pt(160, 120) });
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = Pt(160, 120), ["r"] = 9, ["fill"] = "#141414" });
    }

    private static JsonObject Engines(string key, string sim, string unit, double sample) => new()
    {
        [$"{key}1"] = new JsonObject { ["sim"] = $"{sim}:1", ["unit"] = unit, ["default"] = sample },
        [$"{key}2"] = new JsonObject { ["sim"] = $"{sim}:2", ["unit"] = unit, ["default"] = Math.Round(sample * 1.04, 2) },
    };

    private static string Finish(string name, string description, JsonObject values, JsonObject scales, JsonArray layers, string font) => new JsonObject
    {
        ["format"] = 1,
        ["name"] = name,
        ["author"] = "Made with FIPinator 3000",
        ["description"] = description,
        ["canvas"] = new JsonObject { ["width"] = 320, ["height"] = 240, ["background"] = "#3D4145" },
        ["center"] = new JsonArray(160, 120),
        ["font"] = font,
        ["values"] = values,
        ["scales"] = scales,
        ["layers"] = layers,
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>All the Titan's engine gauges as (file name, json).</summary>
    public static IEnumerable<(string File, string Json)> Create(string font = "BarlowCondensed-Medium.ttf")
    {
        yield return ("manifold-pressure", ManifoldPressure(font));
        yield return ("rpm", Rpm(font));
        yield return ("fuel-flow", FuelFlow(font));
        yield return ("egt", Egt(font));
        yield return ("oil-cht-1", OilCht(1, font));
        yield return ("oil-cht-2", OilCht(2, font));
        yield return ("fuel", Fuel(font));
    }

    /// <summary>Manifold pressure 10-40 inHg (marks from about 8), green arc, red line, the blue max-MP-by-altitude card inside.</summary>
    public static string ManifoldPressure(string font = "BarlowCondensed-Medium.ttf")
    {
        var layers = Body();
        layers.Add(Arc("mp", 17, 35.5, 100, 6, Green));
        layers.Add(Ticks("mp", [.. Steps(8, 41, 1)], 103, 8, 1.6));
        layers.Add(Ticks("mp", [10, 15, 20, 25, 30, 35, 40], 103, 15, 3.2));
        layers.Add(Ticks("mp", [39.6], 104, 16, 3.5, Red));
        layers.Add(Labels("mp", [(10, "10"), (15, "15"), (20, "20"), (25, "25"), (30, "30"), (35, "35"), (40, "40")], 76, 22));
        // The blue card: the most MP allowed at each altitude (16-30 thousand ft), printed on the dial.
        layers.Add(Arc("fixed", 52, 189, 52, 18, Blue));
        layers.Add(Labels("fixed", [(189, "16"), (169, "18"), (150, "20"), (130, "22"), (110, "24"), (90, "26"), (71, "28"), (52, "30")], 52, 10, color: "#0D1A28"));
        layers.Add(T("SL", 150, 214, 9));
        layers.Add(T("MANIFOLD", 103, 106, 15));
        layers.Add(T("IN HG", 103, 124, 15));
        layers.Add(T("MAX MP", 98, 176, 9, Blue));
        layers.Add(T("FT X 1000", 98, 188, 8, Blue));
        Needles(layers, "mp", "mp1", "mp2", 96);
        var scales = new JsonObject
        {
            ["mp"] = new JsonObject { ["points"] = new JsonArray(Row(8, -62), Row(10, -45), Row(15, 0), Row(20, 47), Row(25, 81), Row(30, 124), Row(35, 155), Row(40, 194), Row(41, 202)) },
            ["fixed"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = 360 },
        };
        return Finish("Titan Manifold pressure", "Cessna 404 Titan manifold pressure, L and R needles, green arc, red line, the max-MP-by-altitude card. The look of the Titan's panel.",
            Engines("mp", "RECIP ENG MANIFOLD PRESSURE", "inHg", 24), scales, layers, font);
    }

    /// <summary>RPM x100, 0-2,300, green and yellow arcs, red line, the prop-sync disc and its note.</summary>
    public static string Rpm(string font = "BarlowCondensed-Medium.ttf")
    {
        var layers = Body();
        layers.Add(Arc("rpm", 1650, 2000, 100, 6, Green));
        layers.Add(Arc("rpm", 2000, 2200, 100, 6, Yellow));
        layers.Add(Ticks("rpm", [.. Steps(0, 2300, 100)], 103, 8, 1.6));
        layers.Add(Ticks("rpm", [0, 500, 1000, 1500, 2000], 103, 16, 3.4));
        layers.Add(Ticks("rpm", [2235], 104, 16, 3.5, Red));
        layers.Add(Labels("rpm", [(0, "0"), (500, "5"), (1000, "10"), (1500, "15"), (2000, "20")], 76, 28));
        layers.Add(T("RPM", 160, 92, 20));
        layers.Add(T("X100", 158, 110, 11, Blue));
        layers.Add(T("AVOID CONTINUOUS", 108, 178, 8));
        layers.Add(T("OPERATION IN", 108, 188, 8));
        layers.Add(T("YELLOW ARC", 108, 198, 8));
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = Pt(160, 186), ["r"] = 9, ["fill"] = "#1E1E1E", ["stroke"] = "#555555" });
        layers.Add(T("FAST", 160, 206, 8, Blue));
        layers.Add(T("RIGHT ENG", 160, 215, 8, Blue));
        Needles(layers, "rpm", "rpm1", "rpm2", 96);
        var scales = new JsonObject { ["rpm"] = new JsonObject { ["points"] = new JsonArray(Row(0, -112), Row(500, -56), Row(1000, 1), Row(1500, 58), Row(2000, 119), Row(2300, 155)) } };
        return Finish("Titan RPM", "Cessna 404 Titan RPM x100, L and R needles, green and yellow arcs, red line, the prop-sync disc. The look of the Titan's panel.",
            Engines("rpm", "GENERAL ENG RPM", "rpm", 1900), scales, layers, font);
    }

    /// <summary>Fuel flow 0-250+ lb/hr on its uneven scale, green arc, the red pressure limits, the cruise % power marks and blue climb card.</summary>
    public static string FuelFlow(string font = "BarlowCondensed-Medium.ttf")
    {
        var layers = Body();
        layers.Add(Arc("ff", 8, 268, 100, 6, Green));
        layers.Add(Ticks("ff", [.. Steps(0, 100, 10).Concat(Steps(110, 270, 10))], 103, 8, 1.6));
        layers.Add(Ticks("ff", [50, 100, 150, 200, 250], 103, 15, 3.2));
        layers.Add(Ticks("ff", [6, 270], 104, 16, 3.5, Red));
        layers.Add(Labels("ff", [(50, "50"), (100, "100"), (150, "150"), (200, "200"), (250, "250")], 78, 20));
        // Cruise % power marks (55, 65, 77.5 %) and the blue take-off and climb card (16-24 thousand ft), printed on the dial.
        layers.Add(Arc("fixed", 8, 34, 66, 9, Green));
        layers.Add(Labels("fixed", [(10, "55"), (21, "65"), (33, "77.5")], 76, 8, color: Ink));
        layers.Add(Arc("fixed", 44, 168, 52, 16, Blue));
        layers.Add(Labels("fixed", [(48, "24"), (78, "22"), (108, "20"), (138, "18"), (166, "16")], 52, 9, color: "#0D1A28"));
        layers.Add(Arc("fixed", 168, 196, 52, 16, "#E6DDC0"));
        layers.Add(T("FUEL", 82, 108, 14));
        layers.Add(T("FLOW", 82, 124, 14));
        layers.Add(T("LBS/HR", 84, 140, 14));
        layers.Add(T("CRUISE", 140, 92, 10, Green));
        layers.Add(T("% POWER", 140, 103, 10, Green));
        layers.Add(T("3.6 PSI", 86, 64, 10, Blue));
        layers.Add(T("19.2 PSI", 90, 200, 10, Blue));
        Needles(layers, "ff", "ff1", "ff2", 96);
        var scales = new JsonObject
        {
            ["ff"] = new JsonObject { ["points"] = new JsonArray(Row(0, -48), Row(50, -20), Row(100, 3), Row(150, 43), Row(200, 100), Row(250, 174), Row(270, 200)) },
            ["fixed"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = 360 },
        };
        return Finish("Titan Fuel flow", "Cessna 404 Titan fuel flow in lb/hr on its uneven scale, L and R needles, green arc, red pressure limits, cruise % power marks. The look of the Titan's panel.",
            Engines("ff", "ENG FUEL FLOW PPH", "pounds per hour", 90), scales, layers, font);
    }

    /// <summary>
    /// An edgewise pair: two curved scales back to back (L bulging right, R bulging left), each read by a small pointer
    /// riding up and down it. Drawn as a pointer turning round a pivot off to the side, at the scale's radius.
    /// </summary>
    private static void EdgePair(JsonArray layers, JsonObject scales, string key, double[] minor, double[] major, (double Value, string Text)[] labels,
        string left, string right, double gap, double radius, double top, double bottom)
    {
        foreach (var (side, value) in new[] { (-1, left), (1, right) })
        {
            // Pivot off to the side (left scale: pivot far left, the scale bulging towards the middle); the scale's middle
            // sits `gap` from the FIP's centre line.
            var pivotX = 160 + side * (gap + radius);
            var s = side < 0 ? $"{key}L" : $"{key}R";
            // Values run bottom to top: on the left the angle falls from about 4 o'clock up to 2 o'clock, mirrored on the right.
            var (aLow, aHigh) = side < 0 ? (90 + bottom, 90 - top) : (-90 - bottom, -90 + top);
            scales[s] = new JsonObject { ["min"] = minor.First(), ["max"] = minor.Last(), ["startAngle"] = aLow, ["endAngle"] = aHigh };
            layers.Add(Arc(s, minor.First(), minor.Last(), radius, 2.4, Ink, pivotX, 120));
            layers.Add(Ticks(s, minor, radius + (side < 0 ? 0 : 6), 6, 1.6, Ink, pivotX, 120));
            layers.Add(Ticks(s, major, radius + (side < 0 ? 0 : 11), 11, 2.6, Ink, pivotX, 120));
            layers.Add(Labels(s, labels, radius - 16, 15, pivotX, 120));
            layers.Add(new JsonObject
            {
                ["type"] = "group", ["rotate"] = new JsonObject { ["value"] = value, ["scale"] = s }, ["center"] = Pt(pivotX, 120),
                // The pointer: a short bar inside the scale with its tip at the scale, drawn at 12 o'clock and turned to the value.
                ["layers"] = new JsonArray(
                    new JsonObject { ["type"] = "rect", ["x"] = Math.Round(pivotX - 2.5, 2), ["y"] = Math.Round(120 - radius + 9, 2), ["width"] = 5, ["height"] = 24, ["fill"] = "#ECECE8" },
                    new JsonObject
                    {
                        ["type"] = "ticks", ["scale"] = "zero", ["at"] = new JsonArray(0), ["radius"] = radius - 1, ["length"] = 10, ["width"] = 9,
                        ["color"] = "#ECECE8", ["shape"] = "triangle", ["center"] = Pt(pivotX, 120),
                    }),
            });
        }
        scales["zero"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = 360 };
    }

    /// <summary>EGT: the edgewise pair, 0-5 divisions at 25 °F each, L and R pointers.</summary>
    public static string Egt(string font = "BarlowCondensed-Medium.ttf")
    {
        var layers = Body();
        var scales = new JsonObject();
        EdgePair(layers, scales, "egt", [.. Steps(0, 5, 0.5)], [0, 1, 2, 3, 4, 5], [(0, "0"), (1, "1"), (2, "2"), (3, "3"), (4, "4"), (5, "5")],
            "egt1", "egt2", 12, 82, 44, 44);
        layers.Add(T("EGT", 160, 34, 18));
        layers.Add(T("L", 58, 126, 18, Blue));
        layers.Add(T("R", 262, 126, 18, Blue));
        layers.Add(T("25°F/DIV", 160, 210, 11, Blue));
        var values = new JsonObject
        {
            ["egt1_f"] = new JsonObject { ["sim"] = "ENG EXHAUST GAS TEMPERATURE:1", ["unit"] = "fahrenheit", ["default"] = 300 },
            ["egt2_f"] = new JsonObject { ["sim"] = "ENG EXHAUST GAS TEMPERATURE:2", ["unit"] = "fahrenheit", ["default"] = 280 },
            ["egt1"] = new JsonObject { ["expr"] = "clamp(egt1_f / 100, 0, 5)" },
            ["egt2"] = new JsonObject { ["expr"] = "clamp(egt2_f / 100, 0, 5)" },
        };
        return Finish("Titan EGT", "Cessna 404 Titan EGT: two edgewise scales, 0-5 divisions, L and R pointers (taken as EGT °F / 100; 240 °F read about 2.4 in the cockpit, to confirm).",
            values, scales, layers, font);
    }

    /// <summary>Fuel quantity: the edgewise pair, 0-12 (lb x 100) with the blue gallons marks, L and R pointers.</summary>
    public static string Fuel(string font = "BarlowCondensed-Medium.ttf")
    {
        var layers = Body();
        var scales = new JsonObject();
        EdgePair(layers, scales, "fuel", [.. Steps(0, 12, 0.5)], [0, 2, 4, 6, 8, 10, 12], [(0, "0"), (2, "2"), (4, "4"), (6, "6"), (8, "8"), (10, "10"), (12, "12")],
            "fuel_l", "fuel_r", 12, 82, 48, 48);
        layers.Add(T("FUEL QTY", 160, 34, 18));
        layers.Add(T("LBS×100", 66, 108, 11));
        layers.Add(T("LBS×100", 254, 108, 11));
        layers.Add(T("180", 112, 52, 10, Blue));
        layers.Add(T("180", 208, 52, 10, Blue));
        layers.Add(T("120", 160, 92, 10, Blue));
        layers.Add(T("60", 160, 170, 10, Blue));
        layers.Add(T("GALS", 160, 204, 11, Blue));
        layers.Add(T("STD DAY", 160, 216, 11, Blue));
        var values = new JsonObject
        {
            ["fuel_lb_gal"] = new JsonObject { ["sim"] = "FUEL WEIGHT PER GALLON", ["unit"] = "pounds", ["default"] = 6 },
            ["fuel_l_gal"] = new JsonObject { ["sim"] = "FUEL LEFT QUANTITY", ["unit"] = "gallons", ["default"] = 85 },
            ["fuel_r_gal"] = new JsonObject { ["sim"] = "FUEL RIGHT QUANTITY", ["unit"] = "gallons", ["default"] = 85 },
            ["fuel_l"] = new JsonObject { ["expr"] = "clamp(fuel_l_gal * fuel_lb_gal / 100, 0, 12)" },
            ["fuel_r"] = new JsonObject { ["expr"] = "clamp(fuel_r_gal * fuel_lb_gal / 100, 0, 12)" },
        };
        return Finish("Titan Fuel quantity", "Cessna 404 Titan fuel quantity: two edgewise scales, 0-12 hundred lb per side (gallons marked in blue), L and R pointers.",
            values, scales, layers, font);
    }

    /// <summary>Oil pressure at the top, cylinder head temperature bottom left, oil temperature bottom right, for one engine.</summary>
    public static string OilCht(int engine, string font = "BarlowCondensed-Medium.ttf")
    {
        var layers = Body();
        // Oil pressure: a small dial in the top half.
        const double oy = 112;
        layers.Add(Arc("psi", 26, 63, 74, 6, Green, 160, oy));
        layers.Add(Ticks("psi", [.. Steps(0, 110, 10)], 77, 8, 1.8, Ink, 160, oy));
        layers.Add(Ticks("psi", [0, 100], 77, 13, 3, Ink, 160, oy));
        layers.Add(Ticks("psi", [10, 100], 79, 10, 6, Red, 160, oy));
        layers.Add(Labels("psi", [(0, "0"), (100, "100")], 56, 15, 160, oy));
        layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "psi", ["value"] = "op", ["length"] = 72, ["width"] = 6, ["tail"] = 0, ["color"] = "#ECECE8", ["center"] = Pt(160, oy) });
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = Pt(160, oy), ["r"] = 7, ["fill"] = "#141414" });
        // The plate with the words, and the two temperature dials on either side of it.
        layers.Add(new JsonObject { ["type"] = "rect", ["x"] = 104, ["y"] = 140, ["width"] = 112, ["height"] = 66, ["radius"] = 10, ["fill"] = "#1C1C1C" });
        layers.Add(T("OIL PSI", 160, 156, 12));
        layers.Add(T("CYL", 128, 176, 12));
        layers.Add(T("°F", 128, 190, 12));
        layers.Add(T("OIL", 192, 182, 12));
        layers.Add(T("TEMP", 160, 202, 12));
        foreach (var (side, key, scale) in new[] { (-1, "cht", "chts"), (1, "ot", "ots") })
        {
            var px = 160 + side * 34.0;
            const double py = 178;
            if (side < 0)
            {
                layers.Add(Arc(scale, 200, 460, 70, 5, Green, px, py));
                layers.Add(Ticks(scale, [.. Steps(100, 500, 50)], 72, 7, 1.6, Ink, px, py));
                layers.Add(Ticks(scale, [460], 73, 11, 5, Red, px, py));
                layers.Add(Labels(scale, [(100, "100"), (300, "300"), (500, "500")], 86, 12, px, py));
            }
            else
            {
                layers.Add(Arc(scale, 75, 230, 70, 5, Green, px, py));
                layers.Add(Ticks(scale, [.. Steps(0, 250, 25)], 72, 7, 1.6, Ink, px, py));
                layers.Add(Ticks(scale, [240], 73, 11, 5, Red, px, py));
            }
            layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = scale, ["value"] = key, ["length"] = 64, ["width"] = 4, ["tail"] = 0, ["color"] = "#ECECE8", ["center"] = Pt(px, py) });
        }
        layers.Add(T($"ENG {(engine == 1 ? "L" : "R")}", 284, 120, 14, Blue));
        var values = new JsonObject
        {
            ["op"] = new JsonObject { ["sim"] = $"GENERAL ENG OIL PRESSURE:{engine}", ["unit"] = "psi", ["default"] = 55 },
            ["ot"] = new JsonObject { ["sim"] = $"GENERAL ENG OIL TEMPERATURE:{engine}", ["unit"] = "fahrenheit", ["default"] = 170 },
            ["cht"] = new JsonObject { ["sim"] = $"RECIP ENG CYLINDER HEAD TEMPERATURE:{engine}", ["unit"] = "fahrenheit", ["default"] = 350 },
        };
        var scales = new JsonObject
        {
            ["psi"] = new JsonObject { ["min"] = 0, ["max"] = 110, ["startAngle"] = -80, ["endAngle"] = 70 },
            // Bottom left: 100 °F at the bottom round to 500 at the top (on the left of its pivot); bottom right mirrored.
            ["chts"] = new JsonObject { ["min"] = 100, ["max"] = 500, ["startAngle"] = 236, ["endAngle"] = 312 },
            ["ots"] = new JsonObject { ["min"] = 0, ["max"] = 250, ["startAngle"] = 124, ["endAngle"] = 48 },
        };
        return Finish($"Titan Oil and CHT {(engine == 1 ? "left" : "right")}", $"Cessna 404 Titan oil pressure, cylinder head temperature and oil temperature for the {(engine == 1 ? "left" : "right")} engine, as on the Titan's panel.",
            values, scales, layers, font);
    }
}
