// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Fipinator.Core;

/// <summary>The radios an RMI needle can point to.</summary>
public enum RmiRadio { Vor1, Vor2, Adf1, Adf2 }

public sealed partial class GaugeEditor
{
    // ---------------------------------------------------------------- RMI needles on and off

    private const string HeadingSim = "PLANE HEADING DEGREES MAGNETIC";

    /// <summary>Plain name: "VOR 1", "ADF 2".</summary>
    public static string RmiName(RmiRadio radio) => $"{(IsVor(radio) ? "VOR" : "ADF")} {RadioNumber(radio)}";

    /// <summary>True when the dial has a needle pointing to a VOR or ADF station (an RMI or an ADF dial).</summary>
    public bool IsRmi => Enum.GetValues<RmiRadio>().Any(r => RmiNeedle(r) is not null);

    /// <summary>True when there is a needle pointing to <paramref name="radio"/>.</summary>
    public bool HasRmiNeedle(RmiRadio radio) => RmiNeedle(radio) is not null;

    /// <summary>
    /// Turns the needle for <paramref name="radio"/> on (adds the needle, the sim values and the calculation that
    /// points it at the station, and a key in a bottom corner) or off (removes the needle and its key).
    /// One undo step. Returns null when done, else a plain reason it can't.
    /// </summary>
    public string? SetRmiNeedle(RmiRadio radio, bool on)
    {
        if (!IsRmi) return "This gauge has no VOR or ADF needle to start from.";
        var existing = RmiNeedle(radio);
        if (on == existing is not null) return null;

        if (!on)
        {
            if (Enum.GetValues<RmiRadio>().Count(r => RmiNeedle(r) is not null) == 1) return "Keep at least one needle.";
            Checkpoint();
            var key = KeyText(radio);
            // Remove the later one first so the other's place stays right.
            foreach (var id in new[] { existing!.Value.Id, key ?? default }.Where(i => i.Path is { Length: > 0 }).OrderByDescending(i => i.Path, PathOrder.Instance))
            {
                var (owner, index) = Parent(id);
                owner.RemoveAt(index);
            }
            return null;
        }

        var template = Enum.GetValues<RmiRadio>().Select(RmiNeedle).First(n => n is not null)!.Value;
        Checkpoint();

        // The values: the station's direction, whether there is a signal, and (for a VOR) the heading.
        // Sample readings (for the preview and Try it) put each new needle somewhere different.
        var n = RadioNumber(radio);
        var sample = radio switch { RmiRadio.Vor1 => 100, RmiRadio.Vor2 => 160, RmiRadio.Adf1 => 320, _ => 200 };
        string expr;
        if (IsVor(radio))
        {
            var radial = UseSimValueQuiet($"NAV RADIAL:{n}", "degrees", sample);
            var signal = UseSimValueQuiet($"NAV HAS NAV:{n}", "bool", 1);
            var heading = UseSimValueQuiet(HeadingSim, "degrees", 0);
            expr = $"choose({signal}, wrap360({radial} + 180 - {heading}), 90)";
        }
        else
        {
            var bearing = UseSimValueQuiet($"ADF RADIAL:{n}", "degrees", sample);
            var signal = UseSimValueQuiet($"ADF SIGNAL:{n}", "number", 1);
            expr = $"choose({signal}, {bearing}, 90)";
        }
        var wanted = $"{(IsVor(radio) ? "vor" : "adf")}{n}_needle";
        var name = wanted;
        for (var i = 2; Values[name] is JsonObject o && Str(o, "expr") != expr; i++) name = $"{wanted}{i}";
        Values[name] = new JsonObject { ["expr"] = expr };

        // The needle: yellow for a VOR, light for an ADF; single line for 1, double for 2, unless its partner
        // already has that shape (then the other one, so the two can be told apart).
        var partner = IsVor(radio) ? (n == 1 ? RmiRadio.Vor2 : RmiRadio.Vor1) : (n == 1 ? RmiRadio.Adf2 : RmiRadio.Adf1);
        var style = n == 1 ? "arrow" : "doubleArrow";
        if (RmiNeedle(partner) is { } p && Str(p.Layer, "style") == style) style = style == "arrow" ? "doubleArrow" : "arrow";
        var needle = template.Layer.DeepClone().AsObject();
        needle["value"] = name;
        needle["style"] = style;
        // A colour the other kind of needle doesn't already use (some RMIs have light VOR needles).
        var otherColours = Parts().Where(p => p.Kind == PartKind.Needle).Select(p => Layer(p.Id))
            .Where(l => IsVorNeedle(l) != IsVor(radio)).Select(l => Str(l, "color")?.ToUpperInvariant()).ToHashSet();
        var colour = (IsVor(radio) ? new[] { "#E8C547", "#7CD67C" } : ["#EFE6CF", "#6FB8E8"]).First(c => !otherColours.Contains(c) || c is "#7CD67C" or "#6FB8E8");
        needle["color"] = colour;
        needle["length"] = IsVor(radio) ? 92 : 84;
        needle["tail"] = IsVor(radio) ? 80 : 72;
        needle["width"] = (IsVor(radio), style == "arrow") switch { (true, true) => 3.4, (true, false) => 2.2, (false, true) => 2.6, _ => 1.8 };
        needle.Remove("tag");
        // ADF needles go under VOR needles.
        var (list, _) = Parent(template.Id);
        var prefix = template.Id.Path[..^1];
        var at = -1;
        for (var i = 0; i < list.Count; i++)
            if (list[i] is JsonObject l && KindOf(l) == PartKind.Needle && (IsVor(radio) || !IsVorNeedle(l))) at = i;
        if (!IsVor(radio) && at < 0) at = Enumerable.Range(0, list.Count).First(i => list[i] is JsonObject l && KindOf(l) == PartKind.Needle) - 1;
        Insert(new PartId([.. prefix, at + 1]), needle);

        // The key in a bottom corner: VORs on the left, ADFs on the right, in a free spot.
        var width = _root["canvas"] is JsonObject c ? Num(c, "width") ?? 320 : 320;
        var height = _root["canvas"] is JsonObject c2 ? Num(c2, "height") ?? 240 : 240;
        var left = IsVor(radio);
        var arrows = style == "arrow" ? "<" : "<<";
        var text = left ? $"{arrows} {RmiName(radio)}" : $"{RmiName(radio)} {arrows.Replace('<', '>')}";
        var taken = Layers.OfType<JsonObject>().Where(l => KindOf(l) == PartKind.Text && Num(l, "x") is { } x && (x < width / 2) == left)
            .Select(l => Num(l, "y") ?? 0).ToList();
        var y = new[] { height - 4, height - 20, height - 36, height - 52 }.FirstOrDefault(y => taken.All(t => Math.Abs(t - y) >= 12), height - 4);
        Append(new JsonObject
        {
            ["type"] = "text", ["text"] = text, ["x"] = left ? 4 : width - 4, ["y"] = y, ["size"] = 10,
            ["color"] = colour, ["align"] = left ? "left" : "right",
        });
        return null;
    }

    private static bool IsVor(RmiRadio radio) => radio is RmiRadio.Vor1 or RmiRadio.Vor2;
    private static int RadioNumber(RmiRadio radio) => radio is RmiRadio.Vor1 or RmiRadio.Adf1 ? 1 : 2;
    private static string StationSim(RmiRadio radio) => $"{(IsVor(radio) ? "NAV" : "ADF")} RADIAL:{RadioNumber(radio)}";

    /// <summary>The needle pointing to <paramref name="radio"/>: its value reads (or is worked out from) that radio's direction.</summary>
    private (PartId Id, JsonObject Layer)? RmiNeedle(RmiRadio radio)
    {
        foreach (var part in Parts())
        {
            if (part.Kind != PartKind.Needle) continue;
            var layer = Layer(part.Id);
            if (SimsBehind(Str(layer, "value")).Contains(StationSim(radio), StringComparer.OrdinalIgnoreCase)) return (part.Id, layer);
        }
        return null;
    }

    private bool IsVorNeedle(JsonObject needle) =>
        SimsBehind(Str(needle, "value")).Any(s => s.StartsWith("NAV RADIAL:", StringComparison.OrdinalIgnoreCase));

    /// <summary>The sim values a value reads: its own, or those its calculation uses.</summary>
    private List<string> SimsBehind(string? value)
    {
        if (value is null || Values[value] is not JsonObject spec) return [];
        if (Str(spec, "sim") is { } sim) return [sim.Trim()];
        if (Str(spec, "expr") is not { } expr) return [];
        return [.. Word().Matches(expr).Select(m => m.Value).Distinct()
            .Select(w => Values[w] is JsonObject o ? Str(o, "sim")?.Trim() : null).OfType<string>()];
    }

    /// <summary>The corner key for <paramref name="radio"/> ("&lt; VOR 1", "ADF 2 &gt;&gt;"), if there is one.</summary>
    private PartId? KeyText(RmiRadio radio)
    {
        var pattern = new Regex($@"\b{(IsVor(radio) ? "VOR" : "ADF")} ?{RadioNumber(radio)}\b", RegexOptions.IgnoreCase);
        foreach (var part in Parts())
            if (part.Kind == PartKind.Text && Layer(part.Id) is var l && Str(l, "text") is { } t && pattern.IsMatch(t)) return part.Id;
        return null;
    }

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Word();
}
