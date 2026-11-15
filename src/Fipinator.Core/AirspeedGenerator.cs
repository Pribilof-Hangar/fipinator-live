// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using System.Text.Json.Nodes;

namespace Fipinator.Core;

/// <summary>
/// An aircraft's airspeed limits, in knots. From the sim (DESIGN SPEED VS0/VS1/VC,
/// AIRSPEED BARBER POLE) or a handbook. Zero means "not known".
/// </summary>
/// <param name="StallFlapsDown">Start of the white arc (VS0).</param>
/// <param name="StallClean">Start of the green arc (VS1).</param>
/// <param name="CruiseLimit">Top of the green arc, start of yellow (VNO). Note: the sim's
/// DESIGN SPEED VC is often lower than this (Titan: sim 163, dial about 210).</param>
/// <param name="NeverExceed">Red line (VNE / VMO).</param>
/// <param name="FlapsExtended">Top of the white arc (VFE). Not in the sim.</param>
/// <param name="MinControl">Twins: red line for the slowest controllable speed on one engine (VMC).</param>
/// <param name="BestSingleEngineClimb">Twins: blue line for best climb on one engine (VYSE).</param>
public sealed record AircraftSpeeds(double StallFlapsDown, double StallClean, double CruiseLimit, double NeverExceed,
    double FlapsExtended = 0, double MinControl = 0, double BestSingleEngineClimb = 0)
{
    /// <summary>True if there's enough to draw a sensible dial.</summary>
    public bool Usable => NeverExceed > 0 && StallClean > 0 && NeverExceed > StallClean;
}

/// <summary>
/// Builds a classic airspeed indicator for any aircraft from its speeds, in the style of a
/// 1970s-80s light twin: knots outside, miles per hour on a blue inner ring, white arc on the
/// inside edge, green and yellow arcs outside, red line, and for twins the red (VMC) and blue
/// (VYSE) lines. The scale starts at 12 o'clock and runs clockwise. Original artwork.
/// </summary>
public static class AirspeedGenerator
{
    public const string Green = "#3E9E4A", Yellow = "#E0B13C", White = "#E8E5DC", Red = "#D3362B", Blue = "#3E86D8";
    private const double KnotsToMph = 1.15078;
    private const double LowAngle = 24, HighAngle = 336;

    public static string Create(AircraftSpeeds s, string name, string font = "BarlowCondensed-Medium.ttf")
    {
        if (!s.Usable) throw new ArgumentException("Need at least the clean stall speed and the never-exceed speed.", nameof(s));

        // Knots: 0 at the top, then a jump to the first marked speed (the part below is never
        // read in flight), then even spacing up to a little past the red line.
        const double step = 20;
        var slowest = s.StallFlapsDown > 0 ? Math.Min(s.StallFlapsDown, s.StallClean) : s.StallClean;
        var low = Math.Max(step, Math.Floor(slowest * 0.65 / step) * step);
        var max = Math.Ceiling((s.NeverExceed + 10) / step) * step;
        var cruise = s.CruiseLimit > s.StallClean && s.CruiseLimit < s.NeverExceed ? s.CruiseLimit : s.NeverExceed;

        string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        static JsonArray Pt(double v, double a) => new(v, a);
        JsonObject Arc(double from, double to, double radius, double width, string color) => new()
        {
            ["type"] = "arc", ["scale"] = "kt", ["from"] = from, ["to"] = to,
            ["radius"] = radius, ["width"] = width, ["color"] = color,
        };
        JsonObject Line(double at, double length, double width, string color) => new()
        {
            ["type"] = "ticks", ["scale"] = "kt", ["at"] = new JsonArray(at), ["radius"] = 111,
            ["length"] = length, ["width"] = width, ["color"] = color,
        };

        var layers = new JsonArray
        {
            // Light-grey panel, rubber mounting blocks at the top corners, Phillips-style screws below.
            new JsonObject { ["type"] = "rect", ["x"] = 0, ["y"] = 0, ["width"] = 320, ["height"] = 240, ["fill"] = "#8F9497" },
            new JsonObject { ["type"] = "screw", ["x"] = 62, ["y"] = 22, ["kind"] = "boss" },
            new JsonObject { ["type"] = "screw", ["x"] = 258, ["y"] = 22, ["kind"] = "boss" },
            new JsonObject { ["type"] = "screw", ["x"] = 58, ["y"] = 222, ["kind"] = "slot" },
            new JsonObject { ["type"] = "screw", ["x"] = 262, ["y"] = 222, ["kind"] = "slot" },
            new JsonObject { ["type"] = "circle", ["r"] = 119, ["fillTop"] = "#4A4D50", ["fillBottom"] = "#141516", ["stroke"] = "#2A2C2E", ["strokeWidth"] = 1.2 },
            new JsonObject { ["type"] = "circle", ["r"] = 113, ["fill"] = "#0C0C0C", ["stroke"] = "#1C1C1C", ["strokeWidth"] = 2 },
        };

        // Outer coloured arcs, then the white flap arc on the inside edge of the scale.
        layers.Add(Arc(s.StallClean, cruise, 108, 6, Green));
        if (cruise < s.NeverExceed) layers.Add(Arc(cruise, s.NeverExceed, 108, 6, Yellow));
        if (s.FlapsExtended > 0 && s.StallFlapsDown > 0 && s.FlapsExtended > s.StallFlapsDown)
            layers.Add(Arc(s.StallFlapsDown, s.FlapsExtended, 96, 4, White));

        // Knots scale (white).
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "kt", ["every"] = 5, ["from"] = low, ["to"] = max, ["radius"] = 111, ["length"] = 7, ["width"] = 1.5, ["color"] = "#EDEAE2" });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "kt", ["every"] = 10, ["from"] = low, ["to"] = max, ["radius"] = 111, ["length"] = 13, ["width"] = 2.6, ["color"] = "#EDEAE2" });
        layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = "kt", ["every"] = step, ["from"] = low, ["to"] = max, ["radius"] = 88, ["size"] = 18, ["color"] = "#EDEAE2" });

        // Limit lines on top of the scale.
        layers.Add(Line(s.NeverExceed, 18, 4.5, Red));
        if (s.MinControl > 0) layers.Add(Line(s.MinControl, 18, 4, Red));
        if (s.BestSingleEngineClimb > 0) layers.Add(Line(s.BestSingleEngineClimb, 18, 4, Blue));

        // Miles per hour on the inner blue ring.
        var mphLow = Math.Ceiling(low * KnotsToMph / 10) * 10;
        var mphMax = Math.Floor(max * KnotsToMph / 10) * 10;
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "mph", ["every"] = 10, ["from"] = mphLow, ["to"] = mphMax, ["radius"] = 70, ["length"] = 7, ["width"] = 2, ["color"] = "#5B9BE0" });
        layers.Add(new JsonObject { ["type"] = "labels", ["scale"] = "mph", ["every"] = 50, ["from"] = Math.Ceiling(mphLow / 50) * 50, ["to"] = mphMax, ["radius"] = 54, ["size"] = 13, ["color"] = "#5B9BE0" });

        layers.Add(new JsonObject { ["type"] = "text", ["text"] = "KNOTS", ["x"] = 160, ["y"] = 27, ["size"] = 11, ["color"] = "#D8D4CA" });
        layers.Add(new JsonObject { ["type"] = "text", ["text"] = "AIRSPEED", ["x"] = 160, ["y"] = 92, ["size"] = 12, ["color"] = "#D8D4CA" });
        layers.Add(new JsonObject { ["type"] = "text", ["text"] = "MPH", ["x"] = 160, ["y"] = 168, ["size"] = 10, ["color"] = "#5B9BE0" });
        layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "kt", ["value"] = "ias", ["length"] = 106, ["tail"] = 24, ["width"] = 4, ["color"] = "#F4F2EC", ["tailColor"] = "#161616", ["shadow"] = true });
        layers.Add(new JsonObject { ["type"] = "hub", ["r"] = 9, ["fill"] = "#141414", ["stroke"] = "#4A4A4A" });
        layers.Add(new JsonObject { ["type"] = "glare", ["r"] = 112, ["opacity"] = 0.14 });

        var notes = new List<string>
        {
            $"green {N(s.StallClean)}-{N(cruise)} kt",
            cruise < s.NeverExceed ? $"yellow {N(cruise)}-{N(s.NeverExceed)}" : "no yellow arc",
            $"red line {N(s.NeverExceed)}",
            s.FlapsExtended > 0 ? $"white {N(s.StallFlapsDown)}-{N(s.FlapsExtended)}" : "no white arc (top flap speed not known)",
        };
        if (s.MinControl > 0) notes.Add($"red twin line {N(s.MinControl)}");
        if (s.BestSingleEngineClimb > 0) notes.Add($"blue line {N(s.BestSingleEngineClimb)}");

        var gauge = new JsonObject
        {
            ["format"] = 1,
            ["name"] = name,
            ["author"] = "Made with FIPinator 3000",
            ["description"] = "Classic airspeed indicator built from the aircraft's speeds: " + string.Join(", ", notes) + ".",
            ["canvas"] = new JsonObject { ["width"] = 320, ["height"] = 240, ["background"] = "#8F9497" },
            ["center"] = new JsonArray(160, 120),
            ["font"] = font,
            ["values"] = new JsonObject { ["ias"] = new JsonObject { ["sim"] = "AIRSPEED INDICATED", ["unit"] = "knots", ["default"] = Math.Round(cruise * 0.8) } },
            ["scales"] = new JsonObject
            {
                ["kt"] = new JsonObject { ["points"] = new JsonArray(Pt(0, 0), Pt(low, LowAngle), Pt(max, HighAngle)) },
                ["mph"] = new JsonObject { ["points"] = new JsonArray(Pt(0, 0), Pt(low * KnotsToMph, LowAngle), Pt(max * KnotsToMph, HighAngle)) },
            },
            ["layers"] = layers,
        };
        return gauge.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}
