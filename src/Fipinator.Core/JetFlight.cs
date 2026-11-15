// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fipinator.Core;

/// <summary>
/// Flight displays for modern jets. <see cref="Standby"/> is drawn after the small integrated standby
/// display in the middle of a 737 MAX panel: attitude with speed and altitude tapes, baro and heading.
/// Stock MSFS 2024 sim values (checked live in the Asobo 737 MAX, 2026-11-10: baro 992.2 hPa,
/// altitude 119 ft, heading 292 degrees, all as its own display showed).
/// </summary>
public static class JetFlight
{
    private const string White = "#F2F2F2", Sky = "#2348D2", Ground = "#7A5626", Tape = "#3B5063", Green = "#3BE07A";
    private const double Cy = 112;            // attitude centre (x 160)
    private const double PitchPx = 4;         // pixels per degree of pitch
    private const double SpeedPx = 2;         // pixels per knot (20 kt between labels)
    private const double AltPx = 0.4;         // pixels per foot (100 ft between labels)

    /// <summary>
    /// Standby-style attitude display: deep blue sky and brown ground, pitch ladder, bank scale with a
    /// sky pointer, black airplane symbol, speed tape (left) and altitude tape (right) with readout
    /// boxes, baro setting in hPa (top), and a heading strip (bottom).
    /// </summary>
    public static string Standby(string name, string font = "BarlowCondensed-Medium.ttf")
    {
        var inv = CultureInfo.InvariantCulture;
        string N(double v) => v.ToString(inv);
        var layers = new JsonArray();
        var values = new JsonObject
        {
            ["pitch"] = new JsonObject { ["sim"] = "ATTITUDE INDICATOR PITCH DEGREES", ["unit"] = "degrees", ["default"] = -4 }, // nose up is negative in MSFS
            ["bank"] = new JsonObject { ["sim"] = "ATTITUDE INDICATOR BANK DEGREES", ["unit"] = "degrees", ["default"] = 10 },
            ["ias"] = new JsonObject { ["sim"] = "AIRSPEED INDICATED", ["unit"] = "knots", ["default"] = 152 },
            ["alt"] = new JsonObject { ["sim"] = "INDICATED ALTITUDE", ["unit"] = "feet", ["default"] = 2340 },
            ["hpa"] = new JsonObject { ["sim"] = "KOHLSMAN SETTING MB:1", ["unit"] = "millibars", ["default"] = 1013 },
            ["hdg"] = new JsonObject { ["sim"] = "PLANE HEADING DEGREES MAGNETIC", ["unit"] = "degrees", ["default"] = 292 },
            // Flight director: command bars offset from the airplane symbol by (command - attitude);
            // hidden when the flight director is off. Same values and directions as the airliner ADI.
            ["fdp"] = new JsonObject { ["sim"] = "AUTOPILOT FLIGHT DIRECTOR PITCH", ["unit"] = "degrees", ["default"] = -7 },
            ["fdb"] = new JsonObject { ["sim"] = "AUTOPILOT FLIGHT DIRECTOR BANK", ["unit"] = "degrees", ["default"] = 4 },
            ["fdon"] = new JsonObject { ["sim"] = "AUTOPILOT FLIGHT DIRECTOR ACTIVE", ["unit"] = "bool", ["default"] = 1 },
            ["fd_y"] = new JsonObject { ["expr"] = $"clamp(-(fdp - pitch) * {N(PitchPx)}, -70, 70)" },
            ["fd_x"] = new JsonObject { ["expr"] = "clamp((fdb - bank) * 1.5, -70, 70)" },
            ["fd_hide"] = new JsonObject { ["expr"] = "choose(fdon, 0, 400)" },
            // Tapes: the labels near the readout and how far the tape has rolled past the last one.
            ["spd_base"] = new JsonObject { ["expr"] = "max(ias, 0) - max(ias, 0) % 20" },
            ["spd_roll"] = new JsonObject { ["expr"] = "max(ias, 0) % 20" },
            ["alt_base"] = new JsonObject { ["expr"] = "alt - alt % 100" },
            ["alt_roll"] = new JsonObject { ["expr"] = "alt % 100" },
        };

        JsonObject Rect(double x, double y, double w, double h, string fill, string? stroke = null, double sw = 1.5)
        {
            var r = new JsonObject { ["type"] = "rect", ["x"] = x, ["y"] = y, ["width"] = w, ["height"] = h, ["fill"] = fill };
            if (stroke is not null) { r["stroke"] = stroke; r["strokeWidth"] = sw; }
            return r;
        }
        JsonObject Text(string text, double x, double y, double size, string color, string align = "center") =>
            new() { ["type"] = "text", ["text"] = text, ["x"] = x, ["y"] = y, ["size"] = size, ["color"] = color, ["align"] = align };
        JsonObject ValueText(string value, double x, double y, double size, string color, string align) =>
            new() { ["type"] = "text", ["value"] = value, ["format"] = "0", ["x"] = x, ["y"] = y, ["size"] = size, ["color"] = color, ["align"] = align };
        JsonObject Group(JsonObject how, params JsonNode?[] children)
        {
            how["type"] = "group";
            // Layers built in a list first already belong to it: take copies.
            how["layers"] = new JsonArray([.. children.Select(c => c?.Parent is null ? c : c.DeepClone())]);
            return how;
        }

        // Attitude: sky/ground and ladder move with pitch, turn with bank.
        var horizon = new JsonArray(
            Rect(-400, -900, 1120, 900 + Cy, Sky),
            Rect(-400, Cy, 1120, 1000, Ground),
            Rect(-400, Cy - 1, 1120, 2, White));
        var ladder = new JsonArray();
        for (var p = -20.0; p <= 20; p += 2.5)
        {
            if (p == 0) continue;
            var y = Cy - p * PitchPx;
            var w = p % 10 == 0 ? 60 : p % 5 == 0 ? 28 : 14;
            ladder.Add(Rect(160 - w / 2.0, Math.Round(y - 1, 2), w, 2, White));
            if (p % 10 == 0) ladder.Add(Text(N(Math.Abs(p)), 126, Math.Round(y + 5, 2), 14, White, "right"));
        }
        layers.Add(Rect(0, 0, 320, 240, "#000000"));
        layers.Add(Group(new JsonObject { ["clipBox"] = new JsonArray(58, 20, 204, 188) },
            Group(new JsonObject { ["rotate"] = new JsonObject { ["value"] = "bank", ["scale"] = "bank" } },
                Group(new JsonObject { ["moveY"] = new JsonObject { ["value"] = "pitch", ["scale"] = "pitch" } }, [.. horizon]),
                Group(new JsonObject { ["clip"] = 76 },
                    Group(new JsonObject { ["moveY"] = new JsonObject { ["value"] = "pitch", ["scale"] = "pitch" } }, [.. ladder])),
                new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(0), ["radius"] = 78, ["length"] = -10, ["width"] = 12, ["color"] = White, ["shape"] = "triangle" }),
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(10, 20, 340, 350), ["radius"] = 88, ["length"] = 7, ["width"] = 2, ["color"] = White },
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(30, 60, 300, 330), ["radius"] = 90, ["length"] = 12, ["width"] = 2.5, ["color"] = White },
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(45, 315), ["radius"] = 88, ["length"] = 7, ["width"] = 7, ["color"] = White, ["shape"] = "triangle" },
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(0), ["radius"] = 92, ["length"] = 13, ["width"] = 14, ["color"] = White, ["shape"] = "triangle" }));

        // Airplane symbol: black with a white outline.
        foreach (var r in new[] { Rect(108, Cy - 3, 34, 6, "#000000", White), Rect(136, Cy - 3, 6, 15, "#000000", White),
                                  Rect(178, Cy - 3, 34, 6, "#000000", White), Rect(178, Cy - 3, 6, 15, "#000000", White),
                                  Rect(156, Cy - 4, 8, 8, "#000000", White) })
            layers.Add(r);

        // Flight director crossbars (magenta, as on the MAX's main display), over the airplane symbol.
        const string magenta = "#E651D8";
        layers.Add(Group(new JsonObject { ["moveX"] = new JsonObject { ["value"] = "fd_hide", ["scale"] = "px" } },
            Group(new JsonObject { ["clipBox"] = new JsonArray(58, 20, 204, 188) },
                Group(new JsonObject { ["moveY"] = new JsonObject { ["value"] = "fd_y", ["scale"] = "fdpx" } }, Rect(112, Cy - 1.75, 96, 3.5, magenta)),
                Group(new JsonObject { ["moveX"] = new JsonObject { ["value"] = "fd_x", ["scale"] = "fdpx" } }, Rect(158.25, Cy - 48, 3.5, 96, magenta)))));

        // Speed tape (left) and altitude tape (right): labels roll past a fixed readout box.
        void TapeLabels(JsonArray into, string baseValue, double step, double px, double x, string align, bool hideBelowZero)
        {
            for (var n = -3; n <= 3; n++)
            {
                var key = $"{baseValue}_{n + 3}";
                values[key] = new JsonObject { ["expr"] = $"{baseValue} + {N(n * step)}" };
                var label = ValueText(key, x, Math.Round(Cy - n * step * px + 5, 2), 15, White, align);
                if (hideBelowZero)
                {
                    values[$"{key}_hide"] = new JsonObject { ["expr"] = $"choose({key} + 1, 0, 400)" };
                    into.Add(Group(new JsonObject { ["moveX"] = new JsonObject { ["value"] = $"{key}_hide", ["scale"] = "px" } }, label));
                }
                else into.Add(label);
            }
        }
        var speed = new JsonArray();
        for (var n = -7; n <= 7; n++) speed.Add(Rect(48, Math.Round(Cy - n * 10 * SpeedPx - 1, 2), 8, 2, White));
        TapeLabels(speed, "spd_base", 20, SpeedPx, 42, "right", hideBelowZero: true);
        layers.Add(Rect(0, 20, 56, 188, Tape));
        layers.Add(Group(new JsonObject { ["clipBox"] = new JsonArray(0, 20, 56, 188) },
            Group(new JsonObject { ["moveY"] = new JsonObject { ["value"] = "spd_roll", ["scale"] = "spd" } }, [.. speed])));
        layers.Add(Rect(1, Cy - 13, 52, 26, "#000000", White));
        layers.Add(ValueText("ias", 48, Cy + 8, 22, White, "right"));

        var altitude = new JsonArray();
        for (var n = -7; n <= 7; n++) altitude.Add(Rect(264, Math.Round(Cy - n * 50 * AltPx - 1, 2), 8, 2, White));
        TapeLabels(altitude, "alt_base", 100, AltPx, 316, "right", hideBelowZero: false);
        layers.Add(Rect(264, 20, 56, 188, Tape));
        layers.Add(Group(new JsonObject { ["clipBox"] = new JsonArray(264, 20, 56, 188) },
            Group(new JsonObject { ["moveY"] = new JsonObject { ["value"] = "alt_roll", ["scale"] = "alt" } }, [.. altitude])));
        layers.Add(Rect(262, Cy - 13, 57, 26, "#000000", White));
        layers.Add(ValueText("alt", 316, Cy + 8, 20, White, "right"));

        // Baro (top) and heading strip (bottom).
        layers.Add(ValueText("hpa", 186, 16, 15, Green, "right"));
        layers.Add(Text("HPA", 190, 16, 13, Green, "left"));
        var rose = new JsonArray
        {
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["every"] = 5, ["from"] = 0, ["to"] = 355, ["radius"] = 380, ["length"] = 5, ["width"] = 1.5, ["color"] = White, ["center"] = new JsonArray(160, 594) },
            new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["every"] = 10, ["from"] = 0, ["to"] = 350, ["radius"] = 380, ["length"] = 9, ["width"] = 2, ["color"] = White, ["center"] = new JsonArray(160, 594) },
            new JsonObject { ["type"] = "labels", ["scale"] = "dial", ["every"] = 10, ["from"] = 0, ["to"] = 350, ["divideBy"] = 10, ["radius"] = 364, ["size"] = 15, ["orient"] = "radial", ["color"] = White, ["center"] = new JsonArray(160, 594) },
        };
        layers.Add(Group(new JsonObject { ["clipBox"] = new JsonArray(58, 208, 204, 32) },
            Rect(58, 208, 204, 32, "#3A3F44"),
            Group(new JsonObject { ["center"] = new JsonArray(160, 594), ["rotate"] = new JsonObject { ["value"] = "hdg", ["scale"] = "hdg" } }, [.. rose])));
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "dial", ["at"] = new JsonArray(0), ["radius"] = 386, ["length"] = 9, ["width"] = 11, ["color"] = White, ["shape"] = "triangle", ["center"] = new JsonArray(160, 594) });

        var gauge = new JsonObject
        {
            ["format"] = 1,
            ["name"] = name,
            ["author"] = "Made with FIPinator 3000",
            ["description"] = "Compact jet horizon (laid out like the small display in the middle of a 737 MAX panel): attitude with magenta flight director bars, speed and altitude tapes with readouts, baro in hPa, heading strip.",
            ["canvas"] = new JsonObject { ["width"] = 320, ["height"] = 240, ["background"] = "#000000" },
            ["center"] = new JsonArray(160, Cy),
            ["font"] = font,
            ["values"] = values,
            ["scales"] = new JsonObject
            {
                ["bank"] = new JsonObject { ["min"] = -180, ["max"] = 180, ["startAngle"] = -180, ["endAngle"] = 180 },
                ["pitch"] = new JsonObject { ["min"] = -90, ["max"] = 90, ["startAngle"] = 90 * PitchPx, ["endAngle"] = -90 * PitchPx }, // MSFS reports nose up as negative pitch (checked in flight 2026-11-11): nose up moves the horizon down
                ["dial"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = 360, ["wrap"] = true },
                ["hdg"] = new JsonObject { ["min"] = 0, ["max"] = 360, ["startAngle"] = 0, ["endAngle"] = -360, ["wrap"] = true },
                ["spd"] = new JsonObject { ["min"] = 0, ["max"] = 20, ["startAngle"] = 0, ["endAngle"] = 20 * SpeedPx },
                ["alt"] = new JsonObject { ["min"] = 0, ["max"] = 100, ["startAngle"] = 0, ["endAngle"] = 100 * AltPx },
                ["px"] = new JsonObject { ["min"] = 0, ["max"] = 400, ["startAngle"] = 0, ["endAngle"] = 400 },
                ["fdpx"] = new JsonObject { ["min"] = -400, ["max"] = 400, ["startAngle"] = -400, ["endAngle"] = 400 },
            },
            ["layers"] = layers,
        };
        // Like the real standby display's BARO knob: both FIP knobs set the baro (left: 10 steps).
        return ClassicExtras.WithControls(gauge.ToJsonString(), ClassicExtras.Knobs("KOHLSMAN_INC", "KOHLSMAN_DEC"));
    }
}
