// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fipinator.Core;

/// <summary>
/// Ready-made templates for the gauges FIP users ask for most: gear, flaps, trim, autopilot settings,
/// radios, transponder, warning lights, lights switches, wind, and a few small classics, plus 737
/// extras. All stock MSFS 2024 sim values and events (names checked against the SDK docs, 2026-11-10).
/// Panels with several settings use knob modes: the S buttons pick what the knobs set, and the screen
/// highlights the choice. Labels on the left edge sit next to the FIP's S1-S6 buttons.
/// </summary>
public static class InstrumentTemplates
{
    private const string Bg = "#0B0C0E", Box = "#17191C", Edge = "#33373C", Ink = "#E8E8E6", Dim = "#5D636B", Label = "#9AA0A6",
        Green = "#3BE07A", Amber = "#F5A623", Red = "#FF3B30", Cyan = "#7FD6F0";
    private static string N(double v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Every template: (group folder, file name, gauge json).</summary>
    public static IReadOnlyList<(string Group, string File, string Json)> All(string font = "BarlowCondensed-Medium.ttf") =>
    [
        ("gear-flaps", "gear-lights", GearLights(font)),
        ("gear-flaps", "flaps", ClassicExtras.WithControls(FlapsGenerator.Create(40, [10, 20, 30, 40], "Flaps (generic)", GaugeLook.Classic, font), FlapKnobs)),
        ("boeing-style", "flaps", Flaps737(font)),
        ("gear-flaps", "trim", Trim(font)),
        ("autopilot-radios", "autopilot", Autopilot(font)),
        ("autopilot-radios", "radios", Radios(font)),
        ("autopilot-radios", "transponder", Transponder(font)),
        ("warnings-lights", "annunciators", Annunciators(font)),
        ("warnings-lights", "lights", LightsPanel(font)),
        ("weather-small", "wind", Wind(font)),
        ("weather-small", "suction", Dial("Suction", "SUCTION PRESSURE", "inHg", 0, 10, [0, 2, 4, 6, 8, 10], (4.5, 5.5), [], ["SUCTION", "IN HG"], 5, font)),
        ("weather-small", "g-meter", Dial("G-meter", "G FORCE", "GForce", -3, 7, [-2, 0, 2, 4, 6], (0, 0), [], ["G"], 1, font)),
        ("boeing-style", "autobrake", Autobrake(font)),
        ("airbus-style", "engines", MiniPanels.JetEngines(JetPack.AirbusA320neo, font, airbus: true)),
        ("airbus-style", "flaps", AirbusFlaps(font)),
        ("airbus-style", "gear", AirbusGear(font)),
        ("airbus-style", "fcu", AirbusFcu(font)),
        ("airbus-style", "autobrake", AirbusAutobrake(font)),
        ("airbus-style", "speedbrake", AirbusSpeedbrake(font)),
    ];

    private static readonly (string, string, int)[] FlapKnobs =
        [("rightKnobClockwise", "FLAPS_INCR", 1), ("rightKnobAnticlockwise", "FLAPS_DECR", 1), ("leftKnobClockwise", "FLAPS_INCR", 1), ("leftKnobAnticlockwise", "FLAPS_DECR", 1)];

    // ---------- Gear and flaps ----------

    /// <summary>Gear lights: NOSE, LEFT, RIGHT boxes green DOWN / red IN TRANSIT / dark UP, and the handle. S1 moves the gear.</summary>
    public static string GearLights(string font = "BarlowCondensed-Medium.ttf")
    {
        var p = new Page(font);
        p.SideLabel(1, "GEAR");
        foreach (var (key, sim, label, x, y) in new[] { ("nose", "GEAR CENTER POSITION", "NOSE", 115.0, 30.0), ("left", "GEAR LEFT POSITION", "LEFT", 60.0, 112.0), ("right", "GEAR RIGHT POSITION", "RIGHT", 170.0, 112.0) })
        {
            p.Value(key, sim, "percent over 100", key == "left" ? 0.5 : 1);
            p.Expr($"{key}_down", $"choose({key} - 0.99, 1, 0)");
            p.Expr($"{key}_moving", $"choose({key} - 0.01, choose(0.99 - {key}, 1, 0), 0)");
            p.Rect(x, y, 90, 56, Box, Edge);
            p.Text(label, x + 45, y + 18, 13, Label);
            p.Lamp($"{key}_down", x + 4, y + 24, 82, 28, Green, "DOWN");
            p.Lamp($"{key}_moving", x + 4, y + 24, 82, 28, Red, "IN TRANSIT");
            p.Show($"{key}_up_shown", $"choose({key} - 0.01, 0, 1)", new JsonObject { ["type"] = "text", ["text"] = "UP", ["x"] = x + 45, ["y"] = y + 45, ["size"] = 18, ["color"] = Dim });
        }
        p.Value("handle", "GEAR HANDLE POSITION", "percent over 100", 1);
        p.Text("HANDLE", 160, 198, 13, Label);
        p.Show("handle_dn", "choose(handle - 0.5, 1, 0)", new JsonObject { ["type"] = "text", ["text"] = "DOWN", ["x"] = 160, ["y"] = 222, ["size"] = 20, ["color"] = Ink });
        p.Show("handle_up", "choose(handle - 0.5, 0, 1)", new JsonObject { ["type"] = "text", ["text"] = "UP", ["x"] = 160, ["y"] = 222, ["size"] = 20, ["color"] = Ink });
        p.Control("button1", "GEAR_TOGGLE");
        return p.Gauge("Landing gear", "Gear lights per leg (green DOWN, red IN TRANSIT, dark UP) and the gear handle. S1 moves the gear (GEAR_TOGGLE).");
    }

    /// <summary>737-style flaps gauge: left and right needles on the 0-1-2-5-10-15-25-30-40 scale. Knobs move the flap lever.</summary>
    public static string Flaps737(string font = "BarlowCondensed-Medium.ttf")
    {
        var g = new GaugeParts(GaugeLook.Black);
        var layers = g.Case();
        double[] marks = [0, 1, 2, 5, 10, 15, 25, 30, 40];
        double[] angles = [-140, -112, -88, -60, -30, 0, 36, 70, 116];
        var points = new JsonArray([.. marks.Zip(angles, (v, a) => (JsonNode)new JsonArray(v, a))]);
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "f", ["at"] = new JsonArray([.. marks.Select(m => (JsonNode)m)]), ["radius"] = g.R(104), ["length"] = 16, ["width"] = 3, ["color"] = g.InkColor });
        var texts = new JsonObject(); foreach (var m in marks) texts[N(m)] = N(m);
        layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = "f", ["at"] = new JsonArray([.. marks.Select(m => (JsonNode)m)]), ["radius"] = g.R(78), ["size"] = 20, ["color"] = g.InkColor, ["texts"] = texts });
        layers.Add(g.Text("FLAPS", 0, 40, 16));
        layers.Add(g.Needle("f", "left", 90, tag: "L"));
        layers.Add(g.Needle("f", "right", 84, tag: "R"));
        foreach (var h in g.Hub()) layers.Add(h);
        layers.Add(g.Glass());
        var values = new JsonObject
        {
            ["left"] = new JsonObject { ["sim"] = "TRAILING EDGE FLAPS LEFT ANGLE", ["unit"] = "degrees", ["default"] = 15 },
            ["right"] = new JsonObject { ["sim"] = "TRAILING EDGE FLAPS RIGHT ANGLE", ["unit"] = "degrees", ["default"] = 15 },
        };
        var scales = new JsonObject { ["f"] = new JsonObject { ["points"] = points } };
        var json = g.Gauge("Flaps (Boeing style)", "Boeing-style (737) flap position: left (L) and right (R) needles, marks 0, 1, 2, 5, 10, 15, 25, 30, 40. Knobs move the flap lever.", values, scales, layers, font);
        return ClassicExtras.WithControls(json, FlapKnobs);
    }

    /// <summary>Trim: elevator, rudder and aileron trim positions. S1-S3 pick which one the knobs set (left knob faster).</summary>
    public static string Trim(string font = "BarlowCondensed-Medium.ttf")
    {
        var p = new Page(font);
        p.Value("elev", "ELEVATOR TRIM PCT", "percent over 100", 0.15);
        p.Value("rud", "RUDDER TRIM PCT", "percent over 100", -0.1);
        p.Value("ail", "AILERON TRIM PCT", "percent over 100", 0.05);
        p.Expr("elev_y", "clamp(-elev, -1, 1) * 80");
        p.Expr("rud_x", "clamp(rud, -1, 1) * 60");
        p.Expr("ail_x", "clamp(ail, -1, 1) * 60");
        p.Modes(["ELEV", "RUDDER", "AILERON"]);

        // Elevator: a vertical scale, NOSE UP at the top.
        p.Selectable(1, 50, 8, 92, 224);
        p.Text("ELEVATOR", 96, 26, 13, Label);
        p.Rect(92, 40, 8, 164, Box, Edge);
        p.Rect(80, 121, 32, 2, Label);
        p.Text("UP", 120, 50, 12, Label, "left");
        p.Text("DN", 120, 202, 12, Label, "left");
        p.Move("moveY", "elev_y", p.Pointer(96, 122, "right"));
        // Rudder and aileron: horizontal scales.
        foreach (var (mode, label, value, y) in new[] { (2, "RUDDER", "rud_x", 66.0), (3, "AILERON", "ail_x", 160.0) })
        {
            p.Selectable(mode, 152, y - 50, 162, 92);
            p.Text(label, 233, y - 30, 13, Label);
            p.Rect(170, y - 4, 126, 8, Box, Edge);
            p.Rect(232, y - 14, 2, 28, Label);
            p.Text("L", 166, y + 30, 12, Label);
            p.Text("R", 300, y + 30, 12, Label);
            p.Move("moveX", value, p.Pointer(233, y, "down"));
        }
        p.ModeControls(1, "ELEV_TRIM_UP", "ELEV_TRIM_DN", 10);
        p.ModeControls(2, "RUDDER_TRIM_RIGHT", "RUDDER_TRIM_LEFT", 10);
        p.ModeControls(3, "AILERON_TRIM_RIGHT", "AILERON_TRIM_LEFT", 10);
        return p.Gauge("Trim", "Elevator, rudder and aileron trim. S1-S3 pick which trim the knobs set (right knob fine, left knob faster).");
    }

    // ---------- Autopilot and radios ----------

    /// <summary>Autopilot settings: SPD, HDG, ALT, V/S. S1-S4 pick which one the knobs set; S6 autopilot on/off.</summary>
    public static string Autopilot(string font = "BarlowCondensed-Medium.ttf")
    {
        var p = new Page(font);
        p.Value("spd", "AUTOPILOT AIRSPEED HOLD VAR", "knots", 250);
        p.Value("hdg", "AUTOPILOT HEADING LOCK DIR", "degrees", 270);
        p.Value("alt", "AUTOPILOT ALTITUDE LOCK VAR", "feet", 12000);
        p.Value("vs", "AUTOPILOT VERTICAL HOLD VAR", "feet per minute", -800);
        p.Value("ap", "AUTOPILOT MASTER", "bool", 1);
        p.Modes(["SPD", "HDG", "ALT", "V/S"]);
        var rows = new[] { ("spd", "SPD", "0", "KT"), ("hdg", "HDG", "000", "°"), ("alt", "ALT", "#,0", "FT"), ("vs", "V/S", "+#,0;-#,0;0", "FPM") };
        for (var i = 0; i < rows.Length; i++)
        {
            var (value, name, format, unit) = rows[i];
            var y = 4 + i * 40.0;
            p.Selectable(i + 1, 50, y, 264, 36);
            p.Text(name, 62, y + 26, 18, Label, "left");
            p.Rect(130, y + 3, 132, 30, "#000000", Edge);
            p.ValueText(value, format, 256, y + 28, 26, Ink, "right");
            p.Text(unit, 268, y + 26, 13, Label, "left");
        }
        p.SideLabel(6, "AP");
        p.Rect(130, 200, 132, 34, Box, Edge);
        p.Lamp("ap", 134, 204, 124, 26, Green, "AUTOPILOT ON");
        p.Show("ap_off", "choose(ap - 0.5, 0, 1)", new JsonObject { ["type"] = "text", ["text"] = "AUTOPILOT OFF", ["x"] = 196, ["y"] = 224, ["size"] = 15, ["color"] = Dim });
        p.ModeControls(1, "AP_SPD_VAR_INC", "AP_SPD_VAR_DEC", 10);
        p.ModeControls(2, "HEADING_BUG_INC", "HEADING_BUG_DEC", 10);
        p.ModeControls(3, "AP_ALT_VAR_INC", "AP_ALT_VAR_DEC", 10);
        p.ModeControls(4, "AP_VS_VAR_INC", "AP_VS_VAR_DEC", 5);
        p.Control("button6", "AP_MASTER");
        p.Light("button6", "ap");
        return p.Gauge("Autopilot settings", "Autopilot speed, heading, altitude and vertical speed. S1-S4 pick which one the knobs set (left knob faster); S6 turns the autopilot on or off.");
    }

    /// <summary>COM1 and NAV1, active and standby. S1/S2 pick the radio the knobs tune (left MHz, right kHz); S3/S4 swap.</summary>
    public static string Radios(string font = "BarlowCondensed-Medium.ttf")
    {
        var p = new Page(font);
        p.Value("com", "COM ACTIVE FREQUENCY:1", "MHz", 118.7);
        p.Value("comsb", "COM STANDBY FREQUENCY:1", "MHz", 121.5);
        p.Value("nav", "NAV ACTIVE FREQUENCY:1", "MHz", 110.5);
        p.Value("navsb", "NAV STANDBY FREQUENCY:1", "MHz", 113.9);
        p.Modes(["COM1", "NAV1"]);
        p.SideLabel(3, "SW COM");
        p.SideLabel(4, "SW NAV");
        foreach (var (mode, name, active, standby, format, y) in new[] { (1, "COM 1", "com", "comsb", "0.000", 6.0), (2, "NAV 1", "nav", "navsb", "0.00", 120.0) })
        {
            p.Selectable(mode, 50, y, 264, 104);
            p.Text(name, 64, y + 22, 16, Label, "left");
            p.Text("ACTIVE", 134, y + 22, 11, Label, "left");
            p.ValueText(active, format, 306, y + 60, 38, Green, "right");
            p.Text("STANDBY", 134, y + 88, 11, Label, "left");
            p.ValueText(standby, format, 306, y + 96, 24, Ink, "right");
        }
        p.ModeControls(1, "COM_RADIO_FRACT_INC", "COM_RADIO_FRACT_DEC", 1, "COM_RADIO_WHOLE_INC", "COM_RADIO_WHOLE_DEC");
        p.ModeControls(2, "NAV1_RADIO_FRACT_INC", "NAV1_RADIO_FRACT_DEC", 1, "NAV1_RADIO_WHOLE_INC", "NAV1_RADIO_WHOLE_DEC");
        p.Control("button3", "COM_STBY_RADIO_SWAP");
        p.Control("button4", "NAV1_RADIO_SWAP");
        return p.Gauge("Radios (COM1 / NAV1)", "COM1 and NAV1 active and standby frequencies. S1/S2 pick the radio the knobs tune (left knob MHz, right knob kHz); S3/S4 swap active and standby.");
    }

    /// <summary>Transponder code and mode. S1 picks the first two digits for the knobs, S2 the last two (left knob left digit).</summary>
    public static string Transponder(string font = "BarlowCondensed-Medium.ttf")
    {
        var p = new Page(font);
        p.Value("code", "TRANSPONDER CODE:1", "Bco16", 4608); // 0x1200: the code in packed octal digits
        p.Value("state", "TRANSPONDER STATE:1", "number", 4);
        p.Expr("d1", "(code - code % 4096) / 4096 % 16");
        p.Expr("d2", "(code % 4096 - code % 256) / 256");
        p.Expr("d3", "(code % 256 - code % 16) / 16");
        p.Expr("d4", "code % 16");
        p.Modes(["first two digits", "last two digits"], ["12··", "··34"]);
        p.Text("TRANSPONDER", 182, 30, 15, Label);
        p.Rect(90, 52, 186, 86, "#000000", Edge);
        for (var i = 0; i < 4; i++) p.ValueText($"d{i + 1}", "0", 120 + i * 42, 122, 64, Green, "center");
        p.Selectable(1, 96, 140, 84, 8, solid: true);
        p.Selectable(2, 180, 140, 84, 8, solid: true);
        string[] states = ["OFF", "STBY", "TEST", "ON", "ALT", "GND"];
        for (var s = 0; s < states.Length; s++)
            p.Show($"state_{s}", $"choose(abs(state - {s}) - 0.5, 0, 1)", new JsonObject { ["type"] = "text", ["text"] = states[s], ["x"] = 182, ["y"] = 196, ["size"] = 30, ["color"] = s >= 3 ? Green : Amber });
        p.Text("MODE", 182, 222, 12, Label);
        p.ModeControls(1, "XPNDR_100_INC", "XPNDR_100_DEC", 1, "XPNDR_1000_INC", "XPNDR_1000_DEC");
        p.ModeControls(2, "XPNDR_1_INC", "XPNDR_1_DEC", 1, "XPNDR_10_INC", "XPNDR_10_DEC");
        return p.Gauge("Transponder", "Transponder code and mode. S1 picks the first two digits, S2 the last two: the left knob sets the left digit of the pair, the right knob the right one.");
    }

    // ---------- Warnings and lights ----------

    /// <summary>Warning lights; S1/S2 acknowledge master warning/caution, S3 parking brake (their lights follow).</summary>
    public static string Annunciators(string font = "BarlowCondensed-Medium.ttf")
    {
        var p = new Page(font);
        var lights = new[]
        {
            ("warn", "MASTER WARNING ACTIVE", "MASTER WARN", Red), ("caut", "MASTER CAUTION ACTIVE", "MASTER CAUT", Amber), ("park", "BRAKE PARKING POSITION", "PARK BRAKE", Amber),
            ("fuel", "WARNING FUEL", "LOW FUEL", Amber), ("oil", "WARNING OIL PRESSURE", "OIL PRESS", Red), ("vac", "WARNING VACUUM", "LOW VACUUM", Amber),
            ("volt", "WARNING VOLTAGE", "LOW VOLTS", Amber), ("hgt", "WARNING LOW HEIGHT", "LOW HEIGHT", Amber), ("ap", "AUTOPILOT MASTER", "AUTOPILOT", Green),
        };
        p.SideLabel(1, "WARN");
        p.SideLabel(2, "CAUT");
        p.SideLabel(3, "PARK");
        for (var i = 0; i < lights.Length; i++)
        {
            var (key, sim, text, color) = lights[i];
            p.Value(key, sim, "bool", i is 1 or 2 or 8 ? 1 : 0);
            var (x, y) = (54 + i % 3 * 88.0, 14 + i / 3 * 74.0);
            p.Rect(x, y, 82, 64, Box, Edge);
            p.Text(text, x + 41, y + 38, 13, Dim);
            p.Lamp(key, x + 3, y + 3, 76, 58, color, text, textColor: "#101010");
        }
        p.Control("button1", "MASTER_WARNING_ACKNOWLEDGE");
        p.Control("button2", "MASTER_CAUTION_ACKNOWLEDGE");
        p.Control("button3", "PARKING_BRAKES");
        p.Light("button1", "warn");
        p.Light("button2", "caut");
        p.Light("button3", "park");
        return p.Gauge("Warning lights", "Master warning and caution, parking brake, low fuel, oil pressure, vacuum, volts, low height and autopilot lights. S1/S2 acknowledge master warning/caution, S3 sets the parking brake; their button lights follow.");
    }

    /// <summary>Lights switches on S1-S6: landing, taxi, nav, beacon, strobe, logo. Each button light shows its light.</summary>
    public static string LightsPanel(string font = "BarlowCondensed-Medium.ttf")
    {
        var p = new Page(font);
        var rows = new[]
        {
            ("landing", "LIGHT LANDING", "LANDING", "LANDING_LIGHTS_TOGGLE"), ("taxi", "LIGHT TAXI", "TAXI", "TOGGLE_TAXI_LIGHTS"), ("nav", "LIGHT NAV", "NAV", "TOGGLE_NAV_LIGHTS"),
            ("beacon", "LIGHT BEACON", "BEACON", "TOGGLE_BEACON_LIGHTS"), ("strobe", "LIGHT STROBE", "STROBE", "STROBES_TOGGLE"), ("logo", "LIGHT LOGO", "LOGO", "TOGGLE_LOGO_LIGHTS"),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            var (key, sim, name, ev) = rows[i];
            p.Value(key, sim, "bool", i is 2 or 3 ? 1 : 0);
            var y = 2 + i * 40.0;
            p.Rect(50, y, 264, 36, Box, Edge);
            p.Text(name, 62, y + 25, 18, Ink, "left");
            // Toggle switch: lever up when on.
            p.Rect(200, y + 6, 14, 24, "#2A2D31", Edge);
            p.Show($"{key}_up", $"choose({key} - 0.5, 1, 0)", new JsonObject { ["type"] = "rect", ["x"] = 202, ["y"] = y + 7, ["width"] = 10, ["height"] = 11, ["radius"] = 2, ["fill"] = Ink });
            p.Show($"{key}_dn", $"choose({key} - 0.5, 0, 1)", new JsonObject { ["type"] = "rect", ["x"] = 202, ["y"] = y + 18, ["width"] = 10, ["height"] = 11, ["radius"] = 2, ["fill"] = Ink });
            p.Lamp(key, 236, y + 6, 70, 24, Green, "ON", textColor: "#101010");
            p.Show($"{key}_off", $"choose({key} - 0.5, 0, 1)", new JsonObject { ["type"] = "text", ["text"] = "OFF", ["x"] = 271, ["y"] = y + 24, ["size"] = 16, ["color"] = Dim });
            p.Control($"button{i + 1}", ev);
            p.Light($"button{i + 1}", key);
        }
        return p.Gauge("Lights switches", "Landing, taxi, nav, beacon, strobe and logo lights on S1-S6: press to switch, the button light shows when it's on.");
    }

    // ---------- Weather and small gauges ----------

    /// <summary>Wind arrow relative to the nose (where the wind blows to), wind from/speed, outside temperature, ground speed.</summary>
    public static string Wind(string font = "BarlowCondensed-Medium.ttf")
    {
        var p = new Page(font);
        p.Value("wdir", "AMBIENT WIND DIRECTION", "degrees", 240);
        p.Value("wspd", "AMBIENT WIND VELOCITY", "knots", 18);
        p.Value("hdg", "PLANE HEADING DEGREES TRUE", "degrees", 300);
        p.Value("oat", "AMBIENT TEMPERATURE", "celsius", -12);
        p.Value("gs", "GROUND VELOCITY", "knots", 214);
        p.Expr("rel", "wrap360(wdir + 180 - hdg)");
        p.Scale("dial", 0, 360, 0, 360, wrap: true);
        p.Layers.Add(new JsonObject { ["type"] = "circle", ["center"] = new JsonArray(100, 120), ["r"] = 92, ["fill"] = Box, ["stroke"] = Edge, ["strokeWidth"] = 2 });
        p.Layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["every"] = 30, ["from"] = 0, ["to"] = 330, ["radius"] = 90, ["length"] = 9, ["width"] = 2, ["color"] = Label, ["center"] = new JsonArray(100, 120) });
        // The airplane (nose up) and the wind arrow turning around it.
        p.Rect(97, 92, 6, 56, Ink);
        p.Rect(72, 108, 56, 6, Ink);
        p.Rect(88, 140, 24, 4, Ink);
        p.Layers.Add(new JsonObject
        {
            ["type"] = "group", ["center"] = new JsonArray(100, 120), ["rotate"] = new JsonObject { ["value"] = "rel", ["scale"] = "dial" },
            ["layers"] = new JsonArray(
                new JsonObject { ["type"] = "rect", ["x"] = 97, ["y"] = 44, ["width"] = 6, ["height"] = 46, ["fill"] = Cyan },
                new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(0), ["radius"] = 86, ["length"] = 22, ["width"] = 20, ["color"] = Cyan, ["shape"] = "triangle", ["center"] = new JsonArray(100, 120) }),
        });
        p.Text("WIND", 252, 30, 13, Label);
        p.ValueText("wdir", "000°", 252, 62, 28, Ink, "center");
        p.ValueText("wspd", "0 KT", 252, 94, 28, Cyan, "center");
        p.Text("OAT", 252, 132, 13, Label);
        p.ValueText("oat", "0 °C;-0 °C", 252, 160, 24, Ink, "center");
        p.Text("GROUND SPEED", 252, 192, 13, Label);
        p.ValueText("gs", "0 KT", 252, 222, 24, Ink, "center");
        return p.Gauge("Wind and temperature", "Wind arrow relative to the nose (pointing where the wind blows to), wind direction (from, true) and speed, outside air temperature and ground speed.");
    }

    /// <summary>A small round single-needle gauge in the classic look.</summary>
    private static string Dial(string name, string sim, string unit, double min, double max, double[] labels, (double From, double To) green, double[] reds, string[] words, double sample, string font)
    {
        var g = new GaugeParts(GaugeLook.Classic);
        var layers = g.Case();
        if (green.To > green.From)
            layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = "s", ["from"] = green.From, ["to"] = green.To, ["radius"] = g.R(107), ["width"] = 6, ["color"] = GaugeParts.Green });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "s", ["every"] = (max - min) / 20, ["from"] = min, ["to"] = max, ["radius"] = g.R(104), ["length"] = 8, ["width"] = 1.5, ["color"] = g.InkColor });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "s", ["at"] = new JsonArray([.. labels.Select(l => (JsonNode)l)]), ["radius"] = g.R(104), ["length"] = 16, ["width"] = 2.8, ["color"] = g.InkColor });
        if (reds.Length > 0)
            layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "s", ["at"] = new JsonArray([.. reds.Select(l => (JsonNode)l)]), ["radius"] = g.R(110), ["length"] = 20, ["width"] = 4, ["color"] = GaugeParts.Red });
        layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = "s", ["at"] = new JsonArray([.. labels.Select(l => (JsonNode)l)]), ["radius"] = g.R(78), ["size"] = 22, ["color"] = g.InkColor });
        for (var i = 0; i < words.Length; i++) layers.Add(g.Text(words[i], 0, 40 + i * 16, i == 0 ? 16 : 11));
        layers.Add(g.Needle("s", "v", 92));
        foreach (var h in g.Hub()) layers.Add(h);
        layers.Add(g.Glass());
        var values = new JsonObject { ["v"] = new JsonObject { ["sim"] = sim, ["unit"] = unit, ["default"] = sample } };
        var scales = new JsonObject { ["s"] = new JsonObject { ["min"] = min, ["max"] = max, ["startAngle"] = -135, ["endAngle"] = 135 } };
        return g.Gauge(name, $"{name} ({sim}).", values, scales, layers, font);
    }

    // ---------- 737 extras ----------

    /// <summary>737 autobrake selector: RTO, OFF, 1, 2, 3, MAX (AUTO BRAKE SWITCH CB 0-5). Knobs turn the selector.</summary>
    public static string Autobrake(string font = "BarlowCondensed-Medium.ttf")
    {
        var p = new Page(font);
        p.Value("ab", "AUTO BRAKE SWITCH CB", "number", 3);
        p.Text("AUTO BRAKE", 160, 30, 16, Label);
        string[] positions = ["RTO", "OFF", "1", "2", "3", "MAX"];
        for (var i = 0; i < positions.Length; i++)
        {
            var x = 16 + i * 49.0;
            p.Rect(x, 70, 44, 80, Box, Edge);
            p.Show($"ab_{i}", $"choose(abs(ab - {i}) - 0.5, 0, 1)", new JsonObject { ["type"] = "rect", ["x"] = x + 2, ["y"] = 72, ["width"] = 40, ["height"] = 76, ["radius"] = 3, ["fill"] = i == 1 ? Label : Green });
            p.Text(positions[i], x + 22, 118, positions[i].Length > 2 ? 15 : 22, Ink);
        }
        p.Text("Knobs turn the selector. Position numbers: check in your plane.", 160, 200, 11, Dim);
        p.Control("rightKnobClockwise", "INCREASE_AUTOBRAKE_CONTROL");
        p.Control("rightKnobAnticlockwise", "DECREASE_AUTOBRAKE_CONTROL");
        p.Control("leftKnobClockwise", "INCREASE_AUTOBRAKE_CONTROL");
        p.Control("leftKnobAnticlockwise", "DECREASE_AUTOBRAKE_CONTROL");
        return p.Gauge("Autobrake (Boeing style)", "Boeing-style (737) autobrake selector (RTO, OFF, 1, 2, 3, MAX from AUTO BRAKE SWITCH CB 0-5; to check per plane). Knobs turn the selector.");
    }

    // ---------- Airbus style ----------

    private const string Ecam = "#0A0D12", EcamGreen = "#3BE36E", EcamWhite = "#EDEDED", FcuDigits = "#FFA62B";

    /// <summary>Airbus ECAM-style slats and flaps: S and F tracks with moving markers and the lever position (0, 1, 1+F, 2, 3, FULL).</summary>
    public static string AirbusFlaps(string font = "BarlowCondensed-Medium.ttf")
    {
        var p = new Page(font, Ecam);
        p.Value("idx", "FLAPS HANDLE INDEX", "number", 1);
        p.Value("slat", "LEADING EDGE FLAPS LEFT PERCENT", "percent over 100", 0.7);
        p.Value("flap", "TRAILING EDGE FLAPS LEFT PERCENT", "percent over 100", 0.25);
        p.Expr("s_x", "-110 * clamp(slat, 0, 1)");
        p.Expr("s_y", "70 * clamp(slat, 0, 1)");
        p.Expr("f_x", "110 * clamp(flap, 0, 1)");
        p.Expr("f_y", "70 * clamp(flap, 0, 1)");
        p.Text("FLAPS", 160, 26, 14, EcamWhite);
        p.Rect(142, 50, 36, 10, EcamWhite);
        foreach (var t in new[] { 0.25, 0.5, 0.75, 1.0 })
        {
            p.Rect(132 - 110 * t - 2, 66 + 70 * t - 2, 5, 5, EcamWhite);
            p.Rect(188 + 110 * t - 2, 66 + 70 * t - 2, 5, 5, EcamWhite);
        }
        p.Text("S", 40, 70, 22, EcamWhite);
        p.Text("F", 280, 70, 22, EcamWhite);
        p.Move("moveX", "s_x", new JsonObject { ["type"] = "group", ["moveY"] = new JsonObject { ["value"] = "s_y", ["scale"] = "px" }, ["layers"] = new JsonArray(new JsonObject { ["type"] = "rect", ["x"] = 124, ["y"] = 58, ["width"] = 16, ["height"] = 16, ["radius"] = 2, ["fill"] = EcamGreen }) });
        p.Move("moveX", "f_x", new JsonObject { ["type"] = "group", ["moveY"] = new JsonObject { ["value"] = "f_y", ["scale"] = "px" }, ["layers"] = new JsonArray(new JsonObject { ["type"] = "rect", ["x"] = 180, ["y"] = 58, ["width"] = 16, ["height"] = 16, ["radius"] = 2, ["fill"] = EcamGreen }) });
        // Lever position, ECAM style: 1+F when the lever is at 1 with flaps out.
        var settings = new (string Text, string Test)[]
        {
            ("0", "choose(abs(idx) - 0.5, 0, 1)"), ("1", "choose(abs(idx - 1) - 0.5, 0, choose(flap - 0.02, 0, 1))"), ("1+F", "choose(abs(idx - 1) - 0.5, 0, choose(flap - 0.02, 1, 0))"),
            ("2", "choose(abs(idx - 2) - 0.5, 0, 1)"), ("3", "choose(abs(idx - 3) - 0.5, 0, 1)"), ("FULL", "choose(idx - 3.5, 1, 0)"),
        };
        for (var i = 0; i < settings.Length; i++)
            p.Show($"conf{i}", settings[i].Test, new JsonObject { ["type"] = "text", ["text"] = settings[i].Text, ["x"] = 160, ["y"] = 196, ["size"] = 44, ["color"] = EcamGreen });
        p.Text("knobs: flap lever", 160, 228, 11, Dim);
        foreach (var (control, ev, _) in FlapKnobs) p.Control(control, ev);
        return p.Gauge("Flaps (Airbus style)", "Airbus ECAM-style slats (S) and flaps (F) with moving markers and the lever position: 0, 1, 1+F, 2, 3, FULL. Knobs move the flap lever.");
    }

    /// <summary>Airbus ECAM-style landing gear: a triangle per leg, green down, red moving, dark up; lever UP/DN. S1 moves the gear.</summary>
    public static string AirbusGear(string font = "BarlowCondensed-Medium.ttf")
    {
        var p = new Page(font, Ecam);
        p.Scale("tri", 0, 360, 0, 360, wrap: true);
        p.SideLabel(1, "GEAR");
        p.Text("LDG GEAR", 175, 28, 15, EcamWhite);
        foreach (var (key, sim, label, x, y) in new[] { ("nose", "GEAR CENTER POSITION", "NOSE", 175.0, 92.0), ("left", "GEAR LEFT POSITION", "LEFT", 100.0, 150.0), ("right", "GEAR RIGHT POSITION", "RIGHT", 250.0, 150.0) })
        {
            p.Value(key, sim, "percent over 100", key == "right" ? 0.5 : 1);
            p.Expr($"{key}_down", $"choose({key} - 0.99, 1, 0)");
            p.Expr($"{key}_moving", $"choose({key} - 0.01, choose(0.99 - {key}, 1, 0), 0)");
            JsonObject Tri(string color) => new() { ["type"] = "ticks", ["scale"] = "tri", ["at"] = new JsonArray(180), ["radius"] = 16, ["length"] = -30, ["width"] = 40, ["color"] = color, ["shape"] = "triangle", ["center"] = new JsonArray(x, y - 40) };
            p.Layers.Add(Tri("#1E242B"));
            p.Show($"{key}_g", $"{key}_down", Tri(EcamGreen));
            p.Show($"{key}_r", $"{key}_moving", Tri(Red));
            p.Text(label, x, y + 26, 12, Dim);
        }
        p.Value("lever", "GEAR HANDLE POSITION", "percent over 100", 1);
        p.Show("lever_dn", "choose(lever - 0.5, 1, 0)", new JsonObject { ["type"] = "text", ["text"] = "LEVER DN", ["x"] = 175, ["y"] = 222, ["size"] = 18, ["color"] = EcamGreen });
        p.Show("lever_up", "choose(lever - 0.5, 0, 1)", new JsonObject { ["type"] = "text", ["text"] = "LEVER UP", ["x"] = 175, ["y"] = 222, ["size"] = 18, ["color"] = EcamWhite });
        p.Control("button1", "GEAR_TOGGLE");
        return p.Gauge("Landing gear (Airbus style)", "Airbus ECAM-style gear: a triangle per leg (green down, red moving, dark up) and the lever position. S1 moves the gear.");
    }

    /// <summary>Airbus FCU-style autopilot display: SPD, HDG, ALT, V/S windows in orange digits. S1-S4 pick what the knobs set; S6 AP1.</summary>
    public static string AirbusFcu(string font = "BarlowCondensed-Medium.ttf")
    {
        var p = new Page(font, "#16191D");
        p.Value("spd", "AUTOPILOT AIRSPEED HOLD VAR", "knots", 250);
        p.Value("hdg", "AUTOPILOT HEADING LOCK DIR", "degrees", 270);
        p.Value("alt", "AUTOPILOT ALTITUDE LOCK VAR", "feet", 12000);
        p.Value("vs", "AUTOPILOT VERTICAL HOLD VAR", "feet per minute", -800);
        p.Value("ap", "AUTOPILOT MASTER", "bool", 1);
        p.Modes(["SPD", "HDG", "ALT", "V/S"]);
        p.Rect(50, 34, 266, 92, "#050505", "#2B2F34");
        var windows = new[] { ("spd", "SPD", "000", 86.0), ("hdg", "HDG", "000", 150.0), ("alt", "ALT", "00000", 222.0), ("vs", "V/S", "+0000;-0000;0000", 288.0) };
        for (var i = 0; i < windows.Length; i++)
        {
            var (value, label, format, x) = windows[i];
            p.Text(label, x, 54, 13, EcamWhite);
            p.ValueText(value, format, x, 104, i >= 2 ? 26 : 32, FcuDigits, "center");
            p.Selectable(i + 1, x - (i >= 2 ? 32 : 26), 112, i >= 2 ? 64 : 52, 6, solid: true);
        }
        p.SideLabel(6, "AP1");
        p.Rect(130, 196, 110, 36, "#2B2F34", "#40454B");
        p.Text("AP 1", 185, 222, 18, EcamWhite);
        p.Lamp("ap", 136, 200, 98, 6, EcamGreen, "");
        p.ModeControls(1, "AP_SPD_VAR_INC", "AP_SPD_VAR_DEC", 10);
        p.ModeControls(2, "HEADING_BUG_INC", "HEADING_BUG_DEC", 10);
        p.ModeControls(3, "AP_ALT_VAR_INC", "AP_ALT_VAR_DEC", 10);
        p.ModeControls(4, "AP_VS_VAR_INC", "AP_VS_VAR_DEC", 5);
        p.Control("button6", "AP_MASTER");
        p.Light("button6", "ap");
        return p.Gauge("FCU (Airbus style)", "Airbus FCU-style autopilot display: speed, heading, altitude and vertical speed in orange digits. S1-S4 pick which one the knobs set (left knob faster); S6 AP1 on/off with its light.");
    }

    /// <summary>Airbus autobrake: LO, MED, MAX on S1-S3 (SET_AUTOBRAKE_CONTROL 1-3; to check per plane), with lights.</summary>
    public static string AirbusAutobrake(string font = "BarlowCondensed-Medium.ttf")
    {
        var p = new Page(font, "#16191D");
        p.Value("ab", "AUTO BRAKE SWITCH CB", "number", 2);
        p.Text("AUTO/BRK", 182, 150, 15, EcamWhite);
        string[] levels = ["LO", "MED", "MAX"];
        for (var i = 0; i < levels.Length; i++)
        {
            var y = 6 + i * 40.0;   // level with S1-S3
            p.SideLabel(i + 1, levels[i]);
            p.Expr($"on{i + 1}", $"choose(abs(ab - {i + 1}) - 0.5, 0, 1)");
            p.Rect(110, y, 144, 32, "#2B2F34", "#40454B");
            p.Text(levels[i], 182, y + 25, 18, EcamWhite);
            p.Lamp($"on{i + 1}", 116, y + 3, 132, 6, EcamGreen, "");
            p.Control($"button{i + 1}", "SET_AUTOBRAKE_CONTROL", (uint)(i + 1));
            p.Light($"button{i + 1}", $"on{i + 1}");
        }
        p.Text("Level numbers: check in your plane.", 182, 228, 11, Dim);
        return p.Gauge("Autobrake (Airbus style)", "Airbus-style autobrake: LO, MED, MAX on S1-S3 (SET_AUTOBRAKE_CONTROL 1, 2, 3; to check per plane), the chosen one lit.");
    }

    /// <summary>Airbus-style speedbrake: lever RET to FULL and GND SPLRS ARMED; S1 arms the ground spoilers.</summary>
    public static string AirbusSpeedbrake(string font = "BarlowCondensed-Medium.ttf")
    {
        var p = new Page(font, Ecam);
        p.Value("sb", "SPOILERS HANDLE POSITION", "percent over 100", 0.5);
        p.Value("armed", "SPOILERS ARMED", "bool", 0);
        p.Expr("sb_y", "clamp(sb, 0, 1) * 150");
        p.SideLabel(1, "ARM");
        p.Text("SPEED BRAKE", 200, 26, 15, EcamWhite);
        p.Rect(150, 44, 20, 170, "#1E242B", "#40454B");
        foreach (var (text, y) in new[] { ("RET", 52.0), ("1/2", 127.0), ("FULL", 202.0) })
        {
            p.Rect(172, y - 1, 14, 2, EcamWhite);
            p.Text(text, 192, y + 5, 13, EcamWhite, "left");
        }
        p.Move("moveY", "sb_y", p.Pointer(160, 52, "right"));
        p.Rect(226, 92, 90, 50, "#1E242B", "#40454B");
        p.Lamp("armed", 228, 94, 86, 46, EcamGreen, "GND SPLRS ARMED", textColor: "#101010");
        p.Show("not_armed", "choose(armed - 0.5, 0, 1)", new JsonObject { ["type"] = "text", ["text"] = "NOT ARMED", ["x"] = 271, ["y"] = 122, ["size"] = 13, ["color"] = Dim });
        p.ValueText("sb", "0%", 271, 186, 26, EcamGreen, "center"); // the % format already multiplies by 100
        p.Control("button1", "SPOILERS_ARM_TOGGLE");
        p.Light("button1", "armed");
        return p.Gauge("Speedbrake (Airbus style)", "Airbus-style speedbrake lever position (RET to FULL) and GND SPLRS ARMED light. S1 arms or disarms the ground spoilers.");
    }

    /// <summary>A builder for flat "panel" templates: dark screen, boxes, lamps, readouts, knob modes and button lights.</summary>
    private sealed class Page(string font, string background = Bg)
    {
        public JsonArray Layers { get; } = [new JsonObject { ["type"] = "rect", ["x"] = 0, ["y"] = 0, ["width"] = 320, ["height"] = 240, ["fill"] = background }];
        private readonly JsonObject _values = [];
        private readonly JsonObject _scales = new() { ["px"] = new JsonObject { ["min"] = -400, ["max"] = 400, ["startAngle"] = -400, ["endAngle"] = 400 } };
        private readonly JsonObject _controls = [];
        private readonly JsonArray _modes = [];
        private readonly JsonObject _lights = [];

        public void Value(string key, string sim, string unit, double sample) => _values[key] = new JsonObject { ["sim"] = sim, ["unit"] = unit, ["default"] = sample };
        public void Expr(string key, string expr) => _values[key] = new JsonObject { ["expr"] = expr };
        public void Scale(string key, double min, double max, double start, double end, bool wrap = false)
        {
            var s = new JsonObject { ["min"] = min, ["max"] = max, ["startAngle"] = start, ["endAngle"] = end };
            if (wrap) s["wrap"] = true;
            _scales[key] = s;
        }

        public void Rect(double x, double y, double w, double h, string fill, string? stroke = null)
        {
            var r = new JsonObject { ["type"] = "rect", ["x"] = x, ["y"] = y, ["width"] = w, ["height"] = h, ["radius"] = 3, ["fill"] = fill };
            if (stroke is not null) { r["stroke"] = stroke; r["strokeWidth"] = 1.2; }
            Layers.Add(r);
        }

        public void Text(string text, double x, double y, double size, string color, string align = "center") =>
            Layers.Add(new JsonObject { ["type"] = "text", ["text"] = text, ["x"] = x, ["y"] = y, ["size"] = size, ["color"] = color, ["align"] = align });

        public void ValueText(string value, string format, double x, double y, double size, string color, string align, double factor = 1)
        {
            var v = value;
            if (factor != 1) { v = $"{value}_x{N(factor)}"; Expr(v, $"{value} * {N(factor)}"); }
            Layers.Add(new JsonObject { ["type"] = "text", ["value"] = v, ["format"] = format, ["x"] = x, ["y"] = y, ["size"] = size, ["color"] = color, ["align"] = align });
        }

        /// <summary>The label next to S button 1-6 on the left edge of the screen.</summary>
        public void SideLabel(int button, string text) => Text(text, 4, 25 + (button - 1) * 40, 12, Cyan, "left");

        /// <summary>Layers shown only while <paramref name="expr"/> is 1 (moved off screen otherwise).</summary>
        public void Show(string key, string expr, params JsonNode[] layers)
        {
            Expr($"{key}_hide", $"choose({expr} - 0.5, 0, 400)");
            Layers.Add(new JsonObject { ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = $"{key}_hide", ["scale"] = "px" }, ["layers"] = new JsonArray(layers) });
        }

        /// <summary>A lit lamp with text while <paramref name="value"/> is on.</summary>
        public void Lamp(string value, double x, double y, double w, double h, string color, string text, string textColor = "#0B0C0E") =>
            Show($"{value}_lamp{Layers.Count}", value,
                new JsonObject { ["type"] = "rect", ["x"] = x, ["y"] = y, ["width"] = w, ["height"] = h, ["radius"] = 3, ["fill"] = color },
                new JsonObject { ["type"] = "text", ["text"] = text, ["x"] = x + w / 2, ["y"] = y + h / 2 + 6, ["size"] = Math.Round(Math.Min(Math.Min(18, h * 0.6), w / Math.Max(1, text.Length * 0.52)), 1), ["color"] = textColor });

        public void Move(string axis, string value, JsonNode layer) =>
            Layers.Add(new JsonObject { ["type"] = "group", [axis] = new JsonObject { ["value"] = value, ["scale"] = "px" }, ["layers"] = new JsonArray(layer) });

        /// <summary>A small triangle pointer at (x, y), pointing "right" (at a vertical scale) or "down" (at a horizontal one).</summary>
        public JsonNode Pointer(double x, double y, string pointing)
        {
            Scale("ptr", 0, 360, 0, 360, wrap: true);
            var angle = pointing == "right" ? 90 : 180; // the triangle points away from its centre, toward the scale
            var (cx, cy) = pointing == "right" ? (x - 30, y) : (x, y - 30);
            return new JsonObject { ["type"] = "ticks", ["scale"] = "ptr", ["at"] = new JsonArray(angle), ["radius"] = 22, ["length"] = -14, ["width"] = 14, ["color"] = Amber, ["shape"] = "triangle", ["center"] = new JsonArray(cx, cy) };
        }

        /// <summary>Knob modes on S1, S2...; <paramref name="labels"/> are the short S-button labels (else the names).</summary>
        public void Modes(string[] names, string[]? labels = null)
        {
            _values[GaugeDefinition.KnobModeValue] = new JsonObject { ["default"] = 1 };
            for (var i = 0; i < names.Length; i++)
            {
                _modes.Add(new JsonObject { ["name"] = names[i], ["button"] = $"button{i + 1}", ["controls"] = new JsonObject() });
                var label = labels?[i] ?? names[i];
                SideLabel(i + 1, label.Length <= 7 ? label : label[..7]);
            }
        }

        /// <summary>Highlights an area (frame, or a solid bar) while knob mode <paramref name="mode"/> is chosen.</summary>
        public void Selectable(int mode, double x, double y, double w, double h, bool solid = false) =>
            Show($"sel{mode}_{Layers.Count}", $"choose(abs({GaugeDefinition.KnobModeValue} - {mode}) - 0.5, 0, 1)",
                solid
                    ? new JsonObject { ["type"] = "rect", ["x"] = x, ["y"] = y, ["width"] = w, ["height"] = h, ["radius"] = 2, ["fill"] = Cyan }
                    : new JsonObject { ["type"] = "rect", ["x"] = x, ["y"] = y, ["width"] = w, ["height"] = h, ["radius"] = 4, ["stroke"] = Cyan, ["strokeWidth"] = 2.5 });

        /// <summary>Knob actions for a mode: right knob one step; left knob <paramref name="fast"/> steps, or its own events.</summary>
        public void ModeControls(int mode, string inc, string dec, int fast, string? leftInc = null, string? leftDec = null)
        {
            var c = _modes[mode - 1]!["controls"]!.AsObject();
            c["rightKnobClockwise"] = new JsonObject { ["event"] = inc };
            c["rightKnobAnticlockwise"] = new JsonObject { ["event"] = dec };
            c["leftKnobClockwise"] = leftInc is null ? Act(inc, fast) : new JsonObject { ["event"] = leftInc };
            c["leftKnobAnticlockwise"] = leftDec is null ? Act(dec, fast) : new JsonObject { ["event"] = leftDec };
            static JsonObject Act(string e, int repeat) => repeat > 1 ? new JsonObject { ["event"] = e, ["repeat"] = repeat } : new JsonObject { ["event"] = e };
        }

        public void Control(string control, string ev, uint data = 0)
        {
            var c = new JsonObject { ["event"] = ev };
            if (data != 0) c["data"] = data;
            _controls[control] = c;
        }
        public void Light(string button, string value) => _lights[button] = value;

        public string Gauge(string name, string description)
        {
            var g = new JsonObject
            {
                ["format"] = 1, ["name"] = name, ["author"] = "Made with FIPinator 3000", ["description"] = description,
                ["canvas"] = new JsonObject { ["width"] = 320, ["height"] = 240, ["background"] = background },
                ["center"] = new JsonArray(160, 120), ["font"] = font, ["values"] = _values, ["scales"] = _scales, ["layers"] = Layers,
            };
            if (_controls.Count > 0) g["controls"] = _controls;
            if (_modes.Count > 0) g["modes"] = _modes;
            if (_lights.Count > 0) g["buttonLights"] = _lights;
            return g.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
