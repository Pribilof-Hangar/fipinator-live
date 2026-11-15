// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fipinator.Core;

/// <summary>
/// The Boeing 727's engine dials (EPR, N1, EGT, N2, fuel flow) in the look of the sim's 727 panel: brown faces lit
/// orange from behind, cream numbers and needles, N1/N2 with a small 0-9 sub-dial that turns once every 10 %. Drawn
/// by us after the owner's cockpit screenshot (2026-11-12); scale angles measured from it. Values: the sim's standard
/// ones (TURB ENG PRESSURE RATIO, TURB ENG N1/N2, ENG EXHAUST GAS TEMPERATURE, ENG FUEL FLOW PPH), which matched the
/// 727's own dials at idle (EPR 1.00, N1 37.7, N2 54.5, EGT 327, FF 1,020 lb/h).
/// Three versions of each: one big dial with three numbered needles, three dials in a row (engines 1-2-3), and one
/// big dial per engine.
/// </summary>
public static class Boeing727Engines
{
    private const string Cream = "#F2E3C4", Dim = "#C9B48E", Green = "#3E9A55", Red = "#C8322B", Amber = "#E0A23A";

    private sealed record Kind(string Key, string Title, string Sim, string Unit, double Sample, string ScaleJson, double MinorEvery, double MajorEvery,
        (double Value, string Text)[] Labels, string[] Words, (double From, double To, string Color)[] Bands, double Divide = 1, bool SubDial = false);

    private static string Lin(double min, double max, double start, double end) =>
        $$"""{"min":{{N(min)}},"max":{{N(max)}},"startAngle":{{N(start)}},"endAngle":{{N(end)}}}""";

    private static readonly Kind[] Kinds =
    [
        new("epr", "EPR", "TURB ENG PRESSURE RATIO", "ratio", 1.6, Lin(1.0, 3.0, 125, 459), 0.02, 0.1,
            [(1.0, "1.0"), (1.2, "1.2"), (1.4, "1.4"), (1.6, "1.6"), (1.8, "1.8"), (2.0, "2.0"), (2.2, "2.2"), (2.4, "2.4"), (2.6, "2.6"), (2.8, "2.8"), (3.0, "3.0")],
            ["PRESS", "RATIO"], []),
        new("n1", "N1", "TURB ENG N1", "percent", 85, Lin(0, 110, 32, 287), 1, 5,
            [(10, "10"), (20, "20"), (30, "30"), (40, "40"), (50, "50"), (60, "60"), (70, "70"), (80, "80"), (90, "90"), (110, "110")],
            ["N1", "PERCENT", "RPM"], [(40, 95, Green), (96, 98, Amber), (99, 104, Red)], SubDial: true),
        new("egt", "EGT", "ENG EXHAUST GAS TEMPERATURE", "celsius", 450, Lin(0, 800, 106, 344), 20, 100,
            [(0, "0"), (100, "1"), (200, "2"), (300, "3"), (400, "4"), (500, "5"), (600, "6"), (700, "7"), (800, "8")],
            ["EXH", "TEMP", "C X 100"], [(300, 600, Green), (600, 620, Amber), (640, 700, Red)]),
        new("n2", "N2", "TURB ENG N2", "percent", 90, Lin(0, 110, 32, 287), 1, 5,
            [(10, "10"), (20, "20"), (30, "30"), (40, "40"), (50, "50"), (60, "60"), (70, "70"), (80, "80"), (90, "90"), (110, "110")],
            ["N2", "PERCENT", "RPM"], [(50, 95, Green), (96, 99, Amber), (100, 104, Red)], SubDial: true),
        new("fuel-flow", "Fuel flow", "ENG FUEL FLOW PPH", "pounds per hour", 3000,
            """{"points":[[0,-55],[0.5,-32],[1,0],[2,47],[3,93],[4,147],[5,200],[6,244],[8,261],[10,277],[12,296]]}""", 0.25, 1,
            [(0.5, "0.5"), (2, "2"), (3, "3"), (4, "4"), (5, "5"), (6, "6"), (8, "8"), (10, "10"), (12, "12")],
            ["FUEL FLOW", "PPH X 1000"], [], Divide: 1000),
    ];

    /// <summary>All 25 gauges as (file name, json): for each kind the big three-needle dial ("epr"), the row ("epr-row") and one per engine ("epr-1").</summary>
    public static IEnumerable<(string File, string Json)> Create(string font = "BarlowCondensed-Medium.ttf")
    {
        foreach (var kind in Kinds)
        {
            yield return (kind.Key, Big(kind, font));
            yield return ($"{kind.Key}-row", Row(kind, font));
            for (var e = 1; e <= 3; e++) yield return ($"{kind.Key}-{e}", Single(kind, e, font));
        }
    }

    private static string Big(Kind k, string font)
    {
        var (layers, values, scales) = Start(k);
        Dial(layers, k, 160, 120, 108, [1, 2, 3], numbered: true, subDial: false, values);
        if (k.Key == "epr") foreach (var e in new[] { 1, 2, 3 }) EprBug(layers, values, e, 160, 120, 108, counter: e == 1); // one counter (engine 1): the knob sets all three together
        var json = Finish($"727 {k.Title}", $"Boeing 727 {k.Title}, all three engines on one dial: needles numbered 1, 2, 3. The look of the sim's 727 panel."
            + (k.Key == "epr" ? " The right knob sets all three EPR bugs together (FSS 727; FIPinator Live)." : ""), values, scales, layers, font);
        return k.Key == "epr" ? WithEprKnob(json, "L:FSS_B727_PCP_EPR_KNOB1,L:FSS_B727_PCP_EPR_KNOB2,L:FSS_B727_PCP_EPR_KNOB3") : json;
    }

    private static string Row(Kind k, string font)
    {
        var (layers, values, scales) = Start(k);
        for (var e = 1; e <= 3; e++)
        {
            var cx = 55 + (e - 1) * 105;
            Dial(layers, k, cx, 112, 50, [e], numbered: false, subDial: k.SubDial, values, small: true);
            if (k.Key == "epr") EprBug(layers, values, e, cx, 112, 50, counter: true);
            layers.Add(T(e.ToString(CultureInfo.InvariantCulture), cx, 186, 16, Cream));
        }
        layers.Add(T(k.Title, 160, 222, 18, Cream));
        var json = Finish($"727 {k.Title} (3 in a row)", $"Boeing 727 {k.Title}: engines 1, 2 and 3 side by side, as on the 727's panel."
            + (k.Key == "epr" ? " The right knob sets all three EPR bugs together (FSS 727; FIPinator Live)." : ""), values, scales, layers, font);
        return k.Key == "epr" ? WithEprKnob(json, "L:FSS_B727_PCP_EPR_KNOB1,L:FSS_B727_PCP_EPR_KNOB2,L:FSS_B727_PCP_EPR_KNOB3") : json;
    }

    private static string Single(Kind k, int engine, string font)
    {
        var (layers, values, scales) = Start(k);
        Dial(layers, k, 160, 120, 110, [engine], numbered: false, subDial: k.SubDial, values);
        layers.Add(k.Key == "epr" ? T($"ENG {engine}", 286, 22, 15, Cream) : T($"ENG {engine}", 286, 226, 15, Cream));
        var description = $"Boeing 727 {k.Title} for engine {engine}, one big dial, the look of the sim's 727 panel.";
        if (k.Key != "epr") return Finish($"727 {k.Title} engine {engine}", description, values, scales, layers, font);
        EprBug(layers, values, engine, 160, 120, 110, counter: true);
        var json = Finish($"727 {k.Title} engine {engine}", description + " The EPR bug and its counter follow the cockpit's; the right knob sets them like the cockpit's EPR knob (FSS 727; FIPinator Live).",
            values, scales, layers, font);
        return WithEprKnob(json, $"L:FSS_B727_PCP_EPR_KNOB{engine}");
    }

    private static (JsonArray Layers, JsonObject Values, JsonObject Scales) Start(Kind k)
    {
        var layers = new JsonArray { Box(0, 0, 320, 240, "#3E4246") };
        foreach (var (x, y) in new[] { (12.0, 12.0), (308.0, 12.0), (12.0, 228.0), (308.0, 228.0) })
            layers.Add(new JsonObject { ["type"] = "screw", ["x"] = x, ["y"] = y, ["kind"] = "steel" });
        var scales = new JsonObject
        {
            ["s"] = JsonNode.Parse(k.ScaleJson),
            ["sub"] = JsonNode.Parse(Lin(0, 10, 0, 360)),
            ["fixed"] = JsonNode.Parse(Lin(0, 360, 0, 360)),
        };
        return (layers, new JsonObject(), scales);
    }

    /// <summary>One dial at (cx, cy), radius r, with a needle per engine in <paramref name="engines"/>.</summary>
    private static void Dial(JsonArray layers, Kind k, double cx, double cy, double r, int[] engines, bool numbered, bool subDial, JsonObject values, bool small = false)
    {
        JsonArray C() => new(cx, cy);
        double S(double x) => Math.Round(x * r / 100, 2);
        var epr = k.Key == "epr";
        if (epr)
        {
            // The EPR gauge sits in a square grey case, its SET knob at the bottom right.
            layers.Add(new JsonObject { ["type"] = "rect", ["x"] = Math.Round(cx - S(small ? 104 : 112), 2), ["y"] = Math.Round(cy - S(110), 2), ["width"] = S(small ? 208 : 224), ["height"] = S(220), ["radius"] = S(14), ["fill"] = "#393B3D", ["stroke"] = "#1C1D1E", ["strokeWidth"] = 1.5 });
        }
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = C(), ["r"] = S(108), ["fillTop"] = "#2C2D2F", ["fillBottom"] = "#121314", ["stroke"] = "#0A0A0A", ["strokeWidth"] = 1.5 });
        // Brown faces lit orange from behind (the EPR gauges brighter, as on the panel), darker towards the middle.
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = C(), ["r"] = S(100), ["fillTop"] = epr ? "#C8691F" : "#A4561A", ["fillBottom"] = epr ? "#8C4212" : "#5A2A0C" });
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = C(), ["r"] = S(epr ? 52 : 76), ["fillTop"] = epr ? "#6A3210" : "#4E260C", ["fillBottom"] = epr ? "#3A1A08" : "#241005" });
        foreach (var (from, to, color) in k.Bands)
            layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = "s", ["from"] = from, ["to"] = to, ["radius"] = S(84), ["width"] = S(5), ["color"] = color, ["center"] = C() });
        var low = k.Key == "epr" ? 1.0 : 0;
        var high = k.Key switch { "epr" => 3.0, "egt" => 800, "fuel-flow" => 12, _ => 110 };
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "s", ["every"] = k.MinorEvery * (small ? 2 : 1), ["from"] = low, ["to"] = high, ["radius"] = S(98), ["length"] = S(7), ["width"] = small ? 1 : 1.3, ["color"] = Dim, ["center"] = C() });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "s", ["every"] = k.MajorEvery, ["from"] = low, ["to"] = high, ["radius"] = S(98), ["length"] = S(13), ["width"] = small ? 1.6 : 2.4, ["color"] = Cream, ["center"] = C() });
        var texts = new JsonObject();
        foreach (var (v, t) in k.Labels) texts[N(v)] = t;
        layers.Add(new JsonObject
        {
            ["type"] = "labels", ["scale"] = "s", ["at"] = new JsonArray([.. k.Labels.Where((_, i) => !small || i % 2 == 0).Select(l => (JsonNode)l.Value)]), ["texts"] = texts,
            ["radius"] = S(small ? 70 : 74), ["size"] = S(small ? 26 : 21), ["color"] = Cream, ["center"] = C(),
        });
        // Words on the face.
        if (!small)
        {
            var words = k.Words;
            if (k.Key is "n1" or "n2")
            {
                layers.Add(T(words[0], cx + S(30), cy + S(14), S(34), Cream));
                layers.Add(T(words[1], cx - S(10), cy + S(44), S(15), Cream));
                layers.Add(T(words[2], cx - S(10), cy + S(58), S(15), Cream));
            }
            else if (k.Key == "egt")
            {
                layers.Add(T(words[0], cx, cy - S(24), S(20), Cream));
                layers.Add(T(words[1], cx, cy + S(42), S(20), Cream));
                layers.Add(T(words[2], cx + S(56), cy + S(8), S(12), Cream));
            }
            else
            {
                layers.Add(T(words[0], cx, k.Key == "epr" ? cy - S(40) : cy - S(18), S(k.Key == "epr" ? 21 : 18), Cream));
                layers.Add(T(words[1], cx, k.Key == "epr" ? cy - S(19) : cy + S(40), S(k.Key == "epr" ? 21 : 12), Cream));
            }
        }
        else if (k.Key is "n1" or "n2")
            layers.Add(T(k.Words[0], cx + S(34), cy + S(20), S(36), Cream));

        // N1/N2 sub-dial: the last digit of the percentage, once round every 10 %.
        if (subDial)
        {
            var (sx, sy, sr) = (cx - S(8), cy - S(52), S(24));
            JsonArray SC() => new(sx, sy);
            layers.Add(new JsonObject { ["type"] = "circle", ["center"] = SC(), ["r"] = sr, ["fill"] = "#141414" });
            layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "sub", ["every"] = 1, ["from"] = 0, ["to"] = 9, ["radius"] = sr - 1, ["length"] = Math.Round(sr * 0.22, 2), ["width"] = 1, ["color"] = Cream, ["center"] = SC() });
            if (!small)
                layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = "sub", ["every"] = 1, ["from"] = 0, ["to"] = 9, ["radius"] = Math.Round(sr * 0.62, 2), ["size"] = Math.Round(sr * 0.36, 1), ["color"] = Cream, ["center"] = SC() });
            var e = engines[0];
            values[$"{Id(k)}{e}_digit"] = new JsonObject { ["expr"] = $"{Id(k)}{e} % 10" };
            layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "sub", ["value"] = $"{Id(k)}{e}_digit", ["length"] = Math.Round(sr * 0.9, 2), ["width"] = Math.Max(1.6, S(3.4)), ["tail"] = 0, ["color"] = Cream, ["shadow"] = false, ["center"] = SC() });
        }

        foreach (var e in engines)
        {
            var v = $"{Id(k)}{e}";
            if (k.Divide != 1)
            {
                values[$"{v}_raw"] = new JsonObject { ["sim"] = ClassicPack.Engine(k.Sim, e), ["unit"] = k.Unit, ["default"] = k.Sample * (1 + (e - 1) * 0.04) };
                values[v] = new JsonObject { ["expr"] = $"{v}_raw / {N(k.Divide)}" };
            }
            else
                values[v] = new JsonObject { ["sim"] = ClassicPack.Engine(k.Sim, e), ["unit"] = k.Unit, ["default"] = Math.Round(k.Sample * (1 + (e - 1) * 0.04), 2) };
            var needle = new JsonObject
            {
                ["type"] = "needle", ["scale"] = "s", ["value"] = v, ["length"] = S(numbered ? 92 - (e - 1) * 9 : 96), ["width"] = S(small ? 6 : 5.5),
                ["tail"] = 0, ["color"] = "#F4EEE0", ["shadow"] = false, ["center"] = C(),
            };
            if (numbered) needle["tag"] = e.ToString(CultureInfo.InvariantCulture);
            layers.Add(needle);
        }
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = C(), ["r"] = S(13), ["fill"] = "#0E0E0E" });
    }

    /// <summary>
    /// The right knob sets the EPR bug(s): the FSS 727's EPR knob is a value the plane counts one change at a time
    /// (see docs/sim-interface-notes.md); several values (comma-separated) move together. The left knob stays free.
    /// </summary>
    private static string WithEprKnob(string json, string knobValues)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node["controls"] = new JsonObject
        {
            ["rightKnobClockwise"] = new JsonObject { ["set"] = knobValues, ["add"] = 1 },
            ["rightKnobAnticlockwise"] = new JsonObject { ["set"] = knobValues, ["add"] = -1 },
        };
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// The EPR bug for an engine: a marker on the rim at the bug's EPR (FSS 727: L:FSS_B727_PCP_EPR_BUG1-3, EPR = 1 + 2
    /// x value; 0.5 with the cockpit counter at "200"), and optionally the counter (EPR x 100) under the middle.
    /// </summary>
    private static void EprBug(JsonArray layers, JsonObject values, int e, double cx, double cy, double r, bool counter)
    {
        double S(double x) => Math.Round(x * r / 100, 2);
        values[$"eprbugraw{e}"] = new JsonObject { ["sim"] = $"L:FSS_B727_PCP_EPR_BUG{e}", ["unit"] = "number", ["default"] = 0.5 };
        values[$"eprbug{e}"] = new JsonObject { ["expr"] = $"1 + 2 * eprbugraw{e}" };
        layers.Add(new JsonObject
        {
            ["type"] = "group", ["rotate"] = new JsonObject { ["value"] = $"eprbug{e}", ["scale"] = "s" }, ["center"] = new JsonArray(cx, cy),
            ["layers"] = new JsonArray(new JsonObject
            {
                ["type"] = "ticks", ["scale"] = "fixed", ["at"] = new JsonArray(0), ["radius"] = S(101), ["length"] = S(12), ["width"] = S(10),
                ["color"] = "#E8E2D4", ["shape"] = "triangle", ["center"] = new JsonArray(cx, cy),
            }),
        });
        if (!counter) return;
        // The counter: EPR x 100 in three drum windows (like the cockpit's "226"), each digit its own value.
        values[$"eprcounter{e}"] = new JsonObject { ["expr"] = $"eprbug{e} * 100 + 0.5 - (eprbug{e} * 100 + 0.5) % 1" };
        values[$"eprdigit{e}a"] = new JsonObject { ["expr"] = $"(eprcounter{e} - eprcounter{e} % 100) / 100" };
        values[$"eprdigit{e}b"] = new JsonObject { ["expr"] = $"(eprcounter{e} % 100 - eprcounter{e} % 10) / 10" };
        values[$"eprdigit{e}c"] = new JsonObject { ["expr"] = $"eprcounter{e} % 10" };
        var (w, h, top) = (S(15), S(24), cy + S(26));
        foreach (var (d, i) in new[] { ("a", -1), ("b", 0), ("c", 1) })
        {
            var x = cx - S(2) + i * S(17);
            layers.Add(new JsonObject { ["type"] = "rect", ["x"] = Math.Round(x - w / 2, 2), ["y"] = Math.Round(top, 2), ["width"] = w, ["height"] = h, ["fill"] = "#1A0D05" });
            layers.Add(new JsonObject { ["type"] = "text", ["value"] = $"eprdigit{e}{d}", ["format"] = "0", ["x"] = Math.Round(x, 2), ["y"] = Math.Round(top + h * 0.86, 2), ["size"] = Math.Round(h * 0.95, 1), ["color"] = "#F6E9CC" });
        }
        // The SET knob (drawn; the FIP's right knob does its job).
        layers.Add(new JsonObject { ["type"] = "circle", ["center"] = new JsonArray(Math.Round(cx + S(92), 2), Math.Round(cy + S(90), 2)), ["r"] = S(14), ["fillTop"] = "#2A2A2A", ["fillBottom"] = "#0A0A0A", ["stroke"] = "#4A4A4A", ["strokeWidth"] = 1 });
        layers.Add(new JsonObject { ["type"] = "text", ["text"] = "SET", ["x"] = Math.Round(cx + S(92), 2), ["y"] = Math.Round(cy + S(94), 2), ["size"] = S(10), ["color"] = "#E8E2D4" });
    }

    private static JsonObject T(string text, double x, double y, double size, string color) =>
        new() { ["type"] = "text", ["text"] = text, ["x"] = Math.Round(x, 2), ["y"] = Math.Round(y, 2), ["size"] = Math.Round(size, 1), ["color"] = color };

    private static JsonObject Box(double x, double y, double w, double h, string fill) =>
        new() { ["type"] = "rect", ["x"] = x, ["y"] = y, ["width"] = w, ["height"] = h, ["fill"] = fill };

    /// <summary>The kind's name for value names (no dash: "fuel-flow" would read as a subtraction).</summary>
    private static string Id(Kind k) => k.Key.Replace("-", "", StringComparison.Ordinal);

    private static string N(double v) => v.ToString(CultureInfo.InvariantCulture);

    private static string Finish(string name, string description, JsonObject values, JsonObject scales, JsonArray layers, string font) => new JsonObject
    {
        ["format"] = 1,
        ["name"] = name,
        ["author"] = "Made with FIPinator 3000",
        ["description"] = description,
        ["canvas"] = new JsonObject { ["width"] = 320, ["height"] = 240, ["background"] = "#3E4246" },
        ["center"] = new JsonArray(160, 120),
        ["font"] = font,
        ["values"] = values,
        ["scales"] = scales,
        ["layers"] = layers,
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
