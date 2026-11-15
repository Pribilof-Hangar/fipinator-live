// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fipinator.Core;

/// <summary>
/// More classic instruments: radar altimeter, clock and fixed-card ADF. Sim values and knob events
/// are the ones listed in the MSFS 2024 SDK (MSFS_EventsEnum.h and the SDK's instrument templates).
/// </summary>
public static class ClassicExtras
{
    /// <summary>Adds knob/button actions ("rightKnobClockwise" -> event, repeat) to a gauge file.</summary>
    internal static string WithControls(string json, params (string Control, string Event, int Repeat)[] controls)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        var list = new JsonObject();
        foreach (var (control, ev, repeat) in controls)
        {
            var c = new JsonObject { ["event"] = ev };
            if (repeat > 1) c["repeat"] = repeat;
            list[control] = c;
        }
        node["controls"] = list;
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>The four knob directions: right knob one step, left knob ten steps.</summary>
    internal static (string, string, int)[] Knobs(string increase, string decrease, int fast = 10) =>
    [
        ("rightKnobClockwise", increase, 1), ("rightKnobAnticlockwise", decrease, 1),
        ("leftKnobClockwise", increase, fast), ("leftKnobAnticlockwise", decrease, fast),
    ];

    /// <summary>
    /// Radar altimeter, 0-2,500 ft with the low end opened up, an orange decision-height bug and an
    /// amber DH light below it. Knobs set the decision height (INCREASE_/DECREASE_DECISION_HEIGHT; DECISION_HEIGHT_INC/DEC are only the SDK's internal names, 1 m per click).
    /// </summary>
    public static string RadarAltimeter(string name, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf")
    {
        var g = new GaugeParts(look);
        var layers = g.Case();
        const string orange = "#E8862A";
        var ink = g.InkColor;
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "ra", ["every"] = 20, ["from"] = 0, ["to"] = 500, ["radius"] = g.R(104), ["length"] = 7, ["width"] = 1.4, ["color"] = ink });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "ra", ["every"] = 100, ["from"] = 500, ["to"] = 2500, ["radius"] = g.R(104), ["length"] = 7, ["width"] = 1.4, ["color"] = ink });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "ra", ["at"] = new JsonArray(0, 100, 200, 300, 400, 500, 1000, 1500, 2000, 2500), ["radius"] = g.R(104), ["length"] = 15, ["width"] = 2.8, ["color"] = ink });
        layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = "ra", ["at"] = new JsonArray(0, 100, 200, 300, 400, 500, 1000, 1500, 2000, 2500), ["divideBy"] = 100, ["radius"] = g.R(80), ["size"] = 20, ["color"] = ink });
        layers.Add(g.Text("RADAR", -34, -8, 12));
        layers.Add(g.Text("ALTITUDE", -34, 6, 12));
        layers.Add(g.Text("X 100 FEET", 22, 60, 10));
        // Decision-height bug: a group turned to the DH value on the same scale.
        layers.Add(new JsonObject
        {
            ["type"] = "group", ["rotate"] = new JsonObject { ["value"] = "dh", ["scale"] = "ra" },
            ["layers"] = new JsonArray(new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(0), ["radius"] = g.R(112), ["length"] = 14, ["width"] = 13, ["shape"] = "triangle", ["color"] = orange }),
        });
        // DH light: amber when the radar altitude is below the decision height.
        var light = g.P(-34, 34);
        layers.Add(new JsonObject
        {
            ["type"] = "group", ["moveX"] = new JsonObject { ["value"] = "dh_light", ["scale"] = "px" },
            ["layers"] = new JsonArray(
                new JsonObject { ["type"] = "circle", ["r"] = 11, ["center"] = light.DeepClone(), ["fillTop"] = "#FFE9A0", ["fillBottom"] = "#E0901A", ["stroke"] = "#000000" },
                new JsonObject { ["type"] = "text", ["text"] = "DH", ["x"] = light[0]!.DeepClone(), ["y"] = light[1]!.GetValue<double>() + 4, ["size"] = 11, ["color"] = "#1A1A1A" }),
        });
        layers.Add(g.Needle("ra", "ra", 94));
        foreach (var h in g.Hub()) layers.Add(h);
        layers.Add(g.Glass());

        var values = new JsonObject
        {
            ["ra"] = new JsonObject { ["sim"] = "RADIO HEIGHT", ["unit"] = "feet", ["default"] = 180 },
            ["dh"] = new JsonObject { ["sim"] = "DECISION HEIGHT", ["unit"] = "feet", ["default"] = 200 },
            ["dh_light"] = new JsonObject { ["expr"] = "choose(dh - ra, 0, 400)" },
        };
        var scales = new JsonObject
        {
            ["ra"] = new JsonObject { ["points"] = new JsonArray(P(0, 10), P(100, 40), P(200, 70), P(300, 95), P(400, 120), P(500, 145), P(1000, 195), P(1500, 225), P(2000, 250), P(2500, 275)) },
            ["dial"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = 360, ["wrap"] = true },
            ["px"] = new JsonObject { ["min"] = -1000, ["max"] = 1000, ["startAngle"] = -1000, ["endAngle"] = 1000 },
        };
        return WithControls(g.Gauge(name, "Radar altimeter 0-2,500 ft with decision-height bug and DH light. Knobs set the decision height.", values, scales, layers, font),
            Knobs("INCREASE_DECISION_HEIGHT", "DECREASE_DECISION_HEIGHT"));
    }

    /// <summary>Panel clock: hour, minute and red second hand from the sim's local time. No knobs needed.</summary>
    public static string Clock(string name, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf")
    {
        var g = new GaugeParts(look);
        var layers = g.Case();
        var ink = g.InkColor;
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "min", ["every"] = 1, ["from"] = 0, ["to"] = 59, ["radius"] = g.R(104), ["length"] = 6, ["width"] = 1.3, ["color"] = ink });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "min", ["every"] = 5, ["from"] = 0, ["to"] = 55, ["radius"] = g.R(104), ["length"] = 14, ["width"] = 3, ["color"] = ink });
        layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = "hr", ["every"] = 1, ["from"] = 1, ["to"] = 12, ["radius"] = g.R(80), ["size"] = 22, ["color"] = ink });
        layers.Add(g.Text("LOCAL TIME", 0, 42, 9));
        layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "hr", ["value"] = "h", ["length"] = g.R(52), ["tail"] = g.R(12), ["width"] = 5, ["color"] = "#F4F2EC", ["tailColor"] = "#F4F2EC", ["shadow"] = true });
        layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "min", ["value"] = "m", ["length"] = g.R(86), ["tail"] = g.R(14), ["width"] = 3.6, ["color"] = "#F4F2EC", ["tailColor"] = "#F4F2EC", ["shadow"] = true });
        layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "min", ["value"] = "s", ["length"] = g.R(94), ["tail"] = g.R(20), ["width"] = 1.3, ["color"] = GaugeParts.Red, ["tailColor"] = GaugeParts.Red, ["shadow"] = true });
        foreach (var h in g.Hub(r: 6)) layers.Add(h);
        layers.Add(g.Glass());

        var values = new JsonObject
        {
            ["t"] = new JsonObject { ["sim"] = "LOCAL TIME", ["unit"] = "seconds", ["default"] = 36925 },
            ["h"] = new JsonObject { ["expr"] = "t / 3600 % 12" },
            ["m"] = new JsonObject { ["expr"] = "t / 60 % 60" },
            ["s"] = new JsonObject { ["expr"] = "t % 60" },
        };
        var scales = new JsonObject
        {
            ["hr"] = new JsonObject { ["min"] = 0, ["max"] = 12, ["startAngle"] = 0, ["endAngle"] = 360, ["wrap"] = true },
            ["min"] = new JsonObject { ["min"] = 0, ["max"] = 60, ["startAngle"] = 0, ["endAngle"] = 360, ["wrap"] = true },
        };
        return g.Gauge(name, "Panel clock showing the sim's local time.", values, scales, layers, font);
    }

    /// <summary>
    /// Fixed-card ADF: the needle points to the beacon relative to the nose; the card turns with the
    /// knobs (ADF_CARD_INC/DEC). With no signal the needle parks at 90 degrees, as on the DC-6 RMI.
    /// </summary>
    public static string Adf(string name, GaugeLook look = GaugeLook.Classic, string font = "BarlowCondensed-Medium.ttf")
    {
        var g = new GaugeParts(look);
        var layers = g.Case();
        var ink = g.InkColor;
        layers.Add(new JsonObject
        {
            ["type"] = "group", ["rotate"] = new JsonObject { ["value"] = "card", ["scale"] = "card" },
            ["layers"] = new JsonArray(
                new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["every"] = 5, ["from"] = 0, ["to"] = 355, ["radius"] = g.R(104), ["length"] = 7, ["width"] = 1.4, ["color"] = ink },
                new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["every"] = 10, ["from"] = 0, ["to"] = 350, ["radius"] = g.R(104), ["length"] = 13, ["width"] = 2.4, ["color"] = ink },
                new JsonObject
                {
                    ["type"] = "labels", ["scale"] = "dial", ["every"] = 30, ["from"] = 0, ["to"] = 330, ["radius"] = g.R(82), ["size"] = 19, ["color"] = ink, ["orient"] = "radial",
                    ["texts"] = new JsonObject { ["0"] = "N", ["30"] = "3", ["60"] = "6", ["90"] = "E", ["120"] = "12", ["150"] = "15", ["180"] = "S", ["210"] = "21", ["240"] = "24", ["270"] = "W", ["300"] = "30", ["330"] = "33" },
                }),
        });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(0), ["radius"] = g.R(112), ["length"] = 14, ["width"] = 12, ["shape"] = "triangle", ["color"] = "#E8862A" });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(45, 90, 135, 180, 225, 270, 315), ["radius"] = g.R(112), ["length"] = 8, ["width"] = 8, ["shape"] = "triangle", ["color"] = ink });
        layers.Add(g.Text("ADF", 0, 40, 12));
        layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "dial", ["value"] = "needle", ["style"] = "arrow", ["length"] = g.R(90), ["tail"] = g.R(82), ["width"] = 2.6, ["color"] = "#E8D23A", ["shadow"] = true });
        foreach (var h in g.Hub(r: 7)) layers.Add(h);
        layers.Add(g.Glass());

        var values = new JsonObject
        {
            ["brg"] = new JsonObject { ["sim"] = "ADF RADIAL:1", ["unit"] = "degrees", ["default"] = 35 },
            ["sig"] = new JsonObject { ["sim"] = "ADF SIGNAL:1", ["unit"] = "number", ["default"] = 1 },
            ["card"] = new JsonObject { ["sim"] = "ADF CARD", ["unit"] = "degrees", ["default"] = 0 },
            ["needle"] = new JsonObject { ["expr"] = "choose(sig, brg, 90)" },
        };
        var scales = new JsonObject
        {
            ["card"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = -360, ["wrap"] = true },
            ["dial"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = 360, ["wrap"] = true },
        };
        return WithControls(g.Gauge(name, "Fixed-card ADF: needle to the beacon, card turned with the knobs.", values, scales, layers, font),
            Knobs("ADF_CARD_INC", "ADF_CARD_DEC"));
    }

    private static JsonArray P(double value, double angle) => new(value, angle);
}
