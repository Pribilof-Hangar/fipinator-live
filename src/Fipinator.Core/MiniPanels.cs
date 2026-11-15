// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fipinator.Core;

/// <summary>
/// "Mini panels": several small engine gauges on one FIP screen, drawn for the small size (fewer, bolder
/// numbers, short labels, number readouts), as an extra page next to the big single gauges.
/// </summary>
public static class MiniPanels
{
    private const string White = "#F2F2F2", Amber = "#F5A623", RedInk = "#FF3B30", Cyan = "#7FD6F0", Sweep = "#5E5E5E", EcamGreen = "#3BE36E";
    private static string N(double v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Jet engines at a glance, glass style (like the top of a 737 MAX engine display): a row of N1 dials
    /// and a row of EGT dials (one per engine, with digital boxes that turn red past the red line), then
    /// N2 and fuel flow as numbers. Same sim values and limits as <see cref="JetPack"/>'s pages.
    /// </summary>
    /// <param name="airbus">Airbus (ECAM) style: green needles and numbers, no grey sweep, fuel flow in kg/h.</param>
    public static string JetEngines(JetProfile plane, string font = "BarlowCondensed-Medium.ttf", bool airbus = false)
    {
        var ink = airbus ? EcamGreen : White;
        var layers = new JsonArray();
        var values = new JsonObject();
        var n = plane.Engines;
        const double left = 38;
        var col = (320 - left) / n;
        double X(int e) => Math.Round(left + col * (e - 0.5), 2);

        void Dial(string key, int e, double cy, double r, string sim, string unit, double max, double? caution, double? red, double[] labels, double divideBy, string format, double sample)
        {
            var v = $"{key}{e}";
            var x = X(e);
            values[v] = new JsonObject { ["sim"] = ClassicPack.Engine(sim, e), ["unit"] = unit, ["default"] = Math.Round(sample * (1 + (e - 1) * 0.03), 1) };
            JsonArray C() => new(x, cy);
            var top = caution ?? red ?? max;
            if (!airbus)
                layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = $"{key}s", ["from"] = 0, ["toValue"] = v, ["radius"] = Math.Round(r / 2, 2), ["width"] = r, ["color"] = Sweep, ["center"] = C() });
            layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = $"{key}s", ["from"] = 0, ["to"] = top, ["radius"] = r, ["width"] = 2.5, ["color"] = White, ["center"] = C() });
            if (caution is double c && red is double rr)
                layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = $"{key}s", ["from"] = c, ["to"] = rr, ["radius"] = r, ["width"] = 2.5, ["color"] = Amber, ["center"] = C() });
            if (red is double rl)
                layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = $"{key}s", ["at"] = new JsonArray(rl), ["radius"] = r + 6, ["length"] = 13, ["width"] = 3, ["color"] = RedInk, ["center"] = C() });
            layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = $"{key}s", ["at"] = new JsonArray([.. labels.Select(l => (JsonNode)l)]), ["radius"] = r, ["length"] = 6, ["width"] = 2, ["color"] = White, ["center"] = C() });
            if (r >= 38)
                layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = $"{key}s", ["at"] = new JsonArray([.. labels.Select(l => (JsonNode)l)]), ["divideBy"] = divideBy, ["radius"] = Math.Round(r - 14, 2), ["size"] = 11, ["color"] = White, ["center"] = C() });
            layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = $"{key}s", ["value"] = v, ["length"] = r, ["width"] = airbus ? 3 : 2.4, ["tail"] = 0, ["color"] = ink, ["center"] = C() });
            // Readout box above the dial, right of centre; red once past the red line.
            var (bw, bh) = (Math.Round(r * 1.05, 1), Math.Round(Math.Max(16, r * 0.42), 1));
            // Right of centre like the MAX; centred when 3-4 engines make the columns narrow.
            var (bx, by) = (n >= 3 ? x - bw / 2 : x + 1, cy - r - bh + 5);
            layers.Add(Box(bx, by, bw, bh));
            AddReadout(layers, values, v, format, bx + bw - 3, by + bh * 0.82, Math.Round(bh * 0.84, 1), red, ink);
        }

        // Planes that set thrust by EPR (727, DC-9) get EPR on the top dial and N1 as a number row.
        var epr = plane.HasEpr;
        var rows = epr ? new[] { "N1", "N2", "FF" } : ["N2", "FF"];
        double RowY(int i) => epr ? 168 + i * 24 : 186 + i * 27;
        var (n1Y, n1R, egtY, egtR) = (66.0, 44.0, epr ? 134.0 : 146.0, epr ? 30.0 : 34.0);
        for (var e = 1; e <= n; e++)
        {
            if (epr)
                Dial("epr", e, 66, 44, "TURB ENG PRESSURE RATIO", "ratio", plane.EprMax, null, null, [1.0, 1.5, 2.0, 2.5, 3.0], 1, "0.00", 1.6);
            else
                Dial("n1", e, n1Y, n1R, plane.N1Sim, "percent", 110, null, plane.N1Red > 0 ? plane.N1Red : null, [0, 20, 40, 60, 80, 100], 10, "0.0", 85);
            Dial("egt", e, egtY, egtR, "ENG EXHAUST GAS TEMPERATURE", "celsius", plane.EgtMax, plane.EgtCaution > 0 ? plane.EgtCaution : null, plane.EgtRed > 0 ? plane.EgtRed : null, [0, plane.EgtMax / 2, plane.EgtMax], 100, "0", 650);
            // The rest as numbers.
            var x = X(e);
            values[$"n2{e}"] = new JsonObject { ["sim"] = ClassicPack.Engine(plane.N2Sim, e), ["unit"] = "percent", ["default"] = Math.Round(90 * (1 + (e - 1) * 0.03), 1) };
            values[$"ff{e}_raw"] = new JsonObject { ["sim"] = ClassicPack.Engine("ENG FUEL FLOW PPH", e), ["unit"] = "pounds per hour", ["default"] = 3000 + (e - 1) * 120 };
            values[$"ff{e}"] = new JsonObject { ["expr"] = airbus ? $"ff{e}_raw * 0.45359237" : $"ff{e}_raw * 0.001" };
            if (epr) values[$"n1{e}"] = new JsonObject { ["sim"] = ClassicPack.Engine(plane.N1Sim, e), ["unit"] = "percent", ["default"] = Math.Round(85 * (1 + (e - 1) * 0.03), 1) };
            for (var i = 0; i < rows.Length; i++)
            {
                var y = RowY(i);
                layers.Add(Box(x - 30, y, 60, 22));
                switch (rows[i])
                {
                    case "N1": AddReadout(layers, values, $"n1{e}", "0.0", x + 26, y + 18, 19, plane.N1Red > 0 ? plane.N1Red : null, ink); break;
                    case "N2": AddReadout(layers, values, $"n2{e}", "0.0", x + 26, y + 18, 19, plane.N2Red > 0 ? plane.N2Red : null, ink); break;
                    default: layers.Add(new JsonObject { ["type"] = "text", ["value"] = $"ff{e}", ["format"] = airbus ? "0" : "0.00", ["x"] = x + 26, ["y"] = y + 18, ["size"] = 19, ["color"] = ink, ["align"] = "right" }); break;
                }
            }
            if (n > 1) layers.Add(new JsonObject { ["type"] = "text", ["text"] = N(e), ["x"] = x - 40, ["y"] = 30, ["size"] = 12, ["color"] = Cyan });
        }
        layers.Add(new JsonObject { ["type"] = "text", ["text"] = epr ? "EPR" : "N1", ["x"] = 18, ["y"] = 70, ["size"] = 15, ["color"] = Cyan });
        layers.Add(new JsonObject { ["type"] = "text", ["text"] = "EGT", ["x"] = 18, ["y"] = epr ? 138 : 150, ["size"] = 15, ["color"] = Cyan });
        for (var i = 0; i < rows.Length; i++)
            layers.Add(new JsonObject { ["type"] = "text", ["text"] = airbus && rows[i] == "FF" ? "FF KG/H" : rows[i], ["x"] = 18, ["y"] = RowY(i) + 17, ["size"] = airbus && rows[i] == "FF" ? 10 : 15, ["color"] = Cyan });

        var scales = new JsonObject
        {
            ["n1s"] = new JsonObject { ["min"] = 0, ["max"] = 110, ["startAngle"] = -100, ["endAngle"] = 110 },
            ["eprs"] = new JsonObject { ["min"] = 1.0, ["max"] = Math.Max(plane.EprMax, 1.5), ["startAngle"] = -100, ["endAngle"] = 110 },
            ["egts"] = new JsonObject { ["min"] = 0, ["max"] = plane.EgtMax, ["startAngle"] = -100, ["endAngle"] = 110 },
            ["px"] = new JsonObject { ["min"] = 0, ["max"] = 400, ["startAngle"] = 0, ["endAngle"] = 400 },
        };
        var title = airbus ? "Engines (Airbus style)" : $"{plane.Name} Engines (all in one)";
        return Gauge(title, $"{plane.Name}: N1 and EGT dials and N2 and fuel flow numbers for every engine on one screen{(airbus ? ", Airbus ECAM style, fuel flow in kg/h" : "")}. Limits: {plane.Source}.", airbus ? "#0A0D12" : "#000000", values, scales, layers, font);
    }

    /// <summary>
    /// DC-6 engines at a glance (PMDG DC-6): small RPM, manifold pressure and BMEP dials with four numbered
    /// needles each, and below them a table of exact numbers per engine (RPM, MP, BMEP, fuel flow). Reads the
    /// same PMDG L: values as the big DC-6 engine gauges (their units: RPM / 10, MP x 10, BMEP psi, fuel flow
    /// / 10 lb/h); range marks as on those gauges.
    /// </summary>
    public static string Dc6Engines(string font = "BarlowCondensed-Medium.ttf")
    {
        const string ink = "#EFE6CF", green = "#3F9A52", yellow = "#D9A21E", red = "#D0302A";
        var layers = new JsonArray();
        var values = new JsonObject();
        var scales = new JsonObject();
        string[] rpm = ["L:dc6_337_obj", "L:dc6_338_obj", "L:dc6_339_obj", "L:dc6_340_obj"];
        string[] mp = ["L:dc6_325_obj", "L:dc6_326_obj", "L:dc6_327_obj", "L:dc6_328_obj"];
        string[] bmep = ["L:dc6_317_obj", "L:dc6_318_obj", "L:dc6_319_obj", "L:dc6_320_obj"];
        string[] ff = ["L:dc6_329_obj", "L:dc6_330_obj", "L:dc6_331_obj", "L:dc6_332_obj"];
        for (var e = 0; e < 4; e++)
        {
            values[$"rpm{e + 1}"] = new JsonObject { ["sim"] = rpm[e], ["unit"] = "number", ["default"] = 230 + e * 2 };
            values[$"mp{e + 1}"] = new JsonObject { ["sim"] = mp[e], ["unit"] = "number", ["default"] = 340 + e * 6 };
            values[$"bmep{e + 1}"] = new JsonObject { ["sim"] = bmep[e], ["unit"] = "number", ["default"] = 165 + e * 4 };
            values[$"ff{e + 1}"] = new JsonObject { ["sim"] = ff[e], ["unit"] = "number", ["default"] = 95 + e * 3 };
            values[$"rpm{e + 1}_x"] = new JsonObject { ["expr"] = $"rpm{e + 1} * 10" };
            values[$"mp{e + 1}_x"] = new JsonObject { ["expr"] = $"mp{e + 1} / 10" };
            values[$"ff{e + 1}_x"] = new JsonObject { ["expr"] = $"ff{e + 1} * 10" };
        }

        void Dial(string key, double cx, string title, double min, double max, double[] labels, double divideBy, (double, double, string)[] bands, double[] reds)
        {
            const double cy = 60, r = 47;
            JsonArray C() => new(cx, cy);
            scales[key] = new JsonObject { ["min"] = min, ["max"] = max, ["startAngle"] = -135, ["endAngle"] = 135 };
            layers.Add(new JsonObject { ["type"] = "circle", ["center"] = C(), ["r"] = r + 3, ["fillTop"] = "#3A3B3D", ["fillBottom"] = "#0A0A0A" });
            layers.Add(new JsonObject { ["type"] = "circle", ["center"] = C(), ["r"] = r, ["fill"] = "#0B0B0B" });
            foreach (var (from, to, color) in bands)
                layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = key, ["from"] = from, ["to"] = to, ["radius"] = r - 3, ["width"] = 5, ["color"] = color, ["center"] = C() });
            layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = key, ["at"] = new JsonArray([.. labels.Select(l => (JsonNode)l)]), ["radius"] = r - 1, ["length"] = 9, ["width"] = 2.2, ["color"] = ink, ["center"] = C() });
            if (reds.Length > 0)
                layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = key, ["at"] = new JsonArray([.. reds.Select(l => (JsonNode)l)]), ["radius"] = r, ["length"] = 12, ["width"] = 3.2, ["color"] = red, ["center"] = C() });
            layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = key, ["at"] = new JsonArray([.. labels.Select(l => (JsonNode)l)]), ["divideBy"] = divideBy, ["radius"] = r - 18, ["size"] = 13, ["color"] = ink, ["center"] = C() });
            layers.Add(new JsonObject { ["type"] = "text", ["text"] = title, ["x"] = cx, ["y"] = cy + 34, ["size"] = 12, ["color"] = ink });
            for (var e = 4; e >= 1; e--)
                layers.Add(new JsonObject
                {
                    ["type"] = "needle", ["style"] = "pointer", ["scale"] = key, ["value"] = $"{key}{e}", ["length"] = r - 4 - (e - 1) * 4, ["width"] = 2.2, ["tail"] = 8,
                    ["color"] = ink, ["tailColor"] = "#2A2B2C", ["center"] = C(), // no engine tags: too small here; the table names each engine
                });
            layers.Add(new JsonObject { ["type"] = "circle", ["center"] = C(), ["r"] = 4, ["fill"] = "#1A1A1A", ["stroke"] = "#555555" });
        }
        layers.Add(new JsonObject { ["type"] = "rect", ["x"] = 0, ["y"] = 0, ["width"] = 320, ["height"] = 240, ["fill"] = "#5A6064" });
        Dial("rpm", 55, "RPM", 0, 300, [0, 100, 200, 300], 10, [(260, 280, yellow)], [280]);
        Dial("mp", 160, "MP", 100, 600, [100, 200, 300, 400, 500, 600], 10, [(200, 400, green), (490, 600, yellow)], [530]);
        Dial("bmep", 265, "BMEP", 60, 260, [60, 100, 150, 200, 260], 1, [], [240]);

        // Table of exact numbers.
        layers.Add(new JsonObject { ["type"] = "rect", ["x"] = 4, ["y"] = 120, ["width"] = 312, ["height"] = 117, ["radius"] = 6, ["fill"] = "#0B0B0B", ["stroke"] = "#2A2A2A", ["strokeWidth"] = 1.5 });
        double[] cols = [104, 158, 212, 266];
        for (var e = 0; e < 4; e++)
            layers.Add(new JsonObject { ["type"] = "text", ["text"] = $"ENG {e + 1}", ["x"] = cols[e], ["y"] = 137, ["size"] = 12, ["color"] = "#A8A397" });
        var rows = new[] { ("RPM", "rpm{0}_x", "0"), ("MP IN", "mp{0}_x", "0.0"), ("BMEP", "bmep{0}", "0"), ("FF LB/H", "ff{0}_x", "0") };
        for (var row = 0; row < rows.Length; row++)
        {
            var y = 162 + row * 23;
            var (label, value, format) = rows[row];
            layers.Add(new JsonObject { ["type"] = "text", ["text"] = label, ["x"] = 12, ["y"] = y, ["size"] = 13, ["color"] = "#A8A397", ["align"] = "left" });
            for (var e = 0; e < 4; e++)
                layers.Add(new JsonObject { ["type"] = "text", ["value"] = string.Format(CultureInfo.InvariantCulture, value, e + 1), ["format"] = format, ["x"] = cols[e] + 22, ["y"] = y, ["size"] = 19, ["color"] = ink, ["align"] = "right" });
        }
        return Gauge("DC-6 Engines (all in one)", "PMDG DC-6: RPM, manifold pressure and BMEP dials with numbered needles, and a table of exact numbers per engine (RPM, MP, BMEP, fuel flow).", "#5A6064", values, scales, layers, font);
    }

    private static JsonObject Box(double x, double y, double w, double h) =>
        new() { ["type"] = "rect", ["x"] = Math.Round(x, 2), ["y"] = Math.Round(y, 2), ["width"] = Math.Round(w, 2), ["height"] = Math.Round(h, 2), ["fill"] = "#000000", ["stroke"] = White, ["strokeWidth"] = 1.3 };

    /// <summary>A number, white normally and red once past <paramref name="red"/> (two copies, one moved off screen).</summary>
    internal static void AddReadout(JsonArray layers, JsonObject values, string value, string format, double x, double y, double size, double? red, string normal = White)
    {
        JsonObject Text(string color) => new() { ["type"] = "text", ["value"] = value, ["format"] = format, ["x"] = Math.Round(x, 2), ["y"] = Math.Round(y, 2), ["size"] = size, ["color"] = color, ["align"] = "right" };
        if (red is not double limit) { layers.Add(Text(normal)); return; }
        values[$"{value}_ok"] = new JsonObject { ["expr"] = $"choose({value} - {N(limit)}, 400, 0)" };
        values[$"{value}_hot"] = new JsonObject { ["expr"] = $"choose({value} - {N(limit)}, 0, 400)" };
        layers.Add(new JsonObject { ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = $"{value}_ok", ["scale"] = "px" }, ["layers"] = new JsonArray(Text(normal)) });
        layers.Add(new JsonObject { ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = $"{value}_hot", ["scale"] = "px" }, ["layers"] = new JsonArray(Text(RedInk)) });
    }

    private static string Gauge(string name, string description, string background, JsonObject values, JsonObject scales, JsonArray layers, string font)
    {
        scales["px"] ??= new JsonObject { ["min"] = 0, ["max"] = 400, ["startAngle"] = 0, ["endAngle"] = 400 };
        return new JsonObject
        {
            ["format"] = 1, ["name"] = name, ["author"] = "Made with FIPinator 3000", ["description"] = description,
            ["canvas"] = new JsonObject { ["width"] = 320, ["height"] = 240, ["background"] = background },
            ["center"] = new JsonArray(160, 120), ["font"] = font, ["values"] = values, ["scales"] = scales, ["layers"] = layers,
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
