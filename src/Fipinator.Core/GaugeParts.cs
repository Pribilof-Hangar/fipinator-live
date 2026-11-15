// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fipinator.Core;

/// <summary>The case a generated gauge sits in.</summary>
public enum GaugeLook
{
    /// <summary>Light-grey panel, round case (like the Cessna 404 Titan's panel).</summary>
    LightTwin,
    /// <summary>Dark square plate with slotted screws on a dark panel, like the owner's DC-6 gauges.</summary>
    Dc6,
    /// <summary>
    /// Light grey panel, steel screws, black rim, cream markings and counterweight needles: the
    /// look of the owner's DC-6 Gauges app (and the DC-6 engine gauges). Default for the classic pack.
    /// </summary>
    Classic,
    /// <summary>Black panel and case, white markings and white needles, as in most Cessnas and Pipers.</summary>
    Black,
    /// <summary>727 (old Boeing): grey-green panel, square black instrument case, black dial, white markings.</summary>
    Boeing727,
    /// <summary>737 Classic (-300/-400/-500): tan panel, square black instrument case, black dial, white markings.</summary>
    Boeing737Classic,
}

/// <summary>
/// Shared pieces for the Gauge Maker's built-in recipes (fuel, turn and slip, flaps...), so every
/// generated gauge in the same look matches the others on the FIPs. Original artwork.
/// Positions are given for the light-twin case (dial face radius 113) and scaled to the chosen
/// case, so a recipe works in either look.
/// </summary>
public sealed class GaugeParts
{
    public const string Ink = "#EDEAE2", SoftInk = "#D8D4CA", Red = "#D3362B", Green = "#3E9E4A", Yellow = "#E0B13C";
    private const double DesignFace = 113;

    public GaugeParts(GaugeLook look) => Look = look;

    public GaugeLook Look { get; }

    /// <summary>Radius of the black dial face.</summary>
    public double Face => Look switch { GaugeLook.Dc6 => 107, GaugeLook.Classic or GaugeLook.Black or GaugeLook.Boeing727 or GaugeLook.Boeing737Classic => 101, _ => DesignFace };

    /// <summary>The Boeing looks: square black case on a coloured panel, white ink.</summary>
    private bool Boeing => Look is GaugeLook.Boeing727 or GaugeLook.Boeing737Classic;

    /// <summary>How much smaller (or larger) this case is than the design size.</summary>
    public double K => Face / DesignFace;

    /// <summary>
    /// Panel colour. The two Boeing colours are drawn from cockpit photos by eye (grey-green 727,
    /// tan 737 Classic), not measured; FIPinator 3000 users can change any colour.
    /// </summary>
    public string Background => Look switch
    {
        GaugeLook.Dc6 => "#5A6064", GaugeLook.Classic => "#8D908E", GaugeLook.Black => "#1A1B1D",
        GaugeLook.Boeing727 => "#66766C", GaugeLook.Boeing737Classic => "#9C8C70", _ => "#8F9497",
    };

    /// <summary>Colour of scale marks and numbers in this look.</summary>
    public string InkColor => Look switch { GaugeLook.Classic => "#EFE6CF", GaugeLook.Black => "#F4F4F2", _ when Boeing => "#F4F4F2", _ => Ink };

    /// <summary>Colour of words on the dial in this look.</summary>
    public string TextColor => Look switch { GaugeLook.Classic => "#EFE6CF", GaugeLook.Black => "#E2E2E0", _ when Boeing => "#E6E6E4", _ => SoftInk };

    /// <summary>
    /// A needle in this look: the counterweight pointer for Classic, and in every look whenever an
    /// engine number (<paramref name="tag"/>) has to show on it; the tapered needle otherwise.
    /// </summary>
    public JsonObject Needle(string scale, string value, double length, JsonArray? center = null, string? tag = null, double width = 3.2, double tail = 24)
    {
        var n = Look == GaugeLook.Classic || tag is not null
            ? new JsonObject { ["type"] = "needle", ["style"] = "pointer", ["scale"] = scale, ["value"] = value, ["length"] = R(length), ["width"] = width, ["tail"] = R(tail), ["color"] = Look == GaugeLook.Classic ? "#EFE9D6" : "#F4F4F2", ["tailColor"] = "#2A2B2C", ["shadow"] = false }
            : new JsonObject { ["type"] = "needle", ["scale"] = scale, ["value"] = value, ["length"] = R(length), ["tail"] = R(tail * 0.6), ["width"] = width + 1, ["color"] = "#F4F2EC", ["tailColor"] = "#161616", ["shadow"] = true };
        if (center is not null) n["center"] = center.DeepClone();
        if (tag is not null) n["tag"] = tag;
        return n;
    }

    /// <summary>The cap over a needle pivot in this look.</summary>
    public IEnumerable<JsonObject> Hub(JsonArray? center = null, double r = 8)
    {
        JsonObject With(JsonObject o) { if (center is not null) o["center"] = center.DeepClone(); return o; }
        if (Look == GaugeLook.Classic)
        {
            yield return With(new JsonObject { ["type"] = "circle", ["r"] = r, ["fill"] = "#1A1A1A", ["stroke"] = "#555555" });
            yield return With(new JsonObject { ["type"] = "circle", ["r"] = Math.Round(r * 0.33, 2), ["fill"] = "#777777" });
        }
        else yield return With(new JsonObject { ["type"] = "hub", ["r"] = r, ["fill"] = "#141414", ["stroke"] = "#4A4A4A" });
    }

    /// <summary>A distance from the centre, scaled to this case.</summary>
    public double R(double designRadius) => Math.Round(designRadius * K, 2);

    /// <summary>A point given as an offset from the centre (160, 120), scaled to this case.</summary>
    public JsonArray P(double dx, double dy) => new(Math.Round(160 + dx * K, 2), Math.Round(120 + dy * K, 2));

    /// <summary>Panel, mounting blocks, screws, round case and black dial face.</summary>
    public JsonArray Case()
    {
        if (Look == GaugeLook.Black)
        {
            return
            [
                new JsonObject { ["type"] = "screw", ["x"] = 58, ["y"] = 18, ["kind"] = "slot" },
                new JsonObject { ["type"] = "screw", ["x"] = 262, ["y"] = 18, ["kind"] = "slot" },
                new JsonObject { ["type"] = "screw", ["x"] = 58, ["y"] = 222, ["kind"] = "slot" },
                new JsonObject { ["type"] = "screw", ["x"] = 262, ["y"] = 222, ["kind"] = "slot" },
                new JsonObject { ["type"] = "circle", ["r"] = 114, ["fill"] = "#60000000" },
                new JsonObject { ["type"] = "circle", ["r"] = 112, ["fillTop"] = "#2E2F31", ["fillBottom"] = "#030303", ["stroke"] = "#3A3B3D", ["strokeWidth"] = 1 },
                new JsonObject { ["type"] = "circle", ["r"] = 101, ["fillTop"] = "#141414", ["fillBottom"] = "#030303", ["stroke"] = "#000000", ["strokeWidth"] = 2 },
                new JsonObject { ["type"] = "circle", ["r"] = 103, ["stroke"] = "#80505254", ["strokeWidth"] = 1 },
            ];
        }
        if (Boeing)
        {
            return
            [
                new JsonObject { ["type"] = "rect", ["x"] = 0, ["y"] = 0, ["width"] = 320, ["height"] = 240, ["fill"] = Background },
                new JsonObject { ["type"] = "rect", ["x"] = 42, ["y"] = 2, ["width"] = 236, ["height"] = 236, ["radius"] = 14, ["fill"] = "#17181A", ["stroke"] = "#050505", ["strokeWidth"] = 1.5 },
                new JsonObject { ["type"] = "screw", ["x"] = 58, ["y"] = 18, ["kind"] = "slot" },
                new JsonObject { ["type"] = "screw", ["x"] = 262, ["y"] = 18, ["kind"] = "slot" },
                new JsonObject { ["type"] = "screw", ["x"] = 58, ["y"] = 222, ["kind"] = "slot" },
                new JsonObject { ["type"] = "screw", ["x"] = 262, ["y"] = 222, ["kind"] = "slot" },
                new JsonObject { ["type"] = "circle", ["r"] = 109, ["fillTop"] = "#050505", ["fillBottom"] = "#2A2B2D", ["stroke"] = "#000000", ["strokeWidth"] = 1 },
                new JsonObject { ["type"] = "circle", ["r"] = 101, ["fillTop"] = "#141414", ["fillBottom"] = "#030303", ["stroke"] = "#000000", ["strokeWidth"] = 2 },
            ];
        }
        if (Look == GaugeLook.Classic)
        {
            return
            [
                new JsonObject { ["type"] = "screw", ["x"] = 58, ["y"] = 18, ["kind"] = "steel" },
                new JsonObject { ["type"] = "screw", ["x"] = 262, ["y"] = 18, ["kind"] = "steel" },
                new JsonObject { ["type"] = "screw", ["x"] = 58, ["y"] = 222, ["kind"] = "steel" },
                new JsonObject { ["type"] = "screw", ["x"] = 262, ["y"] = 222, ["kind"] = "steel" },
                new JsonObject { ["type"] = "circle", ["r"] = 114, ["fill"] = "#40000000" },
                new JsonObject { ["type"] = "circle", ["r"] = 112, ["fillTop"] = "#3B3C3D", ["fillBottom"] = "#050505" },
                new JsonObject { ["type"] = "circle", ["r"] = 101, ["fillTop"] = "#1E1E1E", ["fillBottom"] = "#060606", ["stroke"] = "#000000", ["strokeWidth"] = 2 },
                new JsonObject { ["type"] = "circle", ["r"] = 103, ["stroke"] = "#994A4B4C", ["strokeWidth"] = 1 },
            ];
        }
        if (Look == GaugeLook.Dc6)
        {
            return
            [
                new JsonObject { ["type"] = "rect", ["x"] = 40, ["y"] = 0, ["width"] = 240, ["height"] = 240, ["radius"] = 18, ["fill"] = "#6C7378", ["stroke"] = "#41474A", ["strokeWidth"] = 1.5 },
                new JsonObject { ["type"] = "screw", ["x"] = 60, ["y"] = 20, ["kind"] = "boss" },
                new JsonObject { ["type"] = "screw", ["x"] = 260, ["y"] = 20, ["kind"] = "boss" },
                new JsonObject { ["type"] = "screw", ["x"] = 58, ["y"] = 222, ["kind"] = "slot" },
                new JsonObject { ["type"] = "screw", ["x"] = 262, ["y"] = 222, ["kind"] = "slot" },
                new JsonObject { ["type"] = "circle", ["r"] = 115, ["fillTop"] = "#303030", ["fillBottom"] = "#0A0A0A", ["stroke"] = "#2A2A2A", ["strokeWidth"] = 1.2 },
                new JsonObject { ["type"] = "circle", ["r"] = 107, ["fill"] = "#0B0B0B", ["stroke"] = "#202020", ["strokeWidth"] = 3 },
            ];
        }
        return
        [
            new JsonObject { ["type"] = "rect", ["x"] = 0, ["y"] = 0, ["width"] = 320, ["height"] = 240, ["fill"] = "#8F9497" },
            new JsonObject { ["type"] = "screw", ["x"] = 62, ["y"] = 22, ["kind"] = "boss" },
            new JsonObject { ["type"] = "screw", ["x"] = 258, ["y"] = 22, ["kind"] = "boss" },
            new JsonObject { ["type"] = "screw", ["x"] = 58, ["y"] = 222, ["kind"] = "slot" },
            new JsonObject { ["type"] = "screw", ["x"] = 262, ["y"] = 222, ["kind"] = "slot" },
            new JsonObject { ["type"] = "circle", ["r"] = 119, ["fillTop"] = "#4A4D50", ["fillBottom"] = "#141516", ["stroke"] = "#2A2C2E", ["strokeWidth"] = 1.2 },
            new JsonObject { ["type"] = "circle", ["r"] = 113, ["fill"] = "#0C0C0C", ["stroke"] = "#1C1C1C", ["strokeWidth"] = 2 },
        ];
    }

    public JsonObject Glass() => new() { ["type"] = "glare", ["r"] = Face - 1, ["opacity"] = Look switch { GaugeLook.Dc6 => 0.18, GaugeLook.Classic => 0.16, GaugeLook.Black => 0.12, _ => 0.14 } };

    /// <summary>Fixed text at an offset from the centre (scaled to this case), in this look's text colour.</summary>
    public JsonObject Text(string text, double dx, double dy, double size, string? color = null)
    {
        var p = P(dx, dy);
        return new() { ["type"] = "text", ["text"] = text, ["x"] = p[0]!.DeepClone(), ["y"] = p[1]!.DeepClone(), ["size"] = size, ["color"] = color ?? TextColor };
    }

    /// <summary>
    /// Puts an existing gauge's dial into this look's case: drops the gauge's own panel, screws
    /// and rim (everything up to its dial face), shrinks or grows the rest to fit this look's face,
    /// and adds this look's glass. Values, scales and knobs stay as they were.
    /// </summary>
    /// <exception cref="ArgumentException">The gauge has no round dial face near the start to replace.</exception>
    public static string Reskin(string json, GaugeLook look, string? name = null)
    {
        var gauge = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
        var layers = gauge["layers"]!.AsArray().OfType<JsonObject>().ToList();
        // The case ends at the last big circle (the dial face) among the first layers.
        var faceIndex = -1;
        double sourceFace = 0;
        for (var i = 0; i < Math.Min(layers.Count, 12); i++)
        {
            if (layers[i]["type"]?.GetValue<string>() == "circle" && layers[i]["r"]?.GetValue<double>() is >= 95 and < 113.5 and var r && layers[i]["center"] is null)
            {
                faceIndex = i;
                sourceFace = r;
            }
        }
        var parts = new GaugeParts(look);
        if (faceIndex < 0)
        {
            ReskinPlate(gauge, layers, parts, name);
            return gauge.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        // Words that sat outside the dial (on the old square plate, e.g. "<< ADF 1") keep their size
        // and move to the screen's outer corners, clear of the new case's screws.
        bool OutsideFace(JsonObject l) =>
            l["type"]?.GetValue<string>() == "text" && l["x"]?.GetValue<double>() is { } x && l["y"]?.GetValue<double>() is { } y
            && Math.Sqrt((x - 160) * (x - 160) + (y - 120) * (y - 120)) > sourceFace + 4;
        var rest = layers.Skip(faceIndex + 1).Where(l => l["type"]?.GetValue<string>() != "glare").ToList();
        var content = new JsonArray([.. rest.Where(l => !OutsideFace(l)).Select(l => l.DeepClone())]);
        var outside = rest.Where(OutsideFace).Select(l =>
        {
            var copy = l.DeepClone().AsObject();
            var align = copy["align"]?.GetValue<string>() ?? "center";
            if (align == "left") copy["x"] = 4;
            else if (align == "right") copy["x"] = 316;
            return copy;
        }).ToList();

        foreach (var node in content.OfType<JsonObject>().Concat(outside)) parts.Recolor(node);
        var zoom = Math.Round(parts.Face / sourceFace, 4);
        var result = parts.Case();
        result.Add(new JsonObject
        {
            ["type"] = "group", ["offset"] = new JsonArray(Math.Round(160 * (1 - zoom), 2), Math.Round(120 * (1 - zoom), 2)), ["zoom"] = zoom, ["layers"] = content,
        });
        result.Add(parts.Glass());
        foreach (var o in outside) result.Add(o);

        gauge["layers"] = result;
        gauge["canvas"] = new JsonObject { ["width"] = 320, ["height"] = 240, ["background"] = parts.Background };
        if (name is not null) gauge["name"] = name;
        return gauge.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>The whole gauge file as text.</summary>
    public string Gauge(string name, string description, JsonObject values, JsonObject scales, JsonArray layers, string font)
    {
        var gauge = new JsonObject
        {
            ["format"] = 1,
            ["name"] = name,
            ["author"] = "Made with FIPinator 3000",
            ["description"] = description,
            ["canvas"] = new JsonObject { ["width"] = 320, ["height"] = 240, ["background"] = Background },
            ["center"] = new JsonArray(160, 120),
            ["font"] = font,
            ["values"] = values,
            ["scales"] = scales,
            ["layers"] = layers,
        };
        return gauge.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Gives markings this look's colour: near-white and cream colours (numbers, ticks, words,
    /// needles) become this look's ink. Coloured marks (green, yellow, red, blue, orange) and dark
    /// parts are left alone. Walks into groups.
    /// </summary>
    internal void Recolor(JsonObject layer)
    {
        foreach (var key in new[] { "color", "fill" })
        {
            if (layer[key]?.GetValue<string>() is not { } hex || !IsLightNeutral(hex, out var alpha)) continue;
            layer[key] = alpha is null ? InkColor : "#" + alpha + InkColor[1..];
        }
        foreach (var child in (layer["layers"] as JsonArray ?? []).OfType<JsonObject>()) Recolor(child);
    }

    private static bool IsLightNeutral(string hex, out string? alpha)
    {
        alpha = null;
        var h = hex.TrimStart('#');
        if (h.Length == 8) { alpha = h[..2]; h = h[2..]; }
        if (h.Length != 6 || !int.TryParse(h, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgb)) return false;
        int r = rgb >> 16, g = (rgb >> 8) & 255, b = rgb & 255;
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        return max >= 190 && max - min <= 45;
    }

    /// <summary>
    /// Plate-style gauges (a rectangular plate with readout windows, like the DME/GPS): keep the
    /// windows and readouts, give the plate this look's colours and screws.
    /// </summary>
    private static void ReskinPlate(JsonObject gauge, List<JsonObject> layers, GaugeParts parts, string? name)
    {
        var plate = layers.FirstOrDefault();
        if (plate?["type"]?.GetValue<string>() != "rect" || plate["width"]?.GetValue<double>() < 200 || plate["height"]?.GetValue<double>() < 150)
            throw new ArgumentException($"'{gauge["name"]}' has no round dial face or plate to put in a new case.");

        var classic = parts.Look == GaugeLook.Classic;
        plate["fill"] = classic ? "#1B1C1D" : parts.Look switch { GaugeLook.Dc6 => "#6C7378", GaugeLook.Black or GaugeLook.Boeing727 or GaugeLook.Boeing737Classic => "#0E0F10", _ => "#2E3134" };
        plate["stroke"] = classic ? "#050505" : "#1C1E20";
        foreach (var screw in layers.Where(l => l["type"]?.GetValue<string>() == "screw"))
            screw["kind"] = classic ? "steel" : "slot";
        foreach (var layer in layers.Skip(1).Where(l => l["type"]?.GetValue<string>() != "screw")) parts.Recolor(layer);
        gauge["layers"] = new JsonArray([.. layers.Select(l => l.DeepClone())]);
        gauge["canvas"] = new JsonObject { ["width"] = 320, ["height"] = 240, ["background"] = parts.Background };
        if (name is not null) gauge["name"] = name;
    }

    public static string Key(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// Two-tank fuel gauge: left tank needle on the left, right tank on the right, each reading
/// E, 1/2, F with a red band near empty.
/// </summary>
public static class FuelGenerator
{
    public static string Create(double gallonsPerTank, string name, GaugeLook look = GaugeLook.LightTwin, string font = "BarlowCondensed-Medium.ttf")
    {
        if (gallonsPerTank <= 0) throw new ArgumentException("A tank must hold more than 0 gallons.", nameof(gallonsPerTank));
        var full = gallonsPerTank;
        var g = new GaugeParts(look);
        var layers = g.Case();

        void Side(string scale, JsonArray pivot, string value)
        {
            JsonNode Pv() => pivot.DeepClone();
            layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = scale, ["from"] = 0, ["to"] = full * 0.12, ["radius"] = g.R(80), ["width"] = 6, ["color"] = GaugeParts.Red, ["center"] = Pv() });
            layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = scale, ["every"] = full / 8, ["from"] = 0, ["to"] = full, ["radius"] = g.R(84), ["length"] = 8, ["width"] = 1.8, ["color"] = g.InkColor, ["center"] = Pv() });
            layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = scale, ["every"] = full / 4, ["from"] = 0, ["to"] = full, ["radius"] = g.R(84), ["length"] = 15, ["width"] = 3, ["color"] = g.InkColor, ["center"] = Pv() });
            layers.Add(new JsonObject
            {
                ["type"] = "labels", ["scale"] = scale, ["at"] = new JsonArray(0, full / 2, full), ["radius"] = g.R(57), ["size"] = 19, ["color"] = g.InkColor, ["center"] = Pv(),
                ["texts"] = new JsonObject { [GaugeParts.Key(0)] = "E", [GaugeParts.Key(full / 2)] = "1/2", [GaugeParts.Key(full)] = "F" },
            });
            layers.Add(g.Needle(scale, value, 78, pivot, width: g.Look == GaugeLook.Classic ? 2.6 : 3, tail: 0));
            foreach (var h in g.Hub(pivot, 6)) layers.Add(h);
        }

        // Pivots sit either side of the middle; the needles sweep up the outer sides of the dial.
        Side("left", g.P(-26, 4), "left");
        Side("right", g.P(26, 4), "right");
        layers.Add(g.Text("FUEL", 0, -66, 17));
        layers.Add(g.Text("L", -26, 32, 15));
        layers.Add(g.Text("R", 26, 32, 15));
        layers.Add(g.Text($"{Math.Round(full)} U.S. GAL EACH", 0, 76, 10));
        layers.Add(g.Glass());

        var values = new JsonObject
        {
            ["left"] = new JsonObject { ["sim"] = "FUEL LEFT QUANTITY", ["unit"] = "gallons", ["default"] = Math.Round(full * 0.7) },
            ["right"] = new JsonObject { ["sim"] = "FUEL RIGHT QUANTITY", ["unit"] = "gallons", ["default"] = Math.Round(full * 0.55) },
        };
        var scales = new JsonObject
        {
            ["left"] = new JsonObject { ["min"] = 0, ["max"] = full, ["startAngle"] = 215, ["endAngle"] = 325 },
            ["right"] = new JsonObject { ["min"] = 0, ["max"] = full, ["startAngle"] = 145, ["endAngle"] = 35 },
        };
        return g.Gauge(name, $"Two-tank fuel gauge, {Math.Round(full)} gallons per tank, red band near empty.", values, scales, layers, font);
    }
}

/// <summary>
/// Classic turn and slip indicator: a needle that leans with the rate of turn (marks at a
/// standard-rate, 2-minute turn) and a ball rolling in a curved glass tube.
/// </summary>
public static class TurnSlipGenerator
{
    // Standard-rate turn: 3 degrees per second, in radians per second (what the sim reports).
    private const double StandardRate = 0.05236;

    public static string Create(string name, GaugeLook look = GaugeLook.LightTwin, string font = "BarlowCondensed-Medium.ttf")
    {
        var g = new GaugeParts(look);
        var layers = g.Case();
        var pivot = g.P(0, -8);
        var tubeCenter = g.P(0, -68); // the tube is a slice of a big circle centred above the dial
        var tubeRadius = g.R(136);
        const double tubeHalfAngle = 20;

        // Turn marks ("doghouses") at a standard-rate turn each way, and a small center mark.
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "turn", ["at"] = new JsonArray(-StandardRate, StandardRate), ["radius"] = g.R(92), ["length"] = 16, ["width"] = 15, ["shape"] = "triangle", ["color"] = g.InkColor, ["center"] = pivot.DeepClone() });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "turn", ["at"] = new JsonArray(0), ["radius"] = g.R(92), ["length"] = 12, ["width"] = 4, ["color"] = g.InkColor, ["center"] = pivot.DeepClone() });
        layers.Add(g.Text("L", -44, -76, 18, g.InkColor));
        layers.Add(g.Text("R", 44, -76, 18, g.InkColor));
        layers.Add(g.Text("TURN AND SLIP", 0, 26, 12));
        layers.Add(g.Text("2 MIN", 0, -50, 10));

        // Glass tube: dark outline, pale liquid, rounded ends, a soft highlight along the top
        // edge, and two black reference wires in the middle.
        var tc = (X: tubeCenter[0]!.GetValue<double>(), Y: tubeCenter[1]!.GetValue<double>());
        foreach (var (width, color) in new[] { (24.0, "#3A3A36"), (19.0, "#CFC7A6") })
        {
            layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = "tube", ["from"] = -1, ["to"] = 1, ["radius"] = tubeRadius, ["width"] = width, ["color"] = color, ["center"] = tubeCenter.DeepClone() });
            foreach (var side in new[] { -1, 1 })
            {
                var a = tubeHalfAngle * Math.PI / 180 * side;
                var end = new JsonArray(Math.Round(tc.X + tubeRadius * Math.Sin(a), 2), Math.Round(tc.Y + tubeRadius * Math.Cos(a), 2));
                layers.Add(new JsonObject { ["type"] = "circle", ["r"] = width / 2, ["fill"] = color, ["center"] = end });
            }
        }
        layers.Add(new JsonObject { ["type"] = "arc", ["scale"] = "tube", ["from"] = -0.9, ["to"] = 0.9, ["radius"] = tubeRadius - 6, ["width"] = 2.5, ["color"] = "#E9E3CB", ["center"] = tubeCenter.DeepClone() });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "tube", ["at"] = new JsonArray(-0.27, 0.27), ["radius"] = tubeRadius + 12, ["length"] = 24, ["width"] = 2.2, ["color"] = "#1A1A1A", ["center"] = tubeCenter.DeepClone() });

        // The ball: a group swung around the tube's centre, so it follows the curve.
        layers.Add(new JsonObject
        {
            ["type"] = "group", ["center"] = tubeCenter.DeepClone(), ["rotate"] = new JsonObject { ["value"] = "ball", ["scale"] = "ball" },
            ["layers"] = new JsonArray(new JsonObject { ["type"] = "circle", ["r"] = 8.5, ["center"] = new JsonArray(tc.X, Math.Round(tc.Y + tubeRadius, 2)), ["fillTop"] = "#3A3A3A", ["fillBottom"] = "#050505", ["stroke"] = "#000000", ["strokeWidth"] = 1 }),
        });

        // Turn needle: a broad white pointer standing up from a pivot in the middle.
        layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "turn", ["value"] = "turn", ["length"] = g.R(76), ["tail"] = 10, ["width"] = 7, ["color"] = "#F4F2EC", ["tailColor"] = "#F4F2EC", ["shadow"] = true, ["center"] = pivot.DeepClone() });
        layers.Add(new JsonObject { ["type"] = "hub", ["r"] = 12, ["fill"] = "#141414", ["stroke"] = "#4A4A4A", ["center"] = pivot.DeepClone() });
        layers.Add(g.Glass());

        var values = new JsonObject
        {
            ["turn"] = new JsonObject { ["sim"] = "TURN INDICATOR RATE", ["unit"] = "radians per second", ["default"] = 0 },
            ["ball"] = new JsonObject { ["sim"] = "TURN COORDINATOR BALL", ["unit"] = "position", ["default"] = 0 },
        };
        var scales = new JsonObject
        {
            ["turn"] = new JsonObject { ["min"] = -2 * StandardRate, ["max"] = 2 * StandardRate, ["startAngle"] = -44, ["endAngle"] = 44 },
            ["tube"] = new JsonObject { ["min"] = -1, ["max"] = 1, ["startAngle"] = 180 + tubeHalfAngle, ["endAngle"] = 180 - tubeHalfAngle },
            // Ball: swing the group so it rolls along the tube (counter-clockwise moves it right).
            ["ball"] = new JsonObject { ["min"] = -127, ["max"] = 127, ["startAngle"] = 16, ["endAngle"] = -16 },
        };
        return g.Gauge(name, "Classic turn and slip indicator: turn needle with 2-minute (standard rate) marks, ball in a curved tube.", values, scales, layers, font);
    }
}

/// <summary>
/// Flap position indicator: UP at the top left, sweeping clockwise as the flaps go down, with a
/// marked line at each flap setting.
/// </summary>
public static class FlapsGenerator
{
    public static string Create(double maxDegrees, IReadOnlyList<double> settings, string name, GaugeLook look = GaugeLook.LightTwin, string font = "BarlowCondensed-Medium.ttf")
    {
        if (maxDegrees <= 0) throw new ArgumentException("Full flaps must be more than 0 degrees.", nameof(maxDegrees));
        var step = maxDegrees > 30 ? 10 : 5;
        var g = new GaugeParts(look);
        var layers = g.Case();

        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "flaps", ["every"] = step / 2.0, ["from"] = 0, ["to"] = maxDegrees, ["radius"] = g.R(104), ["length"] = 8, ["width"] = 1.8, ["color"] = g.InkColor });
        layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "flaps", ["every"] = step, ["from"] = 0, ["to"] = maxDegrees, ["radius"] = g.R(104), ["length"] = 15, ["width"] = 3, ["color"] = g.InkColor });
        var marks = settings.Where(v => v > 0 && v <= maxDegrees).Distinct().ToList();
        if (marks.Count > 0)
            layers.Add(new JsonObject { ["type"] = "ticks", ["scale"] = "flaps", ["at"] = new JsonArray([.. marks.Select(v => (JsonNode)v)]), ["radius"] = g.R(94), ["length"] = -9, ["width"] = 10, ["shape"] = "triangle", ["color"] = GaugeParts.Yellow });
        var labels = Enumerable.Range(0, (int)(maxDegrees / step) + 1).Select(i => (double)i * step).ToList();
        layers.Add(new JsonObject
        {
            ["type"] = "labels", ["scale"] = "flaps", ["at"] = new JsonArray([.. labels.Select(v => (JsonNode)v)]), ["radius"] = g.R(72), ["size"] = 20, ["color"] = g.InkColor,
            ["texts"] = new JsonObject { ["0"] = "UP" },
        });
        layers.Add(g.Text("FLAPS", 0, 42, 16));
        layers.Add(g.Text("DEGREES", 0, 58, 10));
        layers.Add(new JsonObject { ["type"] = "needle", ["scale"] = "flaps", ["value"] = "flaps", ["length"] = g.R(96), ["tail"] = 22, ["width"] = 5, ["color"] = "#F4F2EC", ["tailColor"] = "#161616", ["shadow"] = true });
        layers.Add(new JsonObject { ["type"] = "hub", ["r"] = 10, ["fill"] = "#141414", ["stroke"] = "#4A4A4A" });
        layers.Add(g.Glass());

        var values = new JsonObject { ["flaps"] = new JsonObject { ["sim"] = "TRAILING EDGE FLAPS LEFT ANGLE", ["unit"] = "degrees", ["default"] = marks.FirstOrDefault() } };
        var scales = new JsonObject { ["flaps"] = new JsonObject { ["min"] = 0, ["max"] = maxDegrees, ["startAngle"] = -70, ["endAngle"] = 150 } };
        var setText = marks.Count > 0 ? $", marks at {string.Join(", ", marks)}" : "";
        return g.Gauge(name, $"Flap position indicator, 0 to {maxDegrees} degrees{setText}.", values, scales, layers, font);
    }
}
