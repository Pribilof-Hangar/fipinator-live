// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using System.Text.Json.Nodes;

namespace Fipinator.Core;

/// <summary>
/// Round jet flight instruments (airliner and bizjet style, any <see cref="GaugeLook"/>): a drum-counter
/// altimeter and a wide-range vertical speed indicator. Stock MSFS 2024 sim values.
/// </summary>
public static class JetInstruments
{
    /// <summary>
    /// Counter-pointer altimeter: one pointer turn per 1,000 ft (20 ft marks), the altitude as digits
    /// in a counter window, baro set in both inHg and hPa. Knobs set the baro (KOHLSMAN_INC/DEC).
    /// </summary>
    public static string Altimeter(string name, GaugeLook look = GaugeLook.Black, string font = "BarlowCondensed-Medium.ttf")
    {
        var g = new GaugeParts(look);
        var layers = g.Case();
        var ink = g.InkColor;
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "k", ["every"] = 20, ["from"] = 0, ["to"] = 980, ["radius"] = g.R(104), ["length"] = 8, ["width"] = 1.5, ["color"] = ink });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "k", ["every"] = 100, ["from"] = 0, ["to"] = 900, ["radius"] = g.R(104), ["length"] = 17, ["width"] = 3, ["color"] = ink });
        layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = "k", ["every"] = 100, ["from"] = 0, ["to"] = 900, ["divideBy"] = 100, ["radius"] = g.R(76), ["size"] = 26, ["color"] = ink });

        // Counter window (thousands and hundreds, to the nearest 20 ft).
        var box = g.P(-46, -40);
        var (bx, by, bw, bh) = ((double)box[0]!, (double)box[1]!, Math.Round(92 * g.K, 1), Math.Round(30 * g.K, 1));
        layers.Add(new JsonObject { ["type"] = "rect", ["x"] = bx, ["y"] = by, ["width"] = bw, ["height"] = bh, ["radius"] = 3, ["fill"] = "#050505", ["stroke"] = "#6A6A6A", ["strokeWidth"] = 1.2 });
        layers.Add(new JsonObject { ["type"] = "text", ["value"] = "counter", ["format"] = "#,0", ["x"] = Math.Round(bx + bw - 6, 2), ["y"] = Math.Round(by + bh * 0.82, 2), ["size"] = Math.Round(bh * 0.86, 1), ["color"] = "#F4F4F2", ["align"] = "right" });
        layers.Add(g.Text("FEET", 34, -2, 10));

        // Baro windows: inHg left, hPa right.
        void Baro(double dx, string value, string format, string unit)
        {
            var p = g.P(dx, 26);
            var (x, y) = ((double)p[0]!, (double)p[1]!);
            var w = Math.Round(46 * g.K, 1);
            layers.Add(new JsonObject { ["type"] = "rect", ["x"] = Math.Round(x - w / 2, 2), ["y"] = Math.Round(y - 11, 2), ["width"] = w, ["height"] = 20, ["radius"] = 2, ["fill"] = "#050505", ["stroke"] = "#6A6A6A", ["strokeWidth"] = 1 });
            layers.Add(new JsonObject { ["type"] = "text", ["value"] = value, ["format"] = format, ["x"] = x, ["y"] = Math.Round(y + 5, 2), ["size"] = 15, ["color"] = "#F4F4F2" });
            layers.Add(g.Text(unit, dx, 44, 9));
        }
        Baro(-27, "inhg", "0.00", "IN HG");
        Baro(27, "hpa", "0", "HPA");

        layers.Add(g.Needle("k", "hundreds", 98));
        foreach (var h in g.Hub()) layers.Add(h);
        layers.Add(g.Glass());

        var values = new JsonObject
        {
            ["alt"] = new JsonObject { ["sim"] = "INDICATED ALTITUDE", ["unit"] = "feet", ["default"] = 10740 },
            ["inhg"] = new JsonObject { ["sim"] = "KOHLSMAN SETTING HG", ["unit"] = "inHg", ["default"] = 29.92 },
            ["hpa"] = new JsonObject { ["sim"] = "KOHLSMAN SETTING MB:1", ["unit"] = "millibars", ["default"] = 1013 },
            ["hundreds"] = new JsonObject { ["expr"] = "(alt % 1000 + 1000) % 1000" },
            ["counter"] = new JsonObject { ["expr"] = "alt - alt % 20" },
        };
        var scales = new JsonObject { ["k"] = new JsonObject { ["min"] = 0, ["max"] = 1000, ["startAngle"] = 0, ["endAngle"] = 360, ["wrap"] = true } };
        var json = g.Gauge(name, "Jet altimeter: pointer turns once per 1,000 ft, counter window shows the altitude, baro in inHg and hPa. Knobs set the baro.", values, scales, layers, font);
        return ClassicExtras.WithControls(json, ClassicExtras.Knobs("KOHLSMAN_INC", "KOHLSMAN_DEC"));
    }

    /// <summary>
    /// Wide-range vertical speed indicator, airliner style: zero at 9 o'clock, ±6,000 fpm with the
    /// first 1,000 fpm spread out (marks .5, 1, 2, 4, 6 thousand), UP above and DN below.
    /// </summary>
    public static string VerticalSpeed(string name, GaugeLook look = GaugeLook.Black, string font = "BarlowCondensed-Medium.ttf")
    {
        var g = new GaugeParts(look);
        var layers = g.Case();
        var ink = g.InkColor;
        var inv = CultureInfo.InvariantCulture;
        JsonArray Nums(params double[] v) => new([.. v.Select(x => (JsonNode)x)]);
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "vs", ["at"] = Nums(-900, -800, -700, -600, -400, -300, -200, -100, 100, 200, 300, 400, 600, 700, 800, 900, -1500, 1500, -3000, 3000, -5000, 5000), ["radius"] = g.R(104), ["length"] = 8, ["width"] = 1.5, ["color"] = ink });
        var majors = new double[] { -6000, -4000, -2000, -1000, -500, 0, 500, 1000, 2000, 4000, 6000 };
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "vs", ["at"] = Nums(majors), ["radius"] = g.R(104), ["length"] = 17, ["width"] = 3, ["color"] = ink });
        var texts = new JsonObject();
        foreach (var m in majors) texts[m.ToString(inv)] = m == 0 ? "0" : Math.Abs(m) == 500 ? ".5" : (Math.Abs(m) / 1000).ToString(inv);
        layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = "vs", ["at"] = Nums(majors), ["radius"] = g.R(78), ["size"] = 22, ["color"] = ink, ["texts"] = texts });
        layers.Add(g.Text("UP", -30, -24, 13));
        layers.Add(g.Text("DN", -30, 28, 13));
        layers.Add(g.Text("VERTICAL SPEED", 44, 30, 11));
        layers.Add(g.Text("1000 FT PER MIN", 44, 44, 9));
        layers.Add(g.Needle("vs", "vs", 96));
        foreach (var h in g.Hub()) layers.Add(h);
        layers.Add(g.Glass());

        JsonArray Pt(double v, double a) => new(v, a);
        var scales = new JsonObject
        {
            ["vs"] = new JsonObject
            {
                ["points"] = new JsonArray(Pt(-6000, -210), Pt(-4000, -188), Pt(-2000, -160), Pt(-1000, -132), Pt(-500, -112), Pt(0, -90),
                    Pt(500, -68), Pt(1000, -48), Pt(2000, -20), Pt(4000, 8), Pt(6000, 30)),
            },
        };
        var values = new JsonObject { ["vs"] = new JsonObject { ["sim"] = "VERTICAL SPEED", ["unit"] = "feet per minute", ["default"] = 1800 } };
        return g.Gauge(name, "Jet vertical speed: zero at 9 o'clock, up to 6,000 fpm each way, the first 1,000 fpm spread out.", values, scales, layers, font);
    }

    /// <summary>
    /// Older airliner ADI (737-200 era): blue/brown ball with pitch ladder and bank scale, orange
    /// airplane symbol, magenta flight-director crossbars (hidden when the flight director is off),
    /// glideslope pointer on the right (hidden with no glideslope), and a localizer "rising runway"
    /// at the bottom that slides with the localizer and rises over the last 200 ft of radio altitude
    /// (hidden with no localizer). Directions of the flight-director bars and the glideslope pointer
    /// still need a check in flight on an ILS.
    /// </summary>
    public static string Adi(string name, GaugeLook look = GaugeLook.Black, string font = "BarlowCondensed-Medium.ttf")
    {
        var g = new GaugeParts(look);
        var layers = g.Case();
        const string white = "#F4F4F2", sky = "#2F86CC", ground = "#7A5230", orange = "#FF8C1A", magenta = "#E651D8", runway = "#E8D24A";
        var ball = g.R(90);

        JsonObject Rect(double x, double y, double w, double h, string fill, double radius = 0) =>
            new() { ["type"] = "rect", ["x"] = Math.Round(x, 2), ["y"] = Math.Round(y, 2), ["width"] = Math.Round(w, 2), ["height"] = Math.Round(h, 2), ["radius"] = radius, ["fill"] = fill };
        JsonObject Group(JsonObject how, params JsonNode?[] children)
        {
            how["type"] = "group";
            how["layers"] = new JsonArray([.. children.Select(c => c?.Parent is null ? c : c.DeepClone())]);
            return how;
        }
        JsonObject Move(string axis, string value, params JsonNode?[] children) =>
            Group(new JsonObject { [axis] = new JsonObject { ["value"] = value, ["scale"] = "px" } }, children);

        // The ball: sky/ground and ladder move with pitch (3 px per degree) and turn with bank.
        var horizon = new JsonArray(Rect(-400, -900, 1120, 1020, sky), Rect(-400, 120, 1120, 1020, ground), Rect(-400, 119, 1120, 2, white));
        var ladder = new JsonArray();
        foreach (var p in new[] { -20, -10, -5, 5, 10, 20 })
        {
            var y = 120 - p * 3.0;
            var w = Math.Abs(p) == 5 ? 26 : 56;
            ladder.Add(Rect(160 - w / 2.0, y - 1, w, 2, white));
            if (Math.Abs(p) != 5)
            {
                ladder.Add(new JsonObject { ["type"] = "text", ["text"] = Math.Abs(p).ToString(CultureInfo.InvariantCulture), ["x"] = 126, ["y"] = y + 5, ["size"] = 13, ["color"] = white, ["align"] = "right" });
                ladder.Add(new JsonObject { ["type"] = "text", ["text"] = Math.Abs(p).ToString(CultureInfo.InvariantCulture), ["x"] = 194, ["y"] = y + 5, ["size"] = 13, ["color"] = white, ["align"] = "left" });
            }
        }
        layers.Add(Group(new JsonObject { ["clip"] = ball },
            Group(new JsonObject { ["rotate"] = new JsonObject { ["value"] = "bank", ["scale"] = "bank" } },
                Group(new JsonObject { ["moveY"] = new JsonObject { ["value"] = "pitch", ["scale"] = "pitch" } }, [.. horizon]),
                Group(new JsonObject { ["clip"] = Math.Round(ball * 0.68, 2) }, Group(new JsonObject { ["moveY"] = new JsonObject { ["value"] = "pitch", ["scale"] = "pitch" } }, [.. ladder])),
                new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(0), ["radius"] = ball - 2, ["length"] = -12, ["width"] = 13, ["color"] = white, ["shape"] = "triangle" })));

        // Fixed bank scale on the ring around the ball.
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(10, 20, 340, 350), ["radius"] = g.R(101), ["length"] = 8, ["width"] = 2, ["color"] = white });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(30, 60, 300, 330), ["radius"] = g.R(101), ["length"] = 11, ["width"] = 3, ["color"] = white });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(45, 315), ["radius"] = g.R(101), ["length"] = 8, ["width"] = 8, ["color"] = white, ["shape"] = "triangle" });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(0), ["radius"] = g.R(102), ["length"] = 12, ["width"] = 14, ["color"] = white, ["shape"] = "triangle" });

        // Glideslope scale (right): two dots each way, pointer moves with NAV GSI.
        var gx = 160 + ball * 0.8;
        foreach (var d in new[] { -2, -1, 1, 2 })
            layers.Add(new JsonObject { ["type"] = "circle", ["center"] = new JsonArray(Math.Round(gx, 2), 120 + d * 17), ["r"] = 3.2, ["stroke"] = white, ["strokeWidth"] = 1.5 });
        layers.Add(Rect(gx - 7, 119, 14, 2, white));
        layers.Add(Move("moveX", "gs_hide", Move("moveY", "gs_y",
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(90), ["radius"] = Math.Round(gx - 160 + 9, 2), ["length"] = 12, ["width"] = 11, ["color"] = runway, ["shape"] = "triangle" })));

        // Localizer: dots along the bottom and the rising runway.
        var ly = 120 + ball * 0.74;
        foreach (var d in new[] { -2, -1, 1, 2 })
            layers.Add(new JsonObject { ["type"] = "circle", ["center"] = new JsonArray(160 + d * 20, Math.Round(ly + 8, 2)), ["r"] = 3.2, ["stroke"] = white, ["strokeWidth"] = 1.5 });
        layers.Add(Move("moveX", "loc_hide", Move("moveX", "loc_x", Move("moveY", "rise",
            Rect(147, ly - 4, 26, 8, runway, 1.5),
            Rect(158.5, ly - 4, 3, 8, ground)))));

        // Flight-director crossbars, then the fixed airplane symbol on top.
        layers.Add(Move("moveX", "fd_hide",
            Move("moveY", "fd_y", Rect(110, 118, 100, 3.5, magenta, 1.5)),
            Move("moveX", "fd_x", Rect(158.2, 70, 3.5, 100, magenta, 1.5))));
        foreach (var r in new[] { Rect(104, 117, 38, 6, orange, 2), Rect(136, 117, 6, 14, orange, 2), Rect(178, 117, 38, 6, orange, 2), Rect(178, 117, 6, 14, orange, 2), Rect(156, 116, 8, 8, orange, 2) })
        {
            r["stroke"] = "#000000";
            r["strokeWidth"] = 1;
            layers.Add(r);
        }
        layers.Add(g.Text("GS", ball * 0.8 / g.K, -58, 10));
        layers.Add(g.Glass());

        var values = new JsonObject
        {
            ["pitch"] = new JsonObject { ["sim"] = "ATTITUDE INDICATOR PITCH DEGREES", ["unit"] = "degrees", ["default"] = -3 }, // nose up is negative in MSFS
            ["bank"] = new JsonObject { ["sim"] = "ATTITUDE INDICATOR BANK DEGREES", ["unit"] = "degrees", ["default"] = 8 },
            ["fdp"] = new JsonObject { ["sim"] = "AUTOPILOT FLIGHT DIRECTOR PITCH", ["unit"] = "degrees", ["default"] = -5 },
            ["fdb"] = new JsonObject { ["sim"] = "AUTOPILOT FLIGHT DIRECTOR BANK", ["unit"] = "degrees", ["default"] = 4 },
            ["fdon"] = new JsonObject { ["sim"] = "AUTOPILOT FLIGHT DIRECTOR ACTIVE", ["unit"] = "bool", ["default"] = 1 },
            ["gsi"] = new JsonObject { ["sim"] = "NAV GSI:1", ["unit"] = "number", ["default"] = 30 },
            ["cdi"] = new JsonObject { ["sim"] = "NAV CDI:1", ["unit"] = "number", ["default"] = -25 },
            ["hasgs"] = new JsonObject { ["sim"] = "NAV HAS GLIDE SLOPE:1", ["unit"] = "bool", ["default"] = 1 },
            ["hasloc"] = new JsonObject { ["sim"] = "NAV HAS LOCALIZER:1", ["unit"] = "bool", ["default"] = 1 },
            ["ra"] = new JsonObject { ["sim"] = "RADIO HEIGHT", ["unit"] = "feet", ["default"] = 900 },
            ["fd_y"] = new JsonObject { ["expr"] = "clamp(-(fdp - pitch) * 3, -40, 40)" },
            ["fd_x"] = new JsonObject { ["expr"] = "clamp((fdb - bank) * 1.5, -40, 40)" },
            ["fd_hide"] = new JsonObject { ["expr"] = "choose(fdon, 0, 400)" },
            ["gs_y"] = new JsonObject { ["expr"] = "clamp(gsi / 127 * 34, -38, 38)" }, // positive = pointer down, like the ILS and HSI gauges (727 HSI checked against the cockpit on a glideslope, 2026-11-12)
            ["gs_hide"] = new JsonObject { ["expr"] = "choose(hasgs, 0, 400)" },
            ["loc_x"] = new JsonObject { ["expr"] = "clamp(cdi / 127 * 40, -46, 46)" }, // MSFS: a positive needle value means the course is to the RIGHT (sim +18.6 while the DC-6's own course indicator said fly right, 2026-11-12)
            ["rise"] = new JsonObject { ["expr"] = "-clamp((200 - ra) / 200, 0, 1) * 30" },
            ["loc_hide"] = new JsonObject { ["expr"] = "choose(hasloc, 0, 400)" },
        };
        var scales = new JsonObject
        {
            ["bank"] = new JsonObject { ["min"] = -180, ["max"] = 180, ["startAngle"] = -180, ["endAngle"] = 180 },
            ["pitch"] = new JsonObject { ["min"] = -90, ["max"] = 90, ["startAngle"] = 270, ["endAngle"] = -270 }, // MSFS reports nose up as negative pitch (checked in flight 2026-11-11): nose up moves the horizon down
            ["dial"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = 360, ["wrap"] = true },
            ["px"] = new JsonObject { ["min"] = -400, ["max"] = 400, ["startAngle"] = -400, ["endAngle"] = 400 },
        };
        return g.Gauge(name, "Airliner ADI, 737-200 style: attitude ball, flight-director crossbars, glideslope pointer, localizer rising runway (rises below 200 ft radio altitude).", values, scales, layers, font);
    }

    /// <summary>
    /// The HSI with two digital windows above it, as on older airliner HSIs: DME distance (MILES,
    /// NAV DME:1) on the left and the selected COURSE (NAV OBS:1) on the right. The dial is the
    /// regular HSI, shrunk a little to make room.
    /// </summary>
    public static string HsiWithWindows(string name, GaugeLook look = GaugeLook.Black, string font = "BarlowCondensed-Medium.ttf")
    {
        const double zoom = 0.84;
        var gauge = JsonNode.Parse(HsiGenerator.Create(name, look, font, readouts: false))!.AsObject(); // it has its own COURSE window
        var old = gauge["layers"]!.AsArray();
        var inner = new JsonArray([.. old.Select(l => l!.DeepClone())]);
        var layers = new JsonArray
        {
            // Dial moved down 12 px and shrunk around its centre: centre (160,120) -> (160,132).
            new JsonObject { ["type"] = "group", ["offset"] = new JsonArray(Math.Round(160 * (1 - zoom), 2), Math.Round(120 * (1 - zoom) + 12, 2)), ["zoom"] = zoom, ["layers"] = inner },
        };
        void Window(double x, string value, string label)
        {
            layers.Add(new JsonObject { ["type"] = "rect", ["x"] = x, ["y"] = 4, ["width"] = 62, ["height"] = 28, ["radius"] = 3, ["fill"] = "#050505", ["stroke"] = "#6A6A6A", ["strokeWidth"] = 1.2 });
            layers.Add(new JsonObject { ["type"] = "text", ["value"] = value, ["format"] = "000", ["x"] = x + 31, ["y"] = 27, ["size"] = 24, ["color"] = "#F4F4F2" });
            layers.Add(new JsonObject { ["type"] = "text", ["text"] = label, ["x"] = x + 31, ["y"] = 44, ["size"] = 11, ["color"] = "#E6E6E4" });
        }
        Window(14, "dme", "MILES");
        Window(244, "obs", "COURSE");
        gauge["layers"] = layers;
        var values = gauge["values"]!.AsObject();
        values["dme"] = new JsonObject { ["sim"] = "NAV DME:1", ["unit"] = "nautical miles", ["default"] = 12 };
        gauge["description"] = "HSI with DME MILES and COURSE windows above the dial, like older airliner HSIs.";
        return gauge.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}
