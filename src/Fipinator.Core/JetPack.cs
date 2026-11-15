// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fipinator.Core;

/// <summary>Numbers for a jet's engine gauges; one record per engine type / aircraft.</summary>
public sealed record JetProfile(
    string Name,
    int Engines,
    double N1Red,
    double EgtMax, double EgtCaution, double EgtRed,
    double N2Red,
    double FuelFlowMax,
    double OilPressMax, double OilPressLowRed,
    double OilTempMax, double OilTempGreenFrom, double OilTempGreenTo, double OilTempRed,
    string Source,
    bool CorrectedSpeeds = false,
    double EprMax = 0,
    bool OilPage = true,
    GaugeLook RoundLook = GaugeLook.Classic)
{
    /// <summary>An EPR page first (727, DC-9 era planes set thrust by EPR); 0 = no EPR page.</summary>
    public bool HasEpr => EprMax > 0;

    /// <summary>
    /// The sim value for N1: TURB ENG CORRECTED N1 when the plane's own display shows corrected speeds.
    /// The Asobo 737 MAX does: checked 2026-11-10 (display 36.2, corrected, plain 35.4) and 2026-11-11 at low power
    /// (display 41.1 / 41.6, corrected 41.4 / 41.8, plain 40.0 / 40.5). The Asobo 727 shows plain N1.
    /// </summary>
    public string N1Sim => CorrectedSpeeds ? "TURB ENG CORRECTED N1" : "TURB ENG N1";

    /// <summary>The sim value for N2, corrected or plain like <see cref="N1Sim"/>.</summary>
    public string N2Sim => CorrectedSpeeds ? "TURB ENG CORRECTED N2" : "TURB ENG N2";
}

/// <summary>
/// Jet engine gauges: N1, EGT, N2, fuel flow and oil, a needle (or dial) per engine. Two looks from
/// the same numbers: <see cref="Look.Classic"/> round steam gauges (727 / DC-9 era) and
/// <see cref="Look.Modern"/> glass-cockpit dials (white arcs, filled sweep, digital boxes that turn
/// red past the limit), like a 737 MAX engine display. Stock MSFS 2024 sim values: TURB ENG N1,
/// TURB ENG N2, ENG EXHAUST GAS TEMPERATURE, ENG FUEL FLOW PPH, GENERAL ENG OIL PRESSURE / TEMPERATURE.
/// </summary>
public static class JetPack
{
    public enum Look { Classic, Modern }

    /// <summary>
    /// Boeing 737 MAX, CFM LEAP-1B. EASA TCDS E.115 (issue 09): N1 max 104.3 %, N2 max 117.5 %
    /// (indicated), EGT 1038 °C take-off / 1013 °C max continuous (indicated), oil temperature 31 °C
    /// minimum for take-off power, 140 °C steady, 155 °C for 15 minutes, oil pressure minimum
    /// 1200 hPa (17.4 psi) at idle. Fuel flow and the oil pressure scale aren't limits: typical ranges.
    /// </summary>
    public static readonly JetProfile Boeing737Max = new("Boeing 737 MAX", 2,
        N1Red: 104.3, EgtMax: 1100, EgtCaution: 1013, EgtRed: 1038, N2Red: 117.5, FuelFlowMax: 10000,
        OilPressMax: 100, OilPressLowRed: 17.4, OilTempMax: 180, OilTempGreenFrom: 31, OilTempGreenTo: 140, OilTempRed: 155,
        Source: "EASA TCDS E.115 (CFM LEAP-1B)", CorrectedSpeeds: true);

    /// <summary>
    /// Boeing 737-800 (737NG, like the PMDG 737-800), CFM56-7B. EASA TCDS E.004 (issue 07): N1 max 104 %
    /// (5382 rpm), N2 max 105 % (15183 rpm), EGT 950 °C take-off / 925 °C max continuous (displayed), oil
    /// temperature 140 °C max continuous above idle, 155 °C for 45 minutes, oil pressure minimum 90 kPa (13 psi).
    /// Fuel flow and the oil pressure scale aren't limits: typical ranges. Which N1/N2 (plain or corrected) the
    /// PMDG's display shows: still to compare in the cockpit; plain for now.
    /// </summary>
    public static readonly JetProfile Boeing737_800 = new("Boeing 737-800", 2,
        N1Red: 104, EgtMax: 1100, EgtCaution: 925, EgtRed: 950, N2Red: 105, FuelFlowMax: 10000,
        OilPressMax: 100, OilPressLowRed: 13, OilTempMax: 180, OilTempGreenFrom: 0, OilTempGreenTo: 140, OilTempRed: 155,
        Source: "EASA TCDS E.004 (CFM56-7B)");

    /// <summary>
    /// Airbus A320neo, CFM LEAP-1A. EASA TCDS E.110 (issue 14): N1 max 101 %, N2 max 116.5 % (indicated),
    /// EGT 1060 °C take-off / 1025 °C max continuous (indicated), oil temperature 19 °C minimum for take-off
    /// power, 140 °C steady, 155 °C for 15 minutes, oil pressure minimum 1200 hPa (17.4 psi) at idle.
    /// Whether the sim's A320neo shows plain or corrected N1/N2: still to check live.
    /// </summary>
    public static readonly JetProfile AirbusA320neo = new("Airbus A320neo", 2,
        N1Red: 101, EgtMax: 1100, EgtCaution: 1025, EgtRed: 1060, N2Red: 116.5, FuelFlowMax: 10000,
        OilPressMax: 100, OilPressLowRed: 17.4, OilTempMax: 180, OilTempGreenFrom: 19, OilTempGreenTo: 140, OilTempRed: 155,
        Source: "EASA TCDS E.110 (CFM LEAP-1A)");

    /// <summary>
    /// Boeing 727 (three P&W JT8D). Dial ranges as on the 727's own gauges; EPR first, then N1, EGT, N2,
    /// fuel flow; no oil page (owner). Red lines left out until the JT8D limits are read from the
    /// official FAA engine sheet (E2EA). Plain (not corrected) N1/N2: checked live 2026-11-10, the
    /// sim's 727 shows 38 % = TURB ENG N1 37.9.
    /// </summary>
    public static readonly JetProfile Boeing727 = new("Boeing 727", 3,
        N1Red: 0, EgtMax: 800, EgtCaution: 0, EgtRed: 0, N2Red: 0, FuelFlowMax: 12000,
        OilPressMax: 0, OilPressLowRed: 0, OilTempMax: 0, OilTempGreenFrom: 0, OilTempGreenTo: 0, OilTempRed: 0,
        Source: "dial ranges from the 727 cockpit; limits not yet verified (FAA TCDS E2EA)",
        EprMax: 3.0, OilPage: false, RoundLook: GaugeLook.Boeing727);

    private static double[] Red(double v) => v > 0 ? [v] : [];

    /// <summary>The engine pages for a plane: EPR first when it has one, else N1 (the thrust gauge pilots watch most).</summary>
    public static IReadOnlyList<(string File, string Json)> Create(JetProfile plane, Look look, string font = "BarlowCondensed-Medium.ttf") =>
        look == Look.Classic ? Round(plane, font) :
            [
                ("n1", Modern(plane, font, "N1", plane.N1Sim, "percent", 1, 0, 110, [0, 20, 40, 60, 80, 100], 10, plane.N1Red, null, "0.0", 85)),
                ("egt", Modern(plane, font, "EGT", "ENG EXHAUST GAS TEMPERATURE", "celsius", 1, 0, plane.EgtMax, [0, 500, 1000], 100, plane.EgtRed, plane.EgtCaution, "0", 650)),
                ("n2", Modern(plane, font, "N2", plane.N2Sim, "percent", 1, 0, 120, [0, 20, 40, 60, 80, 100], 10, plane.N2Red, null, "0.0", 90)),
                ("fuel-flow", Modern(plane, font, "FF", "ENG FUEL FLOW PPH", "pounds per hour", 0.001, 0, plane.FuelFlowMax / 1000, [0, 2, 4, 6, 8, 10], 1, null, null, "0.00", 3)),
                ("oil", ModernOil(plane, font)),
            ];

    private static List<(string File, string Json)> Round(JetProfile plane, string font)
    {
        var look = plane.RoundLook;
        var list = new List<(string File, string Json)>();
        if (plane.HasEpr)
            list.Add(("epr", TurbopropPack.RoundDial(plane.Name, plane.Engines, plane.Source, look, font, "EPR", "epr", "TURB ENG PRESSURE RATIO", "ratio", null,
                1.0, plane.EprMax, 0.05, 0.2, 0.4, 1, [], [], [("PRESS RATIO", 66, 13), ("EPR", 80, 9)], 1.6, digital: true, digitalFormat: "0.00")));
        list.Add(("n1", TurbopropPack.RoundDial(plane.Name, plane.Engines, plane.Source, look, font, "N1", "n1", plane.N1Sim, "percent", null,
            0, 110, 2, 10, 20, 10, [], Red(plane.N1Red), [("N1", 66, 16), ("% RPM", 80, 9)], 85, digital: true, digitalFormat: "0.0")));
        var egtArcs = plane.EgtCaution > 0 && plane.EgtRed > plane.EgtCaution ? new[] { (plane.EgtCaution, plane.EgtRed, GaugeParts.Yellow) } : [];
        list.Add(("egt", TurbopropPack.RoundDial(plane.Name, plane.Engines, plane.Source, look, font, "EGT", "egt", "ENG EXHAUST GAS TEMPERATURE", "celsius", null,
            0, plane.EgtMax, 20, 100, 200, 100, egtArcs, Red(plane.EgtRed), [("EGT", 66, 15), ("°C X 100", 80, 9)], plane.EgtMax * 0.55, digital: true)));
        list.Add(("n2", TurbopropPack.RoundDial(plane.Name, plane.Engines, plane.Source, look, font, "N2", "n2", plane.N2Sim, "percent", null,
            0, 120, 2, 10, 20, 10, [], Red(plane.N2Red), [("N2", 66, 16), ("% RPM", 80, 9)], 90, digital: true, digitalFormat: "0.0")));
        list.Add(("fuel-flow", TurbopropPack.RoundDial(plane.Name, plane.Engines, plane.Source, look, font, "Fuel flow", "ff", "ENG FUEL FLOW PPH", "pounds per hour", null,
            0, plane.FuelFlowMax, 250, 1000, 2000, 1000, [], [], [("FUEL FLOW", 44, 13), ("LB/H X 1000", 60, 9)], plane.FuelFlowMax * 0.3)));
        if (plane.OilPage)
            list.Add(("oil", TurbopropPack.OilDial(plane.Name, plane.Engines, plane.Source, look, font,
                0, plane.OilTempMax, (plane.OilTempGreenFrom, plane.OilTempGreenTo), [plane.OilTempRed], plane.OilPressMax, (0, 0), [plane.OilPressLowRed])));
        return list;
    }

    private const string White = "#F2F2F2", Amber = "#F5A623", RedInk = "#FF3B30", Cyan = "#7FD6F0", Sweep = "#5E5E5E";

    /// <summary>Where each engine's dial sits, and how big, for 1-4 engines across a 320x240 FIP.</summary>
    private static (double X, double Y, double R)[] Dials(int engines) => engines switch
    {
        1 => [(160, 152, 86)],
        2 => [(80, 152, 68), (240, 152, 68)],
        3 => [(56, 156, 46), (160, 156, 46), (264, 156, 46)],
        _ => [.. Enumerable.Range(0, engines).Select(i => (40.0 + i * 80, 156.0, 34.0))],
    };

    /// <summary>
    /// One glass-style dial per engine: white scale arc (amber band before the red line), a grey
    /// sweep that fills up to the value, a white pointer, and a digital box above it that shows the
    /// number in white, or red once it passes <paramref name="red"/>.
    /// </summary>
    private static string Modern(JetProfile plane, string font, string title, string sim, string unit, double factor,
        double min, double max, double[] labels, double divideBy, double? red, double? caution, string format, double sample)
    {
        var layers = new JsonArray();
        var values = new JsonObject();
        JsonArray At(double x, double y) => new(Math.Round(x, 2), Math.Round(y, 2));
        var dials = Dials(plane.Engines);
        for (var e = 1; e <= plane.Engines; e++)
        {
            var (x, y, r) = dials[e - 1];
            var v = $"e{e}";
            var start = Math.Round(sample * (1 + (e - 1) * 0.04), 2);
            if (factor == 1)
                values[v] = new JsonObject { ["sim"] = ClassicPack.Engine(sim, e), ["unit"] = unit, ["default"] = start };
            else
            {
                values[$"{v}_raw"] = new JsonObject { ["sim"] = ClassicPack.Engine(sim, e), ["unit"] = unit, ["default"] = Math.Round(start / factor, 1) };
                values[v] = new JsonObject { ["expr"] = $"{v}_raw * {factor.ToString(CultureInfo.InvariantCulture)}" };
            }

            var top = caution ?? red ?? max;
            layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = "s", ["from"] = min, ["toValue"] = v, ["radius"] = Math.Round(r / 2, 2), ["width"] = r, ["color"] = Sweep, ["center"] = At(x, y) });
            layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = "s", ["from"] = min, ["to"] = top, ["radius"] = r, ["width"] = 2.5, ["color"] = White, ["center"] = At(x, y) });
            if (caution is double c && red is double rr)
                layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = "s", ["from"] = c, ["to"] = rr, ["radius"] = r, ["width"] = 2.5, ["color"] = Amber, ["center"] = At(x, y) });
            if (red is double rl)
                layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "s", ["at"] = new JsonArray(rl), ["radius"] = r + 7, ["length"] = 16, ["width"] = 3, ["color"] = RedInk, ["center"] = At(x, y) });
            layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "s", ["at"] = new JsonArray([.. labels.Select(l => (JsonNode)l)]), ["radius"] = r, ["length"] = 7, ["width"] = 2, ["color"] = White, ["center"] = At(x, y) });
            if (r >= 44)
                layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = "s", ["at"] = new JsonArray([.. labels.Select(l => (JsonNode)l)]), ["divideBy"] = divideBy, ["radius"] = Math.Round(r - 17, 2), ["size"] = Math.Round(r / 4.6, 1), ["color"] = White, ["center"] = At(x, y) });
            layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "s", ["value"] = v, ["length"] = r, ["width"] = 2.6, ["tail"] = 0, ["color"] = White, ["center"] = At(x, y) });

            // Digital box above the dial, right of centre; red text once past the red line.
            var (bw, bh) = (Math.Round(r * 0.95, 1), Math.Round(r * 0.4, 1));
            var (bx, by) = (x + 2, y - r - bh + 4);
            var size = Math.Round(bh * 0.8, 1);
            layers.Add(new JsonObject { ["type"] = "rect", ["x"] = Math.Round(bx, 2), ["y"] = Math.Round(by, 2), ["width"] = bw, ["height"] = bh, ["fill"] = "#000000", ["stroke"] = White, ["strokeWidth"] = 1.5 });
            var text = (string color) => new JsonObject { ["type"] = "text", ["value"] = v, ["format"] = format, ["x"] = Math.Round(bx + bw - 4, 2), ["y"] = Math.Round(by + bh * 0.8, 2), ["size"] = size, ["color"] = color, ["align"] = "right" };
            if (red is double limit)
            {
                var over = $"{v} - {limit.ToString(CultureInfo.InvariantCulture)}";
                values[$"{v}_ok"] = new JsonObject { ["expr"] = $"choose({over}, 400, 0)" };
                values[$"{v}_hot"] = new JsonObject { ["expr"] = $"choose({over}, 0, 400)" };
                layers.Add(new JsonObject { ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = $"{v}_ok", ["scale"] = "px" }, ["layers"] = new JsonArray(text(White)) });
                layers.Add(new JsonObject { ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = $"{v}_hot", ["scale"] = "px" }, ["layers"] = new JsonArray(text(RedInk)) });
            }
            else
                layers.Add(text(White));
        }
        var (ty, tsize) = plane.Engines == 2 ? (224.0, 22.0) : (232.0, 18.0);
        layers.Add(new JsonObject { ["type"] = "text", ["text"] = title, ["x"] = 160, ["y"] = ty, ["size"] = tsize, ["color"] = Cyan });

        var scales = new JsonObject
        {
            ["s"] = new JsonObject { ["min"] = min, ["max"] = max, ["startAngle"] = -100, ["endAngle"] = 110 },
            ["px"] = new JsonObject { ["min"] = 0, ["max"] = 400, ["startAngle"] = 0, ["endAngle"] = 400 },
        };
        var limits = red is double l2 ? $"; red line {l2.ToString(CultureInfo.InvariantCulture)}" : "";
        return Glass($"{plane.Name} {title} (modern)", $"{plane.Name} {title}, glass-cockpit style: a dial per engine with a filled sweep and a digital box{limits}. Limits: {plane.Source}.", values, scales, layers, font);
    }

    /// <summary>Oil pressure and temperature as digital boxes per engine, red when out of limits.</summary>
    private static string ModernOil(JetProfile plane, string font)
    {
        var layers = new JsonArray();
        var values = new JsonObject();
        var n = plane.Engines;
        var bw = n <= 2 ? 84.0 : 56.0;
        double BoxX(int e) => n switch
        {
            1 => 160 - bw / 2,
            2 => e == 1 ? 24 : 320 - 24 - bw,
            _ => 18 + (e - 1) * ((284 - bw) / (n - 1)),
        };
        void Row(string key, string label, string sim, string unit, double y, string test, double sample)
        {
            layers.Add(new JsonObject { ["type"] = "text", ["text"] = label, ["x"] = 160, ["y"] = n <= 2 ? y + 26 : y - 8, ["size"] = 17, ["color"] = Cyan });
            for (var e = 1; e <= n; e++)
            {
                var v = $"{key}{e}";
                values[v] = new JsonObject { ["sim"] = ClassicPack.Engine(sim, e), ["unit"] = unit, ["default"] = sample };
                values[$"{v}_ok"] = new JsonObject { ["expr"] = $"choose({test.Replace("x", v)}, 400, 0)" };
                values[$"{v}_bad"] = new JsonObject { ["expr"] = $"choose({test.Replace("x", v)}, 0, 400)" };
                var x = BoxX(e);
                layers.Add(new JsonObject { ["type"] = "rect", ["x"] = Math.Round(x, 2), ["y"] = y, ["width"] = bw, ["height"] = 38, ["fill"] = "#000000", ["stroke"] = White, ["strokeWidth"] = 1.5 });
                JsonObject Text(string color) => new() { ["type"] = "text", ["value"] = v, ["format"] = "0", ["x"] = Math.Round(x + bw - 8, 2), ["y"] = y + 31, ["size"] = 30, ["color"] = color, ["align"] = "right" };
                layers.Add(new JsonObject { ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = $"{v}_ok", ["scale"] = "px" }, ["layers"] = new JsonArray(Text(White)) });
                layers.Add(new JsonObject { ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = $"{v}_bad", ["scale"] = "px" }, ["layers"] = new JsonArray(Text(RedInk)) });
            }
        }
        var inv = CultureInfo.InvariantCulture;
        Row("press", "OIL PRESS", "GENERAL ENG OIL PRESSURE", "psi", 40, $"{plane.OilPressLowRed.ToString(inv)} - x", 42);
        Row("temp", "OIL TEMP", "GENERAL ENG OIL TEMPERATURE", "celsius", 140, $"x - {plane.OilTempRed.ToString(inv)}", 95);

        var scales = new JsonObject { ["px"] = new JsonObject { ["min"] = 0, ["max"] = 400, ["startAngle"] = 0, ["endAngle"] = 400 } };
        return Glass($"{plane.Name} Oil (modern)", $"{plane.Name} oil pressure (psi) and temperature (°C) per engine; red below {plane.OilPressLowRed.ToString(inv)} psi or above {plane.OilTempRed.ToString(inv)} °C. Limits: {plane.Source}.", values, scales, layers, font);
    }

    /// <summary>
    /// Jet fuel page, glass style (like a 737 MAX's fuel display): tank 1 and tank 2 dials with the
    /// pounds in each, the centre tank on top, and the total below. Each dial's sweep shows how full
    /// that tank is. Stock sim values: FUEL TANK LEFT MAIN / CENTER / RIGHT MAIN QUANTITY and CAPACITY
    /// (gallons) times FUEL WEIGHT PER GALLON.
    /// </summary>
    public static string Fuel(string name, string font = "BarlowCondensed-Medium.ttf")
    {
        var layers = new JsonArray();
        var values = new JsonObject
        {
            ["wpg"] = new JsonObject { ["sim"] = "FUEL WEIGHT PER GALLON", ["unit"] = "pounds", ["default"] = 6.7 },
        };
        JsonArray At(double x, double y) => new(x, y);
        void Tank(string key, string sim, string label, double x, double y, double r, double sampleGal, double capGal)
        {
            values[$"{key}_gal"] = new JsonObject { ["sim"] = $"FUEL TANK {sim} QUANTITY", ["unit"] = "gallons", ["default"] = sampleGal };
            values[$"{key}_cap"] = new JsonObject { ["sim"] = $"FUEL TANK {sim} CAPACITY", ["unit"] = "gallons", ["default"] = capGal };
            values[$"{key}_lb"] = new JsonObject { ["expr"] = $"{key}_gal * wpg" };
            values[$"{key}_full"] = new JsonObject { ["expr"] = $"clamp({key}_gal / max({key}_cap, 1), 0, 1)" };
            layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = "f", ["from"] = 0, ["to"] = 1, ["radius"] = r, ["width"] = 2.5, ["color"] = White, ["center"] = At(x, y) });
            layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = "f", ["from"] = 0, ["toValue"] = $"{key}_full", ["radius"] = r - 6, ["width"] = 8, ["color"] = "#9A9A9A", ["center"] = At(x, y) });
            layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "f", ["at"] = new JsonArray(0, 0.25, 0.5, 0.75, 1), ["radius"] = r, ["length"] = 6, ["width"] = 2, ["color"] = White, ["center"] = At(x, y) });
            layers.Add(new JsonObject { ["type"] = "text", ["value"] = $"{key}_lb", ["format"] = "0", ["x"] = x, ["y"] = y + 8, ["size"] = Math.Round(r * 0.46, 1), ["color"] = White });
            layers.Add(new JsonObject { ["type"] = "text", ["text"] = label, ["x"] = x, ["y"] = y - r * 0.38, ["size"] = 13, ["color"] = Cyan });
        }
        Tank("l", "LEFT MAIN", "1", 72, 140, 56, 1270, 1288);
        Tank("r", "RIGHT MAIN", "2", 248, 140, 56, 1265, 1288);
        Tank("c", "CENTER", "CTR", 160, 62, 40, 800, 4290);
        values["total"] = new JsonObject { ["expr"] = "(l_gal + c_gal + r_gal) * wpg" };
        layers.Add(new JsonObject { ["type"] = "text", ["text"] = "FUEL", ["x"] = 160, ["y"] = 136, ["size"] = 15, ["color"] = Cyan });
        layers.Add(new JsonObject { ["type"] = "text", ["text"] = "LB", ["x"] = 160, ["y"] = 152, ["size"] = 13, ["color"] = Cyan });
        layers.Add(new JsonObject { ["type"] = "text", ["text"] = "TOTAL", ["x"] = 112, ["y"] = 224, ["size"] = 14, ["color"] = Cyan, ["align"] = "right" });
        layers.Add(new JsonObject { ["type"] = "rect", ["x"] = 120, ["y"] = 202, ["width"] = 96, ["height"] = 30, ["fill"] = "#000000", ["stroke"] = White, ["strokeWidth"] = 1.5 });
        layers.Add(new JsonObject { ["type"] = "text", ["value"] = "total", ["format"] = "0", ["x"] = 210, ["y"] = 226, ["size"] = 24, ["color"] = White, ["align"] = "right" });

        var scales = new JsonObject { ["f"] = new JsonObject { ["min"] = 0, ["max"] = 1, ["startAngle"] = -135, ["endAngle"] = 135 } };
        return Glass(name, "Jet fuel, glass style: tank 1, centre and tank 2 in pounds with how-full sweeps, and the total.", values, scales, layers, font);
    }

    private static string Glass(string name, string description, JsonObject values, JsonObject scales, JsonArray layers, string font) =>
        new JsonObject
        {
            ["format"] = 1,
            ["name"] = name,
            ["author"] = "Made with FIPinator 3000",
            ["description"] = description,
            ["canvas"] = new JsonObject { ["width"] = 320, ["height"] = 240, ["background"] = "#000000" },
            ["center"] = new JsonArray(160, 120),
            ["font"] = font,
            ["values"] = values,
            ["scales"] = scales,
            ["layers"] = layers,
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
