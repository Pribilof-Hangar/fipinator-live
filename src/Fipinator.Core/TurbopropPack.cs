// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using System.Text.Json.Nodes;

namespace Fipinator.Core;

/// <summary>
/// Numbers for a turboprop's engine gauges. One record per aircraft type; the same gauge code draws
/// a single (Caravan) or a twin (Saab 340, King Air). Torque reads either in percent or in ft-lb,
/// fuel flow in kg/h or lb/h, so the pack fits both European and US style panels.
/// </summary>
public sealed record TurbopropProfile(
    string Name,
    int Engines,
    bool TorqueInPercent,
    double TorqueMax, double TorqueGreenTo, double TorqueRed,
    double IttMax, double IttGreenFrom, double IttGreenTo, double IttRed,
    double NgGreenFrom, double NgRed,
    double PropMax, double PropGreenFrom, double PropGreenTo, double PropRed,
    bool FuelFlowInKg, double FuelFlowMax,
    double OilPressGreenFrom, double OilPressGreenTo, double OilPressLowRed, double OilPressHighRed,
    double OilTempGreenFrom, double OilTempGreenTo, double OilTempRed,
    string Source);

/// <summary>
/// Classic round engine gauges for turboprops: torque, ITT (with a digital readout), gas generator
/// speed (Ng), propeller RPM, fuel flow and engine oil. A numbered needle per engine, like the DC-6
/// gauges. Sim values are the stock ones from the MSFS 2024 SDK (Aircraft Engine Variables):
/// TURB ENG MAX TORQUE PERCENT / ENG TORQUE, TURB ENG ITT, TURB ENG N1 (Ng on a turboprop),
/// PROP RPM, ENG FUEL FLOW PPH, GENERAL ENG OIL PRESSURE / TEMPERATURE.
/// </summary>
public static class TurbopropPack
{
    /// <summary>
    /// Saab 340B with GE CT7-9B engines. Torque: UK CAA TCDS UK.TC.A.00163 (100 % take-off and max
    /// continuous, 107 % with APR). ITT, Ng, propeller speed and oil: EASA TCDS IM.E.010 for the CT7-9B
    /// (ITT max continuous 944 °C, transient/start 965 °C; 100 % Ng = 44,700 rpm, max continuous
    /// 45,600 = 102 %; propeller 1,384 rpm nominal, 1,396 max continuous, 1,249 minimum governing
    /// (power turbine speeds / 15.9 gearing); oil 30-100 psi, 20 psi minimum at idle, 132 °C normal,
    /// 149 °C for 15 minutes). The lower ends of the ITT, Ng and oil temperature green arcs are not in
    /// the data sheets: they're typical values, drawn so the gauge reads like the real one.
    /// </summary>
    public static readonly TurbopropProfile Saab340B = new("Saab 340B", 2,
        TorqueInPercent: true, TorqueMax: 120, TorqueGreenTo: 100, TorqueRed: 107,
        IttMax: 1000, IttGreenFrom: 400, IttGreenTo: 944, IttRed: 965,
        NgGreenFrom: 60, NgRed: 102,
        PropMax: 1500, PropGreenFrom: 1249, PropGreenTo: 1396, PropRed: 1396,
        FuelFlowInKg: true, FuelFlowMax: 400,
        OilPressGreenFrom: 30, OilPressGreenTo: 100, OilPressLowRed: 20, OilPressHighRed: 100,
        OilTempGreenFrom: 40, OilTempGreenTo: 132, OilTempRed: 149,
        Source: "UK CAA TCDS UK.TC.A.00163, EASA TCDS IM.E.010 (CT7-9B)");

    /// <summary>
    /// Cessna 208B Grand Caravan with a PT6A-114A. Torque 1,865 ft-lb, Ng 101.6 %, propeller 1,900 rpm,
    /// ITT 805 °C (765 °C max climb): EASA TCDS IM.A.226. Oil pressure 85-105 psi (40 psi minimum) and
    /// oil temperature 10-99 °C come from POH summaries, not a data sheet: check against the POH.
    /// The lower ends of the ITT, Ng and propeller green arcs are typical values.
    /// </summary>
    public static readonly TurbopropProfile Caravan208B = new("Cessna 208B Caravan", 1,
        TorqueInPercent: false, TorqueMax: 2400, TorqueGreenTo: 1865, TorqueRed: 1865,
        IttMax: 1000, IttGreenFrom: 400, IttGreenTo: 765, IttRed: 805,
        NgGreenFrom: 52, NgRed: 101.6,
        PropMax: 2000, PropGreenFrom: 1600, PropGreenTo: 1900, PropRed: 1900,
        FuelFlowInKg: false, FuelFlowMax: 500,
        OilPressGreenFrom: 85, OilPressGreenTo: 105, OilPressLowRed: 40, OilPressHighRed: 105,
        OilTempGreenFrom: 10, OilTempGreenTo: 99, OilTempRed: 99,
        Source: "EASA TCDS IM.A.226; oil limits from POH summaries");

    /// <summary>Every turboprop engine gauge for a plane, file name first, torque first (watched most).</summary>
    public static IReadOnlyList<(string File, string Json)> Create(TurbopropProfile plane, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf") =>
    [
        ("torque", Torque(plane, look, font)),
        ("itt", Itt(plane, look, font)),
        ("ng", Ng(plane, look, font)),
        ("prop-rpm", PropRpm(plane, look, font)),
        ("fuel-flow", FuelFlow(plane, look, font)),
        ("oil", Oil(plane, look, font)),
    ];

    public static string Torque(TurbopropProfile p, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf")
    {
        var arcs = new List<(double, double, string)> { (0, p.TorqueGreenTo, GaugeParts.Green) };
        if (p.TorqueRed > p.TorqueGreenTo) arcs.Add((p.TorqueGreenTo, p.TorqueRed, GaugeParts.Yellow));
        return p.TorqueInPercent
            ? Dial(p, look, font, "Torque", "trq", "TURB ENG MAX TORQUE PERCENT", "percent", null,
                0, p.TorqueMax, 2, 10, 20, 1, arcs, [p.TorqueRed], [("TRQ", 44, 16), ("%", 62, 12)], p.TorqueGreenTo * 0.7)
            : Dial(p, look, font, "Torque", "trq", "ENG TORQUE", "foot pounds", null,
                0, p.TorqueMax, 100, 200, 400, 100, arcs, [p.TorqueRed], [("TORQUE", 44, 14), ("FT-LB X 100", 60, 9)], p.TorqueGreenTo * 0.7);
    }

    public static string Itt(TurbopropProfile p, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf")
    {
        var arcs = new List<(double, double, string)> { (p.IttGreenFrom, p.IttGreenTo, GaugeParts.Green) };
        if (p.IttRed > p.IttGreenTo) arcs.Add((p.IttGreenTo, p.IttRed, GaugeParts.Yellow));
        return Dial(p, look, font, "ITT", "itt", "TURB ENG ITT", "celsius", null,
            0, p.IttMax, 20, 100, 200, 100, arcs, [p.IttRed], [("ITT", 66, 15), ("°C X 100", 80, 9)], p.IttGreenFrom + 250, digital: true);
    }

    public static string Ng(TurbopropProfile p, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf") =>
        Dial(p, look, font, "Ng", "ng", "TURB ENG N1", "percent", null,
            0, 110, 2, 10, 20, 1, [(p.NgGreenFrom, p.NgRed, GaugeParts.Green)], [p.NgRed], [("NG", 44, 16), ("% RPM", 62, 10)], 72);

    public static string PropRpm(TurbopropProfile p, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf")
    {
        var label = p.PropMax <= 1500 ? 300 : 400;
        return Dial(p, look, font, "Prop RPM", "np", "PROP RPM", "rpm", null,
            0, p.PropMax, 50, 100, label, 100, [(p.PropGreenFrom, p.PropGreenTo, GaugeParts.Green)], [p.PropRed], [("PROP", 44, 15), ("RPM X 100", 60, 9)], p.PropGreenFrom);
    }

    public static string FuelFlow(TurbopropProfile p, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf") =>
        p.FuelFlowInKg
            ? Dial(p, look, font, "Fuel flow", "ff", "ENG FUEL FLOW PPH", "pounds per hour", 0.45359237,
                0, p.FuelFlowMax, 10, 50, 100, 100, [], [], [("FUEL FLOW", 44, 13), ("KG/H X 100", 60, 9)], p.FuelFlowMax * 0.45)
            : Dial(p, look, font, "Fuel flow", "ff", "ENG FUEL FLOW PPH", "pounds per hour", null,
                0, p.FuelFlowMax, 10, 50, 100, 100, [], [], [("FUEL FLOW", 44, 13), ("LB/H X 100", 60, 9)], p.FuelFlowMax * 0.45);

    private static string Dial(TurbopropProfile plane, GaugeLook look, string font, string what, string scale, string sim, string unit, double? factor,
        double min, double max, double minor, double major, double labelEvery, double divideBy,
        IEnumerable<(double From, double To, string Color)> arcs, double[] reds, (string Text, double Dy, double Size)[] words, double sample, bool digital = false) =>
        RoundDial(plane.Name, plane.Engines, plane.Source, look, font, what, scale, sim, unit, factor, min, max, minor, major, labelEvery, divideBy, arcs, reds, words, sample, digital);

    /// <summary>
    /// One round dial with a numbered needle per engine (shared by the turboprop and jet packs);
    /// <paramref name="factor"/> converts the sim's unit (lb to kg); <paramref name="digital"/> adds a
    /// small amber readout per engine under the hub.
    /// </summary>
    internal static string RoundDial(string planeName, int engines, string source, GaugeLook look, string font, string what, string scale, string sim, string unit, double? factor,
        double min, double max, double minor, double major, double labelEvery, double divideBy,
        IEnumerable<(double From, double To, string Color)> arcs, double[] reds, (string Text, double Dy, double Size)[] words, double sample, bool digital = false,
        string digitalFormat = "0")
    {
        var plane = (Name: planeName, Engines: engines, Source: source);
        var g = new GaugeParts(look);
        var layers = g.Case();
        foreach (var (from, to, color) in arcs.Where(a => a.To > a.From))
            layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = scale, ["from"] = from, ["to"] = to, ["radius"] = g.R(107), ["width"] = 6, ["color"] = color });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = scale, ["every"] = minor, ["from"] = min, ["to"] = max, ["radius"] = g.R(104), ["length"] = 8, ["width"] = 1.5, ["color"] = g.InkColor });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = scale, ["every"] = major, ["from"] = min, ["to"] = max, ["radius"] = g.R(104), ["length"] = 16, ["width"] = 2.8, ["color"] = g.InkColor });
        if (reds.Length > 0)
            layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = scale, ["at"] = new JsonArray([.. reds.Select(r => (JsonNode)r)]), ["radius"] = g.R(110), ["length"] = 20, ["width"] = 4, ["color"] = GaugeParts.Red });
        layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = scale, ["every"] = labelEvery, ["from"] = min, ["to"] = max, ["divideBy"] = divideBy, ["radius"] = g.R(80), ["size"] = 21, ["color"] = g.InkColor });
        foreach (var (text, dy, size) in words)
            layers.Add(g.Text(text, 0, dy, size));

        var values = new JsonObject();
        for (var e = 1; e <= plane.Engines; e++)
        {
            var name = $"e{e}";
            var start = Math.Round(sample + (e - 1) * (max - min) * 0.05, 1);
            if (factor is double f)
            {
                values[$"{name}_raw"] = new JsonObject { ["sim"] = ClassicPack.Engine(sim, e), ["unit"] = unit, ["default"] = Math.Round(start / f, 1) };
                values[name] = new JsonObject { ["expr"] = $"{name}_raw * {f.ToString(CultureInfo.InvariantCulture)}" };
            }
            else
                values[name] = new JsonObject { ["sim"] = ClassicPack.Engine(sim, e), ["unit"] = unit, ["default"] = start };
        }

        // ITT: small amber readouts (one per engine) under the hub, like the real gauges.
        if (digital)
        {
            var n = plane.Engines;
            var (width, spacing) = n switch { 1 => (46.0, 0.0), 2 => (40.0, 48.0), 3 => (40.0, 44.0), _ => (34.0, 37.0) };
            for (var e = 1; e <= plane.Engines; e++)
            {
                var dx = (e - (n + 1) / 2.0) * spacing;   // centred under the hub
                var c = g.P(dx, 38);
                var x = (double)c[0]!; var y = (double)c[1]!;
                layers.Add(new JsonObject { ["type"] = "rect", ["x"] = Math.Round(x - width / 2, 2), ["y"] = Math.Round(y - 12, 2), ["width"] = width, ["height"] = 22, ["radius"] = 3, ["fill"] = "#0E0E0E", ["stroke"] = "#5A5A5A", ["strokeWidth"] = 1.2 });
                layers.Add(new JsonObject { ["type"] = "text", ["value"] = $"e{e}", ["format"] = digitalFormat, ["x"] = x, ["y"] = Math.Round(y + 6, 2), ["size"] = 17, ["color"] = "#F0A030" });
            }
        }

        for (var e = 1; e <= plane.Engines; e++)
            layers.Add(g.Needle(scale, $"e{e}", 92 - (e - 1) * 7, tag: plane.Engines > 1 ? e.ToString(CultureInfo.InvariantCulture) : null));
        foreach (var h in g.Hub()) layers.Add(h);
        layers.Add(g.Glass());

        var scales = new JsonObject { [scale] = new JsonObject { ["min"] = min, ["max"] = max, ["startAngle"] = -135, ["endAngle"] = 135 } };
        var needles = plane.Engines == 1 ? "one engine" : $"{plane.Engines} engines, numbered needles";
        var red = reds.Length > 0 ? $"; red line {string.Join(", ", reds)}" : "";
        return g.Gauge($"{plane.Name} {what}", $"{plane.Name} {what}: {needles}{red}. Limits: {plane.Source}.", values, scales, layers, font);
    }

    /// <summary>Engine oil: temperature (left half, °C) and pressure (right half, psi) on one dial, a needle per engine.</summary>
    public static string Oil(TurbopropProfile p, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf") =>
        OilDial(p.Name, p.Engines, p.Source, look, font, -50, 150, (p.OilTempGreenFrom, p.OilTempGreenTo), [p.OilTempRed],
            p.OilPressHighRed > 120 ? 200 : 150, (p.OilPressGreenFrom, p.OilPressGreenTo), [p.OilPressLowRed, p.OilPressHighRed]);

    /// <summary>Oil temperature (left half, °C) and pressure (right half, psi) on one dial; shared with the jet pack.</summary>
    internal static string OilDial(string planeName, int engines, string source, GaugeLook look, string font,
        double tempMin, double tempMax, (double, double) tempGreen, double[] tempReds, double pressMax, (double, double) pressGreen, double[] pressReds)
    {
        var p = (Name: planeName, Engines: engines, Source: source);
        var g = new GaugeParts(look);
        var layers = g.Case();
        var values = new JsonObject();

        void Side(string scale, JsonArray pivot, double min, double max, (double, double) green, double[] reds, double[] labels, string sim, string unit, double sample)
        {
            JsonNode Pv() => pivot.DeepClone();
            layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = scale, ["from"] = green.Item1, ["to"] = green.Item2, ["radius"] = g.R(88), ["width"] = 6, ["color"] = GaugeParts.Green, ["center"] = Pv() });
            layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = scale, ["every"] = (max - min) / 10, ["from"] = min, ["to"] = max, ["radius"] = g.R(84), ["length"] = 8, ["width"] = 1.6, ["color"] = g.InkColor, ["center"] = Pv() });
            layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = scale, ["at"] = new JsonArray([.. labels.Select(v => (JsonNode)v)]), ["radius"] = g.R(84), ["length"] = 15, ["width"] = 3, ["color"] = g.InkColor, ["center"] = Pv() });
            layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = scale, ["at"] = new JsonArray([.. reds.Select(v => (JsonNode)v)]), ["radius"] = g.R(92), ["length"] = 16, ["width"] = 3.5, ["color"] = GaugeParts.Red, ["center"] = Pv() });
            layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = scale, ["at"] = new JsonArray([.. labels.Select(v => (JsonNode)v)]), ["radius"] = g.R(60), ["size"] = 16, ["color"] = g.InkColor, ["center"] = Pv() });
            for (var e = 1; e <= p.Engines; e++)
            {
                var name = $"{scale}{e}";
                values[name] = new JsonObject { ["sim"] = ClassicPack.Engine(sim, e), ["unit"] = unit, ["default"] = Math.Round(sample + (e - 1) * (max - min) * 0.07, 1) };
                layers.Add(g.Needle(scale, name, 76 - (e - 1) * 6, pivot, p.Engines > 1 ? e.ToString(CultureInfo.InvariantCulture) : null, width: 2.4, tail: 0));
            }
            foreach (var h in g.Hub(pivot, 6)) layers.Add(h);
        }

        Side("temp", g.P(-26, 4), tempMin, tempMax, tempGreen, tempReds, [tempMin, (tempMin + tempMax) / 2, tempMax], "GENERAL ENG OIL TEMPERATURE", "celsius", 70);
        Side("press", g.P(26, 4), 0, pressMax, pressGreen, pressReds, [0, pressMax / 2.0, pressMax], "GENERAL ENG OIL PRESSURE", "psi", (pressGreen.Item1 + pressGreen.Item2) / 2);
        layers.Add(g.Text("ENG OIL", 0, -66, 15));
        layers.Add(g.Text("TEMP °C", -40, 76, 10));
        layers.Add(g.Text("PRESS PSI", 40, 76, 10));
        layers.Add(g.Glass());

        var scales = new JsonObject
        {
            ["temp"] = new JsonObject { ["min"] = tempMin, ["max"] = tempMax, ["startAngle"] = 215, ["endAngle"] = 325 },
            ["press"] = new JsonObject { ["min"] = 0, ["max"] = pressMax, ["startAngle"] = 145, ["endAngle"] = 35 },
        };
        return g.Gauge($"{p.Name} Oil", $"{p.Name} engine oil temperature (left) and pressure (right), {p.Engines} engine(s). Limits: {p.Source}.", values, scales, layers, font);
    }
}
