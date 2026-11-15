// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fipinator.Core;

/// <summary>A gauge as described by a gauge file. See docs/gauge-format.md.</summary>
public sealed record GaugeDefinition
{
    public int Format { get; init; } = 1;
    public string Name { get; init; } = "";
    public string? Author { get; init; }
    public string? Description { get; init; }
    /// <summary>The gauge's licence line, e.g. <see cref="ShippedLicense"/> on FIPinator's own gauges; null if none.</summary>
    public string? License { get; init; }

    /// <summary>The licence line on every gauge that ships with FIPinator (not in its name).</summary>
    public const string ShippedLicense = "FREE and open source (AGPL-3.0): free to use, change, share and fork.";
    public CanvasSpec Canvas { get; init; } = new();
    public float[] Center { get; init; } = [160, 120];
    public string? Font { get; init; }
    public Dictionary<string, ValueSpec> Values { get; init; } = [];
    public Dictionary<string, ScaleSpec> Scales { get; init; } = [];
    public List<Layer> Layers { get; init; } = [];

    /// <summary>
    /// What the FIP's knobs and buttons do, e.g. "rightKnobClockwise": { "event": "KOHLSMAN_INC" }.
    /// Keys: rightKnobClockwise, rightKnobAnticlockwise, leftKnobClockwise, leftKnobAnticlockwise,
    /// button1 ... button6.
    /// </summary>
    public Dictionary<string, ControlSpec> Controls { get; init; } = [];

    /// <summary>
    /// Knob modes: each S button picks what the knobs set (for example S1 HDG, S2 ALT). The chosen mode's
    /// controls come first, then <see cref="Controls"/>. The gauge reads the chosen mode (1, 2, ...) from
    /// its value "knobMode" (declare it in values), and the chosen mode's button light is on.
    /// </summary>
    public List<KnobMode> Modes { get; init; } = [];

    /// <summary>S-button lights: "button3": "landingLights" lights S3 while that value is above 0.5.</summary>
    public Dictionary<string, string> ButtonLights { get; init; } = [];

    /// <summary>The value the FIP app sets to the chosen knob mode (1 = first).</summary>
    public const string KnobModeValue = "knobMode";

    public static readonly IReadOnlySet<string> ControlNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "rightKnobClockwise", "rightKnobAnticlockwise", "leftKnobClockwise", "leftKnobAnticlockwise",
        "button1", "button2", "button3", "button4", "button5", "button6",
    };

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowOutOfOrderMetadataProperties = true,
    };

    public static GaugeDefinition Parse(string json) =>
        JsonSerializer.Deserialize<GaugeDefinition>(json, JsonOptions)
        ?? throw new JsonException("Gauge file is empty.");

    /// <summary>Reads a gauge file and checks it.</summary>
    /// <exception cref="InvalidDataException">The gauge has mistakes; the message lists them all.</exception>
    public static GaugeDefinition Load(string path)
    {
        var gauge = Parse(File.ReadAllText(path));
        var problems = gauge.Problems();
        if (problems.Count > 0)
            throw new InvalidDataException($"Gauge file {Path.GetFileName(path)} has mistakes:\n  - " + string.Join("\n  - ", problems));
        return gauge;
    }

    /// <summary>
    /// Mistakes in the gauge: unknown scales or values, unreadable formulas, formulas that
    /// depend on themselves, uneven scales out of order. Empty when the gauge is fine.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        foreach (var (name, scale) in Scales)
            if (scale.Problem() is { } p) problems.Add($"scale '{name}': {p}");

        var formulas = new Dictionary<string, ValueExpression>();
        foreach (var (name, value) in Values)
        {
            if (value.Expr is null) continue;
            if (value.Sim is not null) problems.Add($"value '{name}' has both 'sim' and 'expr'; use one");
            try { formulas[name] = ValueExpression.Parse(value.Expr); }
            catch (FormatException ex) { problems.Add($"value '{name}': {ex.Message}"); }
        }
        foreach (var (name, formula) in formulas)
        {
            foreach (var used in formula.Names.Where(n => !Values.ContainsKey(n)))
                problems.Add($"value '{name}' uses '{used}', which isn't defined in \"values\"");
            if (DependsOn(name, name, formulas, []))
                problems.Add($"value '{name}' depends on itself");
        }

        void CheckLayers(IEnumerable<Layer> layers)
        {
            foreach (var layer in layers)
            {
                foreach (var (scale, value) in Links(layer))
                {
                    if (scale is not null && !Scales.ContainsKey(scale)) problems.Add($"{layer.GetType().Name.Replace("Layer", "").ToLowerInvariant()} layer uses scale '{scale}', which isn't defined");
                    if (value is not null && !Values.ContainsKey(value)) problems.Add($"{layer.GetType().Name.Replace("Layer", "").ToLowerInvariant()} layer uses value '{value}', which isn't defined");
                }
                if (layer is GroupLayer g) CheckLayers(g.Layers);
            }
        }
        CheckLayers(Layers);

        foreach (var (name, control) in Controls)
        {
            if (!ControlNames.Contains(name))
                problems.Add($"control '{name}' isn't a FIP knob or button; use one of: {string.Join(", ", ControlNames)}");
            if (control.ChangesValue)
            {
                if (control.SetNames.Any(n => !n.StartsWith("L:", StringComparison.Ordinal) || n.Length < 3) || !control.SetNames.Any())
                    problems.Add($"control '{name}': 'set' must be a plane value name starting with L:, like L:MyPlane_Knob");
            }
            else if (!IsEventName(control.Event))
                problems.Add($"control '{name}': event '{control.Event}' should be a sim event name, like KOHLSMAN_INC, an add-on's own event (like MyPlane.Knob_Inc) or a numbered one (like #70001)");
            if (control.Repeat is < 1 or > 50)
                problems.Add($"control '{name}': repeat must be between 1 and 50");
        }

        string[] buttons = ["button1", "button2", "button3", "button4", "button5", "button6"];
        foreach (var mode in Modes)
        {
            if (!buttons.Contains(mode.Button)) problems.Add($"knob mode '{mode.Name}': button must be one of button1 to button6");
            foreach (var (name, control) in mode.Controls)
            {
                if (!ControlNames.Contains(name)) problems.Add($"knob mode '{mode.Name}': '{name}' isn't a FIP knob or button");
                if (!control.ChangesValue && !IsEventName(control.Event)) problems.Add($"knob mode '{mode.Name}': event '{control.Event}' isn't a sim event name");
                if (control.Repeat is < 1 or > 50) problems.Add($"knob mode '{mode.Name}': repeat must be between 1 and 50");
            }
        }
        foreach (var dup in Modes.GroupBy(m => m.Button).Where(g => g.Count() > 1))
            problems.Add($"knob modes: {dup.Key} picks more than one mode");
        if (Modes.Count > 0 && !Values.ContainsKey(KnobModeValue))
            problems.Add($"knob modes need a value '{KnobModeValue}' (set by the FIP app: 1 = first mode)");
        foreach (var (button, value) in ButtonLights)
        {
            if (!buttons.Contains(button)) problems.Add($"button light '{button}' isn't button1 to button6");
            if (!Values.ContainsKey(value)) problems.Add($"button light '{button}' uses value '{value}', which isn't defined");
        }
        return problems;
    }

    /// <summary>
    /// Any event name SimConnect can map: stock events (KOHLSMAN_INC), add-on events with a dot in any
    /// case (MyPlane.Knob_Inc) and numbered third-party events (#70001, #0x11000). Plain letters,
    /// digits, '_', '.' and '-' only, so a gauge file can never carry a path, a command or a script.
    /// </summary>
    public static bool IsEventName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64) return false;
        var body = name[0] == '#' ? name[1..] : name;
        return body.Length > 0 && body.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');
    }

    private static IEnumerable<(string? Scale, string? Value)> Links(Layer layer) => layer switch
    {
        NeedleLayer n => [(n.Scale, n.Value)],
        ScaleStepsLayer s => [(s.Scale, null)],
        ArcLayer a => [(a.Scale, a.ToValue)],
        TextLayer { Value: { } v } => [(null, v)],
        GroupLayer g => new[] { g.Rotate, g.MoveX, g.MoveY }.Where(l => l is not null).Select(l => ((string?)l!.Scale, (string?)l.Value)),
        _ => [],
    };

    private static bool DependsOn(string start, string current, Dictionary<string, ValueExpression> formulas, HashSet<string> seen)
    {
        if (!formulas.TryGetValue(current, out var formula)) return false;
        foreach (var next in formula.Names)
        {
            if (next == start) return true;
            if (seen.Add(next) && DependsOn(start, next, formulas, seen)) return true;
        }
        return false;
    }

    /// <summary>The gauge's values that come from the sim, in a fixed order (by value name).</summary>
    /// <exception cref="InvalidOperationException">A sim value has no unit.</exception>
    public IReadOnlyList<(string Name, string Sim, string Unit)> SimValues() =>
        [.. Values
            .Where(v => !string.IsNullOrWhiteSpace(v.Value.Sim))
            .OrderBy(v => v.Key, StringComparer.Ordinal)
            .Select(v => (v.Key, v.Value.Sim!.Trim(),
                string.IsNullOrWhiteSpace(v.Value.Unit)
                    ? throw new InvalidOperationException($"Gauge '{Name}' value '{v.Key}' comes from the sim but has no unit. Add one, for example \"unit\": \"knots\".")
                    : v.Value.Unit.Trim()))];

    public ScaleSpec Scale(string name) =>
        Scales.TryGetValue(name, out var scale)
            ? scale
            : throw new KeyNotFoundException($"Gauge '{Name}' uses scale '{name}', but no such scale is defined.");
}

public sealed record CanvasSpec
{
    public int Width { get; init; } = 320;
    public int Height { get; init; } = 240;
    public string Background { get; init; } = "#000000";
}

public sealed record ValueSpec
{
    public string? Sim { get; init; }
    public string? Unit { get; init; }
    public double Default { get; init; }

    /// <summary>
    /// Worked out from other values instead of read from the sim, for example
    /// <c>"wrap360(vor1_radial + 180 - heading)"</c>. See <see cref="ValueExpression"/>.
    /// </summary>
    public string? Expr { get; init; }
}

public sealed record ScaleSpec
{
    public double Min { get; init; }
    public double Max { get; init; }
    public float StartAngle { get; init; }
    public float EndAngle { get; init; }

    /// <summary>
    /// For uneven scales: pairs of [value, angle], in increasing value order. Angles between
    /// the listed values are filled in evenly. When set, min/max/startAngle/endAngle are not needed.
    /// For a bent scale (one that isn't a circle around the pivot, like the DC-6's twin pressure
    /// dials) each point is [value, angle, distance from the pivot]; see <see cref="IsBent"/>.
    /// </summary>
    public double[][]? Points { get; init; }

    /// <summary>
    /// The scale line's distance from the pivot changes along the scale. Layers on a bent scale
    /// measure their radius (and needles their length) from the scale line instead of the pivot.
    /// </summary>
    public bool IsBent => Points is { Length: > 0 } p && p.All(x => x is { Length: 3 });

    /// <summary>Distance of the scale line from the pivot at this value (bent scales only, else 0).</summary>
    public float DistanceFor(double value)
    {
        if (!IsBent) return 0;
        var points = Points!;
        if (Wrap && High > Low)
        {
            var span = High - Low;
            value = Low + ((value - Low) % span + span) % span;
        }
        if (value <= points[0][0]) return (float)points[0][2];
        for (var i = 1; i < points.Length; i++)
        {
            if (value <= points[i][0])
            {
                var (v0, r0, v1, r1) = (points[i - 1][0], points[i - 1][2], points[i][0], points[i][2]);
                return (float)(v1 == v0 ? r1 : r0 + (value - v0) / (v1 - v0) * (r1 - r0));
            }
        }
        return (float)points[^1][2];
    }

    /// <summary>Where a layer's radius lands: from the pivot, or from the scale line on a bent scale.</summary>
    public float Reach(double value, float radius) => IsBent ? DistanceFor(value) + radius : radius;

    /// <summary>
    /// The scale goes round and round instead of stopping at the ends, for example an altimeter
    /// hand that turns once per 1000 ft (min 0, max 1000, 0° to 360°).
    /// </summary>
    public bool Wrap { get; init; }

    /// <summary>Lowest value on the scale.</summary>
    public double Low => Points is { Length: > 0 } p ? p[0][0] : Min;

    /// <summary>Highest value on the scale.</summary>
    public double High => Points is { Length: > 0 } p ? p[^1][0] : Max;

    /// <summary>
    /// Angle in degrees, clockwise from 12 o'clock. Values outside the range stop at the ends
    /// (or go round again, for a wrapping scale). Also used as a distance in pixels when a
    /// scale moves a group sideways or up and down.
    /// </summary>
    public float AngleFor(double value)
    {
        var low = Low;
        var high = High;
        if (Wrap && high > low)
        {
            var span = high - low;
            value = low + ((value - low) % span + span) % span;
        }

        if (Points is { Length: > 0 } points)
        {
            if (value <= points[0][0]) return (float)points[0][1];
            for (var i = 1; i < points.Length; i++)
            {
                if (value <= points[i][0])
                {
                    var (v0, a0, v1, a1) = (points[i - 1][0], points[i - 1][1], points[i][0], points[i][1]);
                    return (float)(v1 == v0 ? a1 : a0 + (value - v0) / (v1 - v0) * (a1 - a0));
                }
            }
            return (float)points[^1][1];
        }

        if (Max == Min) return StartAngle;
        var t = Math.Clamp((value - Min) / (Max - Min), 0, 1);
        return StartAngle + (float)t * (EndAngle - StartAngle);
    }

    /// <summary>Checks the scale makes sense; returns a plain description of the problem, or null.</summary>
    public string? Problem()
    {
        if (Points is null) return null;
        if (Points.Length < 2) return "needs at least two [value, angle] points";
        if (Points.Any(p => p is null || p.Length is < 2 or > 3)) return "each point must be [value, angle] (or [value, angle, distance] for a bent scale)";
        if (Points.Select(p => p.Length).Distinct().Count() > 1) return "use [value, angle] for every point, or [value, angle, distance] for every point; don't mix them";
        for (var i = 1; i < Points.Length; i++)
            if (Points[i][0] < Points[i - 1][0]) return "points must be in increasing value order";
        return null;
    }
}

/// <summary>A sim event to send when a knob clicks or a button is pressed.</summary>
/// <summary>One knob mode: the S button that picks it, a short name, and what the knobs (and buttons) do in it.</summary>
public sealed record KnobMode
{
    public string Name { get; init; } = "";
    public string Button { get; init; } = "";
    public Dictionary<string, ControlSpec> Controls { get; init; } = [];
}

public sealed record ControlSpec
{
    /// <summary>Sim event name, e.g. KOHLSMAN_INC, VOR1_OBI_DEC, HEADING_BUG_INC.</summary>
    public string Event { get; init; } = "";

    /// <summary>Number sent with the event (most knob events ignore it).</summary>
    public uint Data { get; init; }

    /// <summary>Send the event this many times per click, for a faster knob.</summary>
    public int Repeat { get; init; } = 1;

    /// <summary>
    /// Instead of an event: a plane's own value to change (an "L:" name, e.g. L:FSS_B727_PCP_EPR_KNOB1), for
    /// add-on planes whose knobs are values rather than sim events. Each click adds <see cref="Add"/>
    /// (times <see cref="Repeat"/>) to it. Several values, separated by commas, all change together (e.g. the 727's
    /// three EPR bug knobs from one FIP knob).
    /// </summary>
    public string? Set { get; init; }

    /// <summary>The plane values <see cref="Set"/> names (one or more).</summary>
    public IEnumerable<string> SetNames => (Set ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>What a click adds to <see cref="Set"/> (use a negative number to subtract).</summary>
    public double Add { get; init; } = 1;

    /// <summary>True when this control changes a plane value (<see cref="Set"/>) rather than sending an event.</summary>
    public bool ChangesValue => !string.IsNullOrEmpty(Set);
}

/// <summary>Links a value to a scale; used to turn or slide a group of layers.</summary>
public sealed record ValueLink
{
    public string Value { get; init; } = "";
    public string Scale { get; init; } = "";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(RectLayer), "rect")]
[JsonDerivedType(typeof(CircleLayer), "circle")]
[JsonDerivedType(typeof(ScrewLayer), "screw")]
[JsonDerivedType(typeof(TicksLayer), "ticks")]
[JsonDerivedType(typeof(LabelsLayer), "labels")]
[JsonDerivedType(typeof(ArcLayer), "arc")]
[JsonDerivedType(typeof(TextLayer), "text")]
[JsonDerivedType(typeof(NeedleLayer), "needle")]
[JsonDerivedType(typeof(HubLayer), "hub")]
[JsonDerivedType(typeof(GlareLayer), "glare")]
[JsonDerivedType(typeof(GroupLayer), "group")]
public abstract record Layer
{
    /// <summary>Dial center for this layer; falls back to the gauge's center.</summary>
    public float[]? Center { get; init; }
}

public sealed record RectLayer : Layer
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
    public float Radius { get; init; }
    public string? Fill { get; init; }
    public string? Stroke { get; init; }
    public float StrokeWidth { get; init; } = 1;
}

public sealed record CircleLayer : Layer
{
    public float R { get; init; }
    public string? Fill { get; init; }
    public string? FillTop { get; init; }
    public string? FillBottom { get; init; }
    public string? Stroke { get; init; }
    public float StrokeWidth { get; init; } = 1;
}

public sealed record ScrewLayer : Layer
{
    public float X { get; init; }
    public float Y { get; init; }
    public string Kind { get; init; } = "slot";
}

/// <summary>Shared fields for layers that place things along a scale.</summary>
public abstract record ScaleStepsLayer : Layer
{
    public string Scale { get; init; } = "";
    public float Radius { get; init; }
    public double? Every { get; init; }
    public double? From { get; init; }
    public double? To { get; init; }
    public double[]? At { get; init; }
    public string Color { get; init; } = "#FFFFFF";

    public IEnumerable<double> Steps(ScaleSpec scale)
    {
        if (At is not null) return At;
        if (Every is not { } every || every <= 0) return [];
        var from = From ?? scale.Low;
        var to = To ?? scale.High;
        var count = (int)Math.Floor((to - from) / every + 1e-9);
        return Enumerable.Range(0, count + 1).Select(i => from + i * every);
    }
}

public sealed record TicksLayer : ScaleStepsLayer
{
    public float Length { get; init; }
    public float Width { get; init; } = 1;

    /// <summary>"line" (default), or "triangle": a filled marker pointing at the center, <see cref="Width"/> wide at the rim.</summary>
    public string Shape { get; init; } = "line";
}

public sealed record LabelsLayer : ScaleStepsLayer
{
    public float Size { get; init; } = 16;
    public double DivideBy { get; init; } = 1;

    /// <summary>Text to show instead of the number for some values, e.g. { "0": "N", "90": "E" }.</summary>
    public Dictionary<string, string>? Texts { get; init; }

    /// <summary>"upright" (default): text stays level. "radial": text turns so its top faces out from the center, like on a compass card.</summary>
    public string Orient { get; init; } = "upright";
}

public sealed record ArcLayer : Layer
{
    public string Scale { get; init; } = "";
    public double From { get; init; }
    public double To { get; init; }
    /// <summary>
    /// Optional: a value that sets where the arc ends, live, instead of <see cref="To"/>. A wide arc
    /// from the scale's start then fills in like the "pie" sweep on modern engine displays.
    /// </summary>
    public string? ToValue { get; init; }
    public float Radius { get; init; }
    public float Width { get; init; } = 1;
    public string Color { get; init; } = "#FFFFFF";
}

public sealed record TextLayer : Layer
{
    public string Text { get; init; } = "";

    /// <summary>Show this value instead of fixed text, formatted with <see cref="Format"/>.</summary>
    public string? Value { get; init; }

    /// <summary>Number format for <see cref="Value"/>, e.g. "0.00" or "000". Default "0".</summary>
    public string Format { get; init; } = "0";
    public float X { get; init; }
    public float Y { get; init; }
    public float Size { get; init; } = 14;
    public string Color { get; init; } = "#FFFFFF";
    public string Align { get; init; } = "center";

    /// <summary>Slanted text, e.g. for a maker's signature on a dial.</summary>
    public bool Italic { get; init; }
}

public sealed record NeedleLayer : Layer
{
    public string Scale { get; init; } = "";
    public string Value { get; init; } = "";
    public float Length { get; init; }
    public float Tail { get; init; }
    public float Width { get; init; } = 3;
    public string Color { get; init; } = "#FFFFFF";
    public string TailColor { get; init; } = "#161616";
    public bool Shadow { get; init; } = true;

    /// <summary>
    /// "classic" (default): tapered pointer. "arrow": thin line with an arrowhead and a forked
    /// tail, as on RMI needle 1. "doubleArrow": two parallel lines with an arrowhead, as on RMI needle 2.
    /// "pointer": outlined blade with a dark counterweight arm (<see cref="TailColor"/>) behind the
    /// pivot, as on the DC-6's engine dials; <see cref="Width"/> is half the blade's width.
    /// </summary>
    public string Style { get; init; } = "classic";

    /// <summary>Short text (an engine number) in a small disc on the needle, for two-needle dials.</summary>
    public string? Tag { get; init; }
}

public sealed record HubLayer : Layer
{
    public float R { get; init; } = 10;
    public string Fill { get; init; } = "#1A1A1A";
    public string? Stroke { get; init; }
}

public sealed record GlareLayer : Layer
{
    public float R { get; init; }
    public float Opacity { get; init; } = 0.2f;
}

/// <summary>
/// A set of layers drawn together, optionally turned and/or slid by live values: compass cards,
/// course bars, glideslope pointers. Turning is around the group's center.
/// </summary>
public sealed record GroupLayer : Layer
{
    public List<Layer> Layers { get; init; } = [];

    /// <summary>Turns the group by the scale's angle for this value.</summary>
    public ValueLink? Rotate { get; init; }

    /// <summary>Slides the group right (positive) by the scale's output, in pixels.</summary>
    public ValueLink? MoveX { get; init; }

    /// <summary>Slides the group down (positive) by the scale's output, in pixels.</summary>
    public ValueLink? MoveY { get; init; }

    /// <summary>Only draw inside a circle of this radius around the center (e.g. the dial window).</summary>
    public float? Clip { get; init; }

    /// <summary>Moves the whole group by [x, y] pixels (used to place a gauge inside a combined gauge).</summary>
    public float[]? Offset { get; init; }

    /// <summary>Draws the whole group bigger or smaller, e.g. 0.66 for two gauges side by side.</summary>
    public float Zoom { get; init; } = 1;

    /// <summary>Only draw inside this rectangle [x, y, width, height], in the group's own coordinates.</summary>
    public float[]? ClipBox { get; init; }
}
