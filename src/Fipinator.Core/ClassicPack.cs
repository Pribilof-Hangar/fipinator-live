// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Text.Json.Nodes;

namespace Fipinator.Core;

/// <summary>
/// What the classic pack needs to know about a round-gauge piston aircraft. Most of it comes from
/// the sim (engine count, design speeds); the rest from a handbook or sensible defaults.
/// </summary>
public sealed record AircraftProfile(
    string Name,
    int Engines,
    bool HasManifoldPressure,
    AircraftSpeeds Speeds,
    double TankGallons,
    double MaxRpm = 2700,
    double RpmGreenFrom = 2100,
    double RpmGreenTo = 2700,
    double ManifoldMax = 35,
    double ManifoldGreenFrom = 15,
    double ManifoldGreenTo = 25,
    double ManifoldRedLine = 0);

/// <summary>
/// The classic 10-pack for round-gauge piston aircraft, fitted to one aircraft: airspeed,
/// attitude, altimeter, turn coordinator, heading, vertical speed, RPM, manifold pressure (only
/// if the engines have it), oil temperature + pressure, fuel. Engine gauges get one needle per
/// engine (1, 2 or 4), numbered when there's more than one. Listed in priority order, so with
/// fewer FIPs the first ones go on screen and the rest on pages.
/// </summary>
public static class ClassicPack
{
    /// <summary>
    /// Typical aircraft for the generic panels, until the pack fills itself in from the loaded plane:
    /// <list type="bullet">
    /// <item>1 engine: a Cessna 152-type trainer (handbook speeds 35 / 40 / 111 / 149 kt, flaps 85 kt,
    /// 13 gal per tank, 2550 RPM red line); no manifold pressure (fixed-pitch propeller).</item>
    /// <item>2 engines: the Cessna 404 Titan's speeds as the owner's sim reported them (70 / 83 /
    /// 241 kt; green to about 210 as on its dial; flaps about 150); engine limits are typical values.</item>
    /// </list>
    /// </summary>
    public static AircraftProfile Typical(int engines) => engines switch
    {
        1 => new("Classic single", 1, false, new AircraftSpeeds(35, 40, 111, 149, 85), 13, MaxRpm: 2550, RpmGreenFrom: 1900, RpmGreenTo: 2550),
        2 => new("Classic twin", 2, true, new AircraftSpeeds(70, 83, 210, 241, 150, 83, 105), 85, MaxRpm: 2300, RpmGreenFrom: 1800, RpmGreenTo: 2300,
            ManifoldMax: 45, ManifoldGreenFrom: 20, ManifoldGreenTo: 35, ManifoldRedLine: 40),
        // No four-engine set (owner, 2026-11-11): the DC-6 is MSFS 2024's four-piston-engine airliner and has its own set.
        _ => throw new ArgumentException("The ready-made classic sets are for 1 or 2 engines (four engines: use the DC-6 set).", nameof(engines)),
    };

    /// <param name="sources">Gauge files reused (re-cased in the chosen look): "attitude", "altimeter", "vertical-speed".</param>
    public static IReadOnlyList<(string File, string Json)> Create(AircraftProfile plane, GaugeLook look, IReadOnlyDictionary<string, string> sources, string font = "BarlowCondensed-Medium.ttf")
    {
        if (plane.Engines is not (1 or 2 or 4)) throw new ArgumentException("The classic pack covers 1, 2 or 4 engines.", nameof(plane));
        var list = new List<(string, string)>
        {
            ("airspeed", Recase(AirspeedGenerator.Create(plane.Speeds, $"{plane.Name} Airspeed", font), look)),
            ("attitude", WithAdjustableBars(GaugeParts.Reskin(sources["attitude"], look, $"{plane.Name} Attitude"))),
            ("altimeter", GaugeParts.Reskin(sources["altimeter"], look, $"{plane.Name} Altimeter")),
            ("turn-coordinator", TurnCoordinatorGenerator.Create($"{plane.Name} Turn coordinator", look, font)),
            ("heading", HeadingGenerator.Create($"{plane.Name} Heading", look, font)),
            ("vertical-speed", GaugeParts.Reskin(sources["vertical-speed"], look, $"{plane.Name} Vertical speed")),
            ("rpm", EngineDialGenerator.Rpm(plane, look, font)),
        };
        if (plane.HasManifoldPressure) list.Add(("manifold-pressure", EngineDialGenerator.ManifoldPressure(plane, look, font)));
        list.Add(("oil", OilGenerator.Create(plane, look, font)));
        list.Add(("fuel", FuelGenerator.Create(plane.TankGallons, $"{plane.Name} Fuel", look, font)));
        // Alternatives for the turn and heading FIPs (classic twins often have these instead).
        list.Add(("turn-and-slip", TurnAndSlipGenerator.Create($"{plane.Name} Turn and slip", look, font)));
        list.Add(("hsi", HsiGenerator.Create($"{plane.Name} HSI", look, font)));
        // More engine pages and other instruments.
        list.Add(("fuel-flow", EngineDialGenerator.FuelFlow(plane, look, font)));
        list.Add(("egt", EngineDialGenerator.Egt(plane, look, font)));
        list.Add(("radar-altimeter", ClassicExtras.RadarAltimeter($"{plane.Name} Radar altimeter", look, font)));
        list.Add(("clock", ClassicExtras.Clock($"{plane.Name} Clock", look, font)));
        list.Add(("adf", ClassicExtras.Adf($"{plane.Name} ADF", look, font)));
        return list;
    }

    /// <summary>
    /// Lets the attitude's yellow airplane symbol move up and down with ATTITUDE BARS POSITION, set by
    /// the knobs (ATTITUDE_BARS_POSITION_UP/DOWN), as on a real attitude indicator.
    /// </summary>
    private static string WithAdjustableBars(string json)
    {
        var gauge = JsonNode.Parse(json)!.AsObject();
        var dial = gauge["layers"]!.AsArray().OfType<JsonObject>().FirstOrDefault(l => l["zoom"] is not null);
        if (dial?["layers"] is not JsonArray inner) return json;
        bool IsPlane(JsonObject l) => l["fill"]?.GetValue<string>() is { } f && f.Equals("#FFE01A", StringComparison.OrdinalIgnoreCase)
            && l["type"]?.GetValue<string>() is "rect" or "circle";
        var plane = inner.OfType<JsonObject>().Where(IsPlane).ToList();
        if (plane.Count == 0) return json;
        var at = inner.IndexOf(plane[0]);
        foreach (var p in plane) inner.Remove(p);
        inner.Insert(at, new JsonObject
        {
            ["type"] = "group", ["moveY"] = new JsonObject { ["value"] = "bars", ["scale"] = "barspx" },
            ["layers"] = new JsonArray([.. plane]),
        });
        gauge["values"]!.AsObject()["bars"] = new JsonObject { ["sim"] = "ATTITUDE BARS POSITION", ["unit"] = "percent", ["default"] = 0 };
        // Higher setting, symbol higher (up on the screen is a smaller y).
        gauge["scales"]!.AsObject()["barspx"] = new JsonObject { ["min"] = -100, ["max"] = 100, ["startAngle"] = 20, ["endAngle"] = -20 };
        return ClassicExtras.WithControls(gauge.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            ClassicExtras.Knobs("ATTITUDE_BARS_POSITION_UP", "ATTITUDE_BARS_POSITION_DOWN", fast: 5));
    }

    private static string Recase(string json, GaugeLook look) => look == GaugeLook.LightTwin ? json : GaugeParts.Reskin(json, look);

    /// <summary>"Engine 1" style suffix for sim values: ":1", ":2"...</summary>
    internal static string Engine(string sim, int engine) => $"{sim}:{engine}";
}

/// <summary>Directional gyro: a compass card that turns under a fixed lubber line and airplane.</summary>
public static class HeadingGenerator
{
    public static string Create(string name, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf")
    {
        var g = new GaugeParts(look);
        var layers = g.Case();
        var card = new JsonArray
        {
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["every"] = 5, ["from"] = 0, ["to"] = 355, ["radius"] = g.R(104), ["length"] = 7, ["width"] = 1.4, ["color"] = g.InkColor },
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["every"] = 10, ["from"] = 0, ["to"] = 350, ["radius"] = g.R(104), ["length"] = 14, ["width"] = 2.6, ["color"] = g.InkColor },
            new JsonObject
            {
                ["type"] = "labels", ["scale"] = "dial", ["every"] = 30, ["from"] = 0, ["to"] = 330, ["radius"] = g.R(78), ["size"] = 22, ["color"] = g.InkColor, ["orient"] = "radial",
                ["texts"] = new JsonObject { ["0"] = "N", ["30"] = "3", ["60"] = "6", ["90"] = "E", ["120"] = "12", ["150"] = "15", ["180"] = "S", ["210"] = "21", ["240"] = "24", ["270"] = "W", ["300"] = "30", ["330"] = "33" },
            },
        };
        // Heading bug on the card (left knob), turned to the autopilot heading.
        card.Add(new JsonObject
        {
            ["type"] = "group", ["rotate"] = new JsonObject { ["value"] = "bug", ["scale"] = "dial" },
            ["layers"] = new JsonArray(new JsonObject { ["type"] = "rect", ["x"] = Math.Round(160 - 8 * g.K, 2), ["y"] = Math.Round(120 - 111 * g.K, 2), ["width"] = Math.Round(16 * g.K, 2), ["height"] = Math.Round(9 * g.K, 2), ["radius"] = 1, ["fill"] = "#E8862A" }),
        });
        layers.Add(new JsonObject { ["type"] = "group", ["rotate"] = new JsonObject { ["value"] = "hdg", ["scale"] = "card" }, ["layers"] = card });

        // Fixed: orange lubber mark at the top, small marks every 45 degrees, the airplane.
        const string orange = "#E8862A";
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(45, 90, 135, 180, 225, 270, 315), ["radius"] = g.R(112), ["length"] = 8, ["width"] = 9, ["shape"] = "triangle", ["color"] = g.InkColor });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(0), ["radius"] = g.R(112), ["length"] = 13, ["width"] = 14, ["shape"] = "triangle", ["color"] = orange });
        double X(double dx) => 160 + dx * g.K;
        double Y(double dy) => 120 + dy * g.K;
        JsonObject Rect(double dx, double dy, double w, double h) => new()
        {
            ["type"] = "rect", ["x"] = Math.Round(X(dx), 2), ["y"] = Math.Round(Y(dy), 2), ["width"] = Math.Round(w * g.K, 2), ["height"] = Math.Round(h * g.K, 2), ["radius"] = 2, ["fill"] = orange, ["stroke"] = "#3A1E00", ["strokeWidth"] = 0.8,
        };
        layers.Add(Rect(-3, -32, 6, 60));   // fuselage
        layers.Add(Rect(-36, -8, 72, 7));   // wings
        layers.Add(Rect(-13, 20, 26, 5));   // tail
        layers.Add(g.Glass());

        var values = new JsonObject
        {
            ["hdg"] = new JsonObject { ["sim"] = "HEADING INDICATOR", ["unit"] = "degrees", ["default"] = 40 },
            ["bug"] = new JsonObject { ["sim"] = "AUTOPILOT HEADING LOCK DIR", ["unit"] = "degrees", ["default"] = 70 },
        };
        var scales = new JsonObject
        {
            ["card"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = -360, ["wrap"] = true },
            ["dial"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = 360, ["wrap"] = true },
        };
        // Right knob lines the gyro up with the compass (it drifts); left knob sets the heading bug.
        return ClassicExtras.WithControls(
            g.Gauge(name, "Directional gyro: compass card under a fixed lubber line and airplane, heading bug. Right knob: align with the compass. Left knob: heading bug.", values, scales, layers, font),
            ("rightKnobClockwise", "GYRO_DRIFT_INC", 1), ("rightKnobAnticlockwise", "GYRO_DRIFT_DEC", 1),
            ("leftKnobClockwise", "HEADING_BUG_INC", 1), ("leftKnobAnticlockwise", "HEADING_BUG_DEC", 1));
    }
}

/// <summary>Turn coordinator: a small airplane that banks with the rate of turn, and the slip ball.</summary>
public static class TurnCoordinatorGenerator
{
    private const double StandardRate = 0.05236; // 3 degrees per second, in radians per second

    public static string Create(string name, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf")
    {
        var g = new GaugeParts(look);
        var layers = g.Case();
        var values = new JsonObject
        {
            ["turn"] = new JsonObject { ["sim"] = "TURN INDICATOR RATE", ["unit"] = "radians per second", ["default"] = StandardRate },
            ["ball"] = new JsonObject { ["sim"] = "TURN COORDINATOR BALL", ["unit"] = "position", ["default"] = 0 },
        };
        var scales = new JsonObject
        {
            ["dial"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = 360, ["wrap"] = true },
            ["turn"] = new JsonObject { ["min"] = -2 * StandardRate, ["max"] = 2 * StandardRate, ["startAngle"] = -40, ["endAngle"] = 40 },
        };

        // Wings-level marks at 9 and 3 o'clock; standard-rate (2 minute) marks 20 degrees below them.
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(90, 270, 110, 250), ["radius"] = g.R(106), ["length"] = 20, ["width"] = 4.5, ["color"] = g.InkColor });
        layers.Add(new JsonObject
        {
            ["type"] = "labels", ["scale"] = "dial", ["at"] = new JsonArray(250, 110), ["radius"] = g.R(76), ["size"] = 20, ["color"] = g.InkColor,
            ["texts"] = new JsonObject { ["250"] = "L", ["110"] = "R" },
        });
        layers.Add(g.Text("TURN COORDINATOR", 0, -62, 12));
        layers.Add(g.Text("NO PITCH INFORMATION", 0, -46, 8));
        layers.Add(g.Text("2 MIN", 0, 40, 11));

        BallTube.Add(g, layers, values, scales);

        // The airplane, seen from behind: wings, body and fin, banking with the turn rate.
        double X(double dx) => Math.Round(160 + dx * g.K, 2);
        double Y(double dy) => Math.Round(120 + dy * g.K, 2);
        const string white = "#F2EFE6";
        var plane = new JsonArray
        {
            new JsonObject { ["type"] = "rect", ["x"] = X(-66), ["y"] = Y(-3), ["width"] = Math.Round(132 * g.K, 2), ["height"] = Math.Round(6 * g.K, 2), ["radius"] = 3, ["fill"] = white, ["stroke"] = "#000000", ["strokeWidth"] = 0.8 },
            new JsonObject { ["type"] = "rect", ["x"] = X(-3), ["y"] = Y(-24), ["width"] = Math.Round(6 * g.K, 2), ["height"] = Math.Round(22 * g.K, 2), ["radius"] = 2, ["fill"] = white, ["stroke"] = "#000000", ["strokeWidth"] = 0.8 },
            new JsonObject { ["type"] = "circle", ["r"] = Math.Round(11 * g.K, 2), ["fill"] = white, ["stroke"] = "#000000", ["strokeWidth"] = 0.8 },
            new JsonObject { ["type"] = "circle", ["r"] = Math.Round(4 * g.K, 2), ["fill"] = "#2A2B2C" },
        };
        layers.Add(new JsonObject { ["type"] = "group", ["rotate"] = new JsonObject { ["value"] = "turn", ["scale"] = "turn" }, ["layers"] = plane });
        layers.Add(g.Glass());
        return g.Gauge(name, "Turn coordinator: airplane symbol banks with the rate of turn (marks at a 2-minute turn), slip ball below.", values, scales, layers, font);
    }
}

/// <summary>
/// Needle-type turn and slip, as in classic twins like the Cessna 404: a broad white needle standing
/// up from a low pivot, standard-rate ("doghouse") marks, L / R, and a white ball in a dark window.
/// </summary>
public static class TurnAndSlipGenerator
{
    private const double StandardRate = 0.05236; // 3 degrees per second, in radians per second

    public static string Create(string name, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf")
    {
        var g = new GaugeParts(look);
        var layers = g.Case();
        var pivot = g.P(0, 12);
        double X(double dx) => Math.Round(160 + dx * g.K, 2);
        double Y(double dy) => Math.Round(120 + dy * g.K, 2);
        double S(double v) => Math.Round(v * g.K, 2);

        // Standard-rate marks: a little "doghouse" (square with a peak) each side, a bar in the middle.
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "turn", ["at"] = new JsonArray(-StandardRate, StandardRate), ["radius"] = g.R(80), ["length"] = -12, ["width"] = 16, ["shape"] = "triangle", ["color"] = g.InkColor, ["center"] = pivot.DeepClone() });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "turn", ["at"] = new JsonArray(-StandardRate, StandardRate), ["radius"] = g.R(80), ["length"] = 10, ["width"] = 15, ["color"] = g.InkColor, ["center"] = pivot.DeepClone() });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "turn", ["at"] = new JsonArray(0), ["radius"] = g.R(92), ["length"] = 14, ["width"] = 14, ["color"] = g.InkColor, ["center"] = pivot.DeepClone() });
        layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = "turn", ["from"] = -2 * StandardRate, ["to"] = 2 * StandardRate, ["radius"] = g.R(48), ["width"] = 2, ["color"] = "#5A5A5A", ["center"] = pivot.DeepClone() });
        layers.Add(g.Text("L", -60, -60, 18, g.InkColor));
        layers.Add(g.Text("R", 60, -60, 18, g.InkColor));
        layers.Add(g.Text("Y/D", -70, -6, 11));
        layers.Add(g.Text("GYRO", 70, -6, 11));
        layers.Add(g.Text("D.C. ELEC.", 0, 32, 9));
        layers.Add(g.Text("TURN AND SLIP", 0, 48, 14));

        // The slip window: dark glass, two white lines, a white ball that slides with the slip.
        layers.Add(new JsonObject { ["type"] = "rect", ["x"] = X(-42), ["y"] = Y(56), ["width"] = S(84), ["height"] = S(20), ["radius"] = 5, ["fill"] = "#141414", ["stroke"] = "#3A3A3A", ["strokeWidth"] = 1 });
        foreach (var dx in new[] { -11.0, 11.0 })
            layers.Add(new JsonObject { ["type"] = "rect", ["x"] = X(dx - 1), ["y"] = Y(57), ["width"] = S(2.2), ["height"] = S(18), ["fill"] = g.InkColor });
        layers.Add(new JsonObject
        {
            ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = "ball", ["scale"] = "ballpx" },
            ["layers"] = new JsonArray(new JsonObject { ["type"] = "circle", ["r"] = S(8.5), ["center"] = new JsonArray(X(0), Y(66)), ["fillTop"] = "#FFFFFF", ["fillBottom"] = "#BDBDB6", ["stroke"] = "#000000", ["strokeWidth"] = 0.8 }),
        });

        layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "turn", ["value"] = "turn", ["length"] = g.R(76), ["tail"] = 0, ["width"] = 7, ["color"] = "#F2EFE6", ["shadow"] = true, ["center"] = pivot.DeepClone() });
        foreach (var h in g.Hub(pivot, 9)) layers.Add(h);
        layers.Add(g.Glass());

        var values = new JsonObject
        {
            ["turn"] = new JsonObject { ["sim"] = "TURN INDICATOR RATE", ["unit"] = "radians per second", ["default"] = StandardRate },
            ["ball"] = new JsonObject { ["sim"] = "TURN COORDINATOR BALL", ["unit"] = "position", ["default"] = -30 },
        };
        var scales = new JsonObject
        {
            ["turn"] = new JsonObject { ["min"] = -2 * StandardRate, ["max"] = 2 * StandardRate, ["startAngle"] = -40, ["endAngle"] = 40 },
            // The ball slides sideways: a "scale" whose angle is a distance in pixels.
            ["ballpx"] = new JsonObject { ["min"] = -127, ["max"] = 127, ["startAngle"] = -S(30), ["endAngle"] = S(30) },
        };
        return g.Gauge(name, "Needle-type turn and slip: turn needle with standard-rate marks, white ball in a dark window.", values, scales, layers, font);
    }
}

/// <summary>
/// Horizontal situation indicator: a compass card turning with heading, an orange heading bug, the
/// yellow course arrow with its deviation bar, TO/FROM, glideslope pointers and a NAV flag, as on
/// classic twins. Right knob turns the course (OBS), left knob the heading bug.
/// </summary>
public static class HsiGenerator
{
    /// <param name="readouts">CRS and HDG digits in small boxes in the top corners (off for an HSI that has its own windows).</param>
    public static string Create(string name, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf", bool readouts = true)
    {
        var g = new GaugeParts(look);
        var layers = g.Case();
        const string yellow = "#E8D23A", orange = "#E8862A";
        double X(double dx) => Math.Round(160 + dx * g.K, 2);
        double Y(double dy) => Math.Round(120 + dy * g.K, 2);
        double S(double v) => Math.Round(v * g.K, 2);
        JsonObject Rect(double dx, double dy, double w, double h, string fill, double radius = 0) => new()
        {
            ["type"] = "rect", ["x"] = X(dx), ["y"] = Y(dy), ["width"] = S(w), ["height"] = S(h), ["radius"] = radius, ["fill"] = fill,
        };
        JsonObject Group(string value, string scale, string how, params JsonObject[] inside) => new()
        {
            ["type"] = "group", [how] = new JsonObject { ["value"] = value, ["scale"] = scale }, ["layers"] = new JsonArray([.. inside]),
        };

        // The course arrow, drawn pointing up; its group turns to the course (inside the card).
        var cdiBar = Group("cdi", "cdipx", "moveX", Rect(-2.5, -36, 5, 72, yellow));
        var to = Group("to_shift", "px", "moveX", new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(0), ["radius"] = g.R(20), ["length"] = -12, ["width"] = 12, ["shape"] = "triangle", ["color"] = g.InkColor });
        var from = Group("from_shift", "px", "moveX", new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(180), ["radius"] = g.R(20), ["length"] = -12, ["width"] = 12, ["shape"] = "triangle", ["color"] = g.InkColor });
        var course = Group("obs", "dial", "rotate",
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(0), ["radius"] = g.R(76), ["length"] = -22, ["width"] = 18, ["shape"] = "triangle", ["color"] = yellow },
            Rect(-2.5, -78, 5, 40, yellow),
            Rect(-2.5, 38, 5, 54, yellow),
            new JsonObject { ["type"] = "circle", ["r"] = 2.6, ["center"] = new JsonArray(X(-40), Y(0)), ["fill"] = g.InkColor },
            new JsonObject { ["type"] = "circle", ["r"] = 2.6, ["center"] = new JsonArray(X(-20), Y(0)), ["fill"] = g.InkColor },
            new JsonObject { ["type"] = "circle", ["r"] = 2.6, ["center"] = new JsonArray(X(20), Y(0)), ["fill"] = g.InkColor },
            new JsonObject { ["type"] = "circle", ["r"] = 2.6, ["center"] = new JsonArray(X(40), Y(0)), ["fill"] = g.InkColor },
            to, from, cdiBar);
        var bug = Group("bug", "dial", "rotate", Rect(-8, -111, 16, 9, orange, 1));
        var card = Group("hdg", "card", "rotate",
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["every"] = 5, ["from"] = 0, ["to"] = 355, ["radius"] = g.R(104), ["length"] = 7, ["width"] = 1.4, ["color"] = g.InkColor },
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["every"] = 10, ["from"] = 0, ["to"] = 350, ["radius"] = g.R(104), ["length"] = 13, ["width"] = 2.4, ["color"] = g.InkColor },
            new JsonObject
            {
                ["type"] = "labels", ["scale"] = "dial", ["every"] = 30, ["from"] = 0, ["to"] = 330, ["radius"] = g.R(83), ["size"] = 18, ["color"] = g.InkColor, ["orient"] = "radial",
                ["texts"] = new JsonObject { ["0"] = "N", ["30"] = "3", ["60"] = "6", ["90"] = "E", ["120"] = "12", ["150"] = "15", ["180"] = "S", ["210"] = "21", ["240"] = "24", ["270"] = "W", ["300"] = "30", ["330"] = "33" },
            },
            bug, course);
        layers.Add(card);

        // Fixed: lubber line, 45-degree marks, glideslope scale and pointer, NAV flag, the airplane.
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(0), ["radius"] = g.R(112), ["length"] = 14, ["width"] = 12, ["shape"] = "triangle", ["color"] = g.InkColor });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(45, 90, 135, 180, 225, 270, 315), ["radius"] = g.R(112), ["length"] = 8, ["width"] = 8, ["shape"] = "triangle", ["color"] = g.InkColor });
        // Glideslope: a scale on each side of the case, as on real HSIs (two dots above and two below a centre
        // mark), in the free space beside the round case; a big pointer on each, moving together, hidden with no
        // glideslope signal.
        const double gsDot = 26, stripTop = 120 - 2 * gsDot - 14, stripHeight = 4 * gsDot + 28;
        foreach (var (x, at) in new[] { (19.0, 270), (301.0, 90) })
        {
            layers.Add(new JsonObject { ["type"] = "rect", ["x"] = x - 15, ["y"] = stripTop, ["width"] = 30, ["height"] = stripHeight, ["radius"] = 4, ["fill"] = "#121212", ["stroke"] = "#5A5A5A", ["strokeWidth"] = 1.2 });
            foreach (var dy in new[] { -2, -1, 1, 2 })
                layers.Add(new JsonObject { ["type"] = "circle", ["r"] = 3.4, ["center"] = new JsonArray(x, 120 + dy * gsDot), ["stroke"] = "#F2F2EE", ["strokeWidth"] = 1.6 });
            layers.Add(new JsonObject { ["type"] = "rect", ["x"] = x - 10, ["y"] = 118.75, ["width"] = 20, ["height"] = 2.5, ["fill"] = "#F2F2EE" });
            // The pointer: a triangle pointing at the dial, drawn round the gauge's centre at the side.
            layers.Add(new JsonObject
            {
                ["type"] = "group", ["moveY"] = new JsonObject { ["value"] = "gsi", ["scale"] = "gsipx" },
                ["layers"] = new JsonArray(Group("gs_hide", "px", "moveX", new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(at), ["radius"] = Math.Abs(x - 160) + 9, ["length"] = 18, ["width"] = 16, ["shape"] = "triangle", ["color"] = yellow })),
            });
        }
        if (readouts)
        {
            // Course and heading-bug digits in small boxes in the top corners (clear of the screws).
            foreach (var (x, value, label) in new[] { (4.0, "obs", "CRS"), (266.0, "bug", "HDG") })
            {
                layers.Add(new JsonObject { ["type"] = "rect", ["x"] = x, ["y"] = 3, ["width"] = 50, ["height"] = 25, ["radius"] = 3, ["fill"] = "#050505", ["stroke"] = "#6A6A6A", ["strokeWidth"] = 1.2 });
                layers.Add(new JsonObject { ["type"] = "text", ["value"] = value, ["format"] = "000", ["x"] = x + 25, ["y"] = 23, ["size"] = 21, ["color"] = value == "bug" ? orange : yellow });
                layers.Add(new JsonObject { ["type"] = "text", ["text"] = label, ["x"] = x + 25, ["y"] = 41, ["size"] = 12, ["color"] = "#E6E6E4" });
            }
        }
        layers.Add(Group("nav_flag_shift", "px", "moveX",
            Rect(-78, -62, 36, 15, "#C8321E", 2),
            new JsonObject { ["type"] = "text", ["text"] = "NAV", ["x"] = X(-60), ["y"] = Y(-50.5), ["size"] = 12, ["color"] = "#FFFFFF" }));
        layers.Add(Rect(-3, -22, 6, 40, orange, 2));   // fuselage
        layers.Add(Rect(-24, -6, 48, 6, orange, 2));   // wings
        layers.Add(Rect(-10, 14, 20, 4, orange, 1.5)); // tail
        layers.Add(g.Glass());

        var values = new JsonObject
        {
            ["hdg"] = new JsonObject { ["sim"] = "PLANE HEADING DEGREES MAGNETIC", ["unit"] = "degrees", ["default"] = 300 },
            ["bug"] = new JsonObject { ["sim"] = "AUTOPILOT HEADING LOCK DIR", ["unit"] = "degrees", ["default"] = 280 },
            ["obs"] = new JsonObject { ["sim"] = "NAV OBS:1", ["unit"] = "degrees", ["default"] = 240 },
            ["cdi"] = new JsonObject { ["sim"] = "HSI CDI NEEDLE", ["unit"] = "number", ["default"] = 40 },
            ["gsi"] = new JsonObject { ["sim"] = "HSI GSI NEEDLE", ["unit"] = "number", ["default"] = -30 },
            ["has_nav"] = new JsonObject { ["sim"] = "NAV HAS NAV:1", ["unit"] = "bool", ["default"] = 1 },
            ["has_gs"] = new JsonObject { ["sim"] = "NAV HAS GLIDE SLOPE:1", ["unit"] = "bool", ["default"] = 1 },
            ["tofrom"] = new JsonObject { ["sim"] = "NAV TOFROM:1", ["unit"] = "number", ["default"] = 1 },
            ["gps_drives"] = new JsonObject { ["sim"] = "GPS DRIVES NAV1", ["unit"] = "bool", ["default"] = 0 },
            ["nav_flag_shift"] = new JsonObject { ["expr"] = "choose(has_nav + gps_drives, 400, 0)" },
            ["gs_hide"] = new JsonObject { ["expr"] = "choose(has_gs, 0, 400)" },
            ["to_shift"] = new JsonObject { ["expr"] = "choose(0.5 - abs(tofrom - 1), 0, 400)" },
            ["from_shift"] = new JsonObject { ["expr"] = "choose(0.5 - abs(tofrom - 2), 0, 400)" },
        };
        var scales = new JsonObject
        {
            ["card"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = -360, ["wrap"] = true },
            ["dial"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = 360, ["wrap"] = true },
            ["cdipx"] = new JsonObject { ["min"] = -127, ["max"] = 127, ["startAngle"] = -S(45), ["endAngle"] = S(45) }, // MSFS: a positive needle value means the course is to the RIGHT (needle right): sim reading +18.6 while the DC-6's own course indicator said fly right, 2026-11-12. (A 2026-11-11 change had reversed it; that was wrong.)
            // Below the glideslope (negative) the pointer goes up, as on the DC-6 course/ILS gauge; full scale is
            // the outer dot.
            ["gsipx"] = new JsonObject { ["min"] = -127, ["max"] = 127, ["startAngle"] = -2 * gsDot, ["endAngle"] = 2 * gsDot },
            ["px"] = new JsonObject { ["min"] = -1000, ["max"] = 1000, ["startAngle"] = -1000, ["endAngle"] = 1000 },
        };
        var json = System.Text.Json.Nodes.JsonNode.Parse(g.Gauge(name, "HSI: compass card, heading bug, course arrow with deviation bar, TO/FROM, glideslope scales on both sides, NAV flag" + (readouts ? ", CRS and HDG readouts" : "") + ". Right knob: course. Left knob: heading bug.", values, scales, layers, font))!.AsObject();
        json["controls"] = new JsonObject
        {
            ["rightKnobClockwise"] = new JsonObject { ["event"] = "VOR1_OBI_INC" },
            ["rightKnobAnticlockwise"] = new JsonObject { ["event"] = "VOR1_OBI_DEC" },
            ["leftKnobClockwise"] = new JsonObject { ["event"] = "HEADING_BUG_INC" },
            ["leftKnobAnticlockwise"] = new JsonObject { ["event"] = "HEADING_BUG_DEC" },
        };
        return json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}

/// <summary>The slip ball in its curved glass tube, shared by the turn and slip and the turn coordinator.</summary>
internal static class BallTube
{
    public static void Add(GaugeParts g, JsonArray layers, JsonObject values, JsonObject scales)
    {
        var tubeCenter = g.P(0, -68); // the tube is a slice of a big circle centred above the dial
        var tubeRadius = g.R(136);
        const double halfAngle = 20;
        var (cx, cy) = (tubeCenter[0]!.GetValue<double>(), tubeCenter[1]!.GetValue<double>());
        foreach (var (width, color) in new[] { (24.0, "#3A3A36"), (19.0, "#CFC7A6") })
        {
            layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = "tube", ["from"] = -1, ["to"] = 1, ["radius"] = tubeRadius, ["width"] = width, ["color"] = color, ["center"] = tubeCenter.DeepClone() });
            foreach (var side in new[] { -1, 1 })
            {
                var a = halfAngle * Math.PI / 180 * side;
                layers.Add(new JsonObject { ["type"] = "circle", ["r"] = width / 2, ["fill"] = color, ["center"] = new JsonArray(Math.Round(cx + tubeRadius * Math.Sin(a), 2), Math.Round(cy + tubeRadius * Math.Cos(a), 2)) });
            }
        }
        layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = "tube", ["from"] = -0.9, ["to"] = 0.9, ["radius"] = tubeRadius - 6, ["width"] = 2.5, ["color"] = "#E9E3CB", ["center"] = tubeCenter.DeepClone() });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "tube", ["at"] = new JsonArray(-0.27, 0.27), ["radius"] = tubeRadius + 12, ["length"] = 24, ["width"] = 2.2, ["color"] = "#1A1A1A", ["center"] = tubeCenter.DeepClone() });
        layers.Add(new JsonObject
        {
            ["type"] = "group", ["center"] = tubeCenter.DeepClone(), ["rotate"] = new JsonObject { ["value"] = "ball", ["scale"] = "ball" },
            ["layers"] = new JsonArray(new JsonObject { ["type"] = "circle", ["r"] = 8.5, ["center"] = new JsonArray(cx, Math.Round(cy + tubeRadius, 2)), ["fillTop"] = "#3A3A3A", ["fillBottom"] = "#050505", ["stroke"] = "#000000", ["strokeWidth"] = 1 }),
        });
        scales["tube"] = new JsonObject { ["min"] = -1, ["max"] = 1, ["startAngle"] = 180 + halfAngle, ["endAngle"] = 180 - halfAngle };
        scales["ball"] = new JsonObject { ["min"] = -127, ["max"] = 127, ["startAngle"] = 16, ["endAngle"] = -16 };
    }
}

/// <summary>Round engine dials with one needle per engine: RPM and manifold pressure.</summary>
public static class EngineDialGenerator
{
    public static string Rpm(AircraftProfile plane, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf")
    {
        var max = Math.Ceiling((plane.MaxRpm + 300) / 500) * 500;
        return Dial(plane, look, font, $"{plane.Name} RPM", "rpm", "GENERAL ENG RPM", "rpm",
            min: 0, max: max, minor: 100, major: 500, labelEvery: 500, divideBy: 100,
            green: (plane.RpmGreenFrom, plane.RpmGreenTo), reds: [plane.MaxRpm],
            words: [("RPM", 50, 16), ("X 100", 64, 10)], sample: plane.RpmGreenFrom);
    }

    public static string ManifoldPressure(AircraftProfile plane, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf")
    {
        var red = plane.ManifoldRedLine > 0 ? plane.ManifoldRedLine : plane.ManifoldMax;
        return Dial(plane, look, font, $"{plane.Name} Manifold pressure", "mp", "ENG MANIFOLD PRESSURE", "inHg",
            min: 10, max: plane.ManifoldMax, minor: 1, major: 5, labelEvery: 5, divideBy: 1,
            green: (plane.ManifoldGreenFrom, plane.ManifoldGreenTo), reds: [red],
            words: [("MANIFOLD", 44, 12), ("PRESSURE", 57, 12), ("IN. HG", 70, 9)], sample: plane.ManifoldGreenTo);
    }

    /// <summary>Fuel flow in US gallons per hour, a needle per engine (ENG FUEL FLOW GPH:n).</summary>
    public static string FuelFlow(AircraftProfile plane, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf")
    {
        var max = plane.Engines >= 4 ? 200 : plane.HasManifoldPressure ? 40 : 20;
        var step = max / 8.0;
        return Dial(plane, look, font, $"{plane.Name} Fuel flow", "ff", "ENG FUEL FLOW GPH", "gallons per hour",
            min: 0, max: max, minor: step / 2, major: step, labelEvery: step * 2, divideBy: 1,
            green: (max * 0.15, max * 0.8), reds: [],
            words: [("FUEL FLOW", 46, 13), ("GAL / HR", 60, 9)], sample: max * 0.45);
    }

    /// <summary>Exhaust gas temperature in degrees F, a needle per engine (ENG EXHAUST GAS TEMPERATURE:n).</summary>
    public static string Egt(AircraftProfile plane, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf") =>
        Dial(plane, look, font, $"{plane.Name} EGT", "egt", "ENG EXHAUST GAS TEMPERATURE", "fahrenheit",
            min: 800, max: 1700, minor: 25, major: 100, labelEvery: 100, divideBy: 100,
            green: (0, 0), reds: [1650],
            words: [("EGT", 46, 15), ("°F X 100", 60, 9)], sample: 1350);

    private static string Dial(AircraftProfile plane, GaugeLook look, string font, string name, string scale, string sim, string unit,
        double min, double max, double minor, double major, double labelEvery, double divideBy,
        (double From, double To) green, double[] reds, (string Text, double Dy, double Size)[] words, double sample)
    {
        var g = new GaugeParts(look);
        var layers = g.Case();
        if (green.To > green.From)
            layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = scale, ["from"] = green.From, ["to"] = green.To, ["radius"] = g.R(107), ["width"] = 6, ["color"] = GaugeParts.Green });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = scale, ["every"] = minor, ["from"] = min, ["to"] = max, ["radius"] = g.R(104), ["length"] = 8, ["width"] = 1.5, ["color"] = g.InkColor });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = scale, ["every"] = major, ["from"] = min, ["to"] = max, ["radius"] = g.R(104), ["length"] = 16, ["width"] = 2.8, ["color"] = g.InkColor });
        if (reds.Length > 0)
            layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = scale, ["at"] = new JsonArray([.. reds.Select(r => (JsonNode)r)]), ["radius"] = g.R(110), ["length"] = 20, ["width"] = 4, ["color"] = GaugeParts.Red });
        layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = scale, ["every"] = labelEvery, ["from"] = Math.Ceiling(min / labelEvery) * labelEvery, ["to"] = max, ["divideBy"] = divideBy, ["radius"] = g.R(80), ["size"] = 21, ["color"] = g.InkColor });
        foreach (var (text, dy, size) in words) layers.Add(g.Text(text, 0, dy, size));

        var values = new JsonObject();
        for (var e = 1; e <= plane.Engines; e++)
        {
            values[$"e{e}"] = new JsonObject { ["sim"] = ClassicPack.Engine(sim, e), ["unit"] = unit, ["default"] = Math.Round(sample + (e - 1) * (max - min) * 0.05, 1) };
            layers.Add(g.Needle(scale, $"e{e}", 92 - (e - 1) * 7, tag: plane.Engines > 1 ? e.ToString(System.Globalization.CultureInfo.InvariantCulture) : null));
        }
        foreach (var h in g.Hub()) layers.Add(h);
        layers.Add(g.Glass());

        var scales = new JsonObject { [scale] = new JsonObject { ["min"] = min, ["max"] = max, ["startAngle"] = -135, ["endAngle"] = 135 } };
        var engines = plane.Engines == 1 ? "one engine" : $"{plane.Engines} engines, numbered needles";
        var marks = (green.To > green.From ? $"; green {green.From}-{green.To}" : "") + (reds.Length > 0 ? $"; red line {string.Join(", ", reds)}" : "");
        return g.Gauge(name, $"{name}: {engines}{marks}.", values, scales, layers, font);
    }
}

/// <summary>Oil temperature (left half) and oil pressure (right half) on one dial, a needle per engine on each.</summary>
public static class OilGenerator
{
    public static string Create(AircraftProfile plane, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf")
    {
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
            for (var e = 1; e <= plane.Engines; e++)
            {
                var name = $"{scale}{e}";
                values[name] = new JsonObject { ["sim"] = ClassicPack.Engine(sim, e), ["unit"] = unit, ["default"] = Math.Round(sample + (e - 1) * (max - min) * 0.07, 1) };
                layers.Add(g.Needle(scale, name, 76 - (e - 1) * 6, pivot, plane.Engines > 1 ? e.ToString(System.Globalization.CultureInfo.InvariantCulture) : null, width: 2.4, tail: 0));
            }
            foreach (var h in g.Hub(pivot, 6)) layers.Add(h);
        }

        Side("temp", g.P(-26, 4), 50, 250, (100, 240), [245], [50, 150, 250], "GENERAL ENG OIL TEMPERATURE", "fahrenheit", 180);
        Side("press", g.P(26, 4), 0, 120, (60, 90), [25, 115], [0, 60, 120], "GENERAL ENG OIL PRESSURE", "psi", 75);
        layers.Add(g.Text("OIL", 0, -66, 17));
        layers.Add(g.Text("TEMP °F", -40, 76, 10));
        layers.Add(g.Text("PRESS PSI", 40, 76, 10));
        layers.Add(g.Glass());

        var scales = new JsonObject
        {
            ["temp"] = new JsonObject { ["min"] = 50, ["max"] = 250, ["startAngle"] = 215, ["endAngle"] = 325 },
            ["press"] = new JsonObject { ["min"] = 0, ["max"] = 120, ["startAngle"] = 145, ["endAngle"] = 35 },
        };
        return g.Gauge($"{plane.Name} Oil", $"Oil temperature (left) and oil pressure (right), {plane.Engines} engine(s).", values, scales, layers, font);
    }
}
