// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Fipinator.Core;

/// <summary>
/// Combines gauges for people with fewer FIPs than gauges:
/// <list type="bullet">
/// <item><see cref="MoreNeedles"/>: the needles of several gauges with the same dial on one dial
/// (RPM 1-2 + RPM 3-4 = RPM 1-4), numbered and staggered so their number discs don't overlap.</item>
/// <item><see cref="SideBySide"/>: two gauges shrunk to fit one FIP.</item>
/// </list>
/// Works on gauge files as written, so it works for any gauge, not just the DC-6.
/// </summary>
public static partial class GaugeCombiner
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// Puts every needle of <paramref name="gauges"/> on the first gauge's dial. Each needle keeps
    /// its own sim value; scales are matched by name, so the gauges must share a dial (same scale
    /// names). Needles without a number get 1, 2, 3... in order.
    /// </summary>
    /// <exception cref="ArgumentException">Fewer than two gauges, or a needle's scale isn't on the first gauge's dial.</exception>
    public static string MoreNeedles(IReadOnlyList<string> gauges, string name)
    {
        if (gauges.Count < 2) throw new ArgumentException("Pick at least two gauges to combine.", nameof(gauges));
        var baseGauge = Parse(gauges[0]);
        var values = baseGauge["values"]?.AsObject() ?? [];
        baseGauge["values"] = values;
        var scales = baseGauge["scales"]!.AsObject();
        var layers = baseGauge["layers"]!.AsArray();

        // Collect needles: the base gauge's in place, the others' added after its last needle.
        var needles = layers.OfType<JsonObject>().Where(IsNeedle).ToList();
        if (needles.Count == 0) throw new ArgumentException($"'{Name(baseGauge)}' has no needles to add to.", nameof(gauges));
        var insertAt = layers.IndexOf(needles[^1]) + 1;

        for (var g = 1; g < gauges.Count; g++)
        {
            var other = Parse(gauges[g]);
            var otherValues = other["values"]?.AsObject() ?? [];
            foreach (var needle in (other["layers"]?.AsArray() ?? []).OfType<JsonObject>().Where(IsNeedle))
            {
                var scale = needle["scale"]?.GetValue<string>() ?? "";
                if (!scales.ContainsKey(scale))
                    throw new ArgumentException($"'{Name(other)}' uses a different dial from '{Name(baseGauge)}' (no scale '{scale}'), so its needle can't go on that dial. Try side by side instead.", nameof(gauges));
                var copy = needle.DeepClone().AsObject();
                var value = copy["value"]?.GetValue<string>() ?? "";
                if (otherValues[value] is { } spec)
                {
                    var newName = Unique(values, value);
                    values[newName] = spec.DeepClone();
                    copy["value"] = newName;
                }
                layers.Insert(insertAt++, copy);
                needles.Add(copy);
            }
        }

        // Number the needles and stagger them (6 px shorter each) so their discs sit apart.
        var tagged = needles.Count > 1;
        var first = needles[0]["length"]?.GetValue<double>() ?? 0;
        for (var i = 0; i < needles.Count; i++)
        {
            if (tagged && needles[i]["tag"] is null && needles[i]["style"]?.GetValue<string>() == "pointer")
                needles[i]["tag"] = (i + 1).ToString(CultureInfo.InvariantCulture);
            if (needles[i]["style"]?.GetValue<string>() == "pointer" && !IsTwin(needles[i]))
                needles[i]["length"] = first - i * 6;
        }

        baseGauge["name"] = name;
        baseGauge["description"] = $"{needles.Count} needles on one dial, combined from: {string.Join(", ", gauges.Select(j => Name(Parse(j))))}.";
        return baseGauge.ToJsonString(Indented);
    }

    /// <summary>
    /// Two gauges side by side on one FIP, each shrunk to fit. Their values and scales are
    /// renamed (left_..., right_...) so they can't clash.
    /// </summary>
    public static string SideBySide(string left, string right, string name)
    {
        var a = Parse(left);
        var b = Parse(right);
        var width = a["canvas"]?["width"]?.GetValue<int>() ?? 320;
        var height = a["canvas"]?["height"]?.GetValue<int>() ?? 240;

        // Each gauge's drawing sits in a box as tall as the screen (240 x 240 in the middle of 320);
        // shrink that box to half the width and centre it vertically.
        var box = height;
        var zoom = Math.Round(width / 2.0 / box, 4);
        var top = Math.Round((height - box * zoom) / 2, 2);

        var values = new JsonObject();
        var scales = new JsonObject();
        var layers = new JsonArray();
        foreach (var (gauge, prefix, slot) in new[] { (a, "left_", 0), (b, "right_", 1) })
        {
            var rename = Rename(gauge, prefix, values, scales);
            var center = gauge["center"]?.DeepClone() ?? new JsonArray(160, 120);
            var inner = new JsonArray();
            // The gauge's own background, then its layers, all kept inside its box.
            inner.Add(new JsonObject { ["type"] = "rect", ["x"] = (width - box) / 2, ["y"] = 0, ["width"] = box, ["height"] = box, ["fill"] = gauge["canvas"]?["background"]?.GetValue<string>() ?? "#000000" });
            foreach (var layer in (gauge["layers"]?.AsArray() ?? []).OfType<JsonObject>())
            {
                var copy = layer.DeepClone().AsObject();
                rename(copy);
                if (copy["center"] is null) copy["center"] = center.DeepClone();
                inner.Add(copy);
            }
            var offsetX = Math.Round(slot * width / 2.0 - (width - box) / 2.0 * zoom, 2);
            layers.Add(new JsonObject
            {
                ["type"] = "group", ["offset"] = new JsonArray(offsetX, top), ["zoom"] = zoom,
                ["clipBox"] = new JsonArray((width - box) / 2, 0, box, box), ["layers"] = inner,
            });
        }

        var result = new JsonObject
        {
            ["format"] = 1,
            ["name"] = name,
            ["author"] = a["author"]?.DeepClone(),
            ["description"] = $"Two gauges side by side: {Name(a)} and {Name(b)}.",
            ["canvas"] = new JsonObject { ["width"] = width, ["height"] = height, ["background"] = a["canvas"]?["background"]?.DeepClone() ?? "#000000" },
            ["center"] = new JsonArray(160, 120),
            ["font"] = a["font"]?.DeepClone(),
            ["values"] = values,
            ["scales"] = scales,
            ["layers"] = layers,
        };
        var controls = SideBySideKnobs(left, right);
        if (controls.Count > 0) result["controls"] = controls;
        return result.ToJsonString(Indented);
    }

    /// <summary>
    /// Knobs for two gauges on one FIP: the left gauge's knob actions go on the FIP's left knob, the
    /// right gauge's on the right knob (each gauge's own right-knob, one-step actions; a gauge without
    /// knobs gets its standard ones first, see <see cref="KnobDefaults"/>).
    /// </summary>
    private static JsonObject SideBySideKnobs(string left, string right)
    {
        var controls = new JsonObject();
        foreach (var (json, side) in new[] { (left, "left"), (right, "right") })
        {
            var own = Parse(KnobDefaults.Fill(json))["controls"] as JsonObject;
            if (own is null) continue;
            foreach (var turn in new[] { "Clockwise", "Anticlockwise" })
                if ((own[$"rightKnob{turn}"] ?? own[$"leftKnob{turn}"]) is JsonObject action)
                    controls[$"{side}Knob{turn}"] = action.DeepClone();
        }
        return controls;
    }

    /// <summary>Copies a gauge's values and scales under new names; returns a function that renames a layer's links.</summary>
    private static Action<JsonObject> Rename(JsonObject gauge, string prefix, JsonObject values, JsonObject scales)
    {
        var valueNames = (gauge["values"]?.AsObject() ?? []).Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, spec) in gauge["values"]?.AsObject() ?? [])
        {
            var copy = spec!.DeepClone().AsObject();
            if (copy["expr"]?.GetValue<string>() is { } expr)
                copy["expr"] = Word().Replace(expr, m => valueNames.Contains(m.Value) ? prefix + m.Value : m.Value);
            values[prefix + key] = copy;
        }
        foreach (var (key, spec) in gauge["scales"]?.AsObject() ?? [])
            scales[prefix + key] = spec!.DeepClone();

        void Walk(JsonObject layer)
        {
            if (layer["scale"]?.GetValue<string>() is { } s) layer["scale"] = prefix + s;
            if (layer["value"]?.GetValue<string>() is { } v) layer["value"] = prefix + v;
            foreach (var link in new[] { "rotate", "moveX", "moveY" })
            {
                if (layer[link] is not JsonObject l) continue;
                if (l["scale"]?.GetValue<string>() is { } ls) l["scale"] = prefix + ls;
                if (l["value"]?.GetValue<string>() is { } lv) l["value"] = prefix + lv;
            }
            foreach (var child in (layer["layers"] as JsonArray ?? []).OfType<JsonObject>()) Walk(child);
        }
        return Walk;
    }

    private static JsonObject Parse(string json)
    {
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?.AsObject()
            ?? throw new ArgumentException("A gauge file is empty.");
        if (node["layers"] is not JsonArray || node["scales"] is not JsonObject)
            throw new ArgumentException($"'{Name(node)}' isn't a complete gauge (no layers or scales).");
        return node;
    }

    private static string Name(JsonObject gauge) => gauge["name"]?.GetValue<string>() ?? "gauge";
    private static bool IsNeedle(JsonObject layer) => layer["type"]?.GetValue<string>() == "needle";

    // Twin-dial needles (pivot at the dial's edge) reach exactly to their scale; don't stagger them.
    private static bool IsTwin(JsonObject needle) => needle["width"]?.GetValue<double>() is < 2.5;

    private static string Unique(JsonObject values, string wanted)
    {
        if (!values.ContainsKey(wanted)) return wanted;
        var stem = wanted.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        for (var n = 1; ; n++)
            if (!values.ContainsKey(stem + n)) return stem + n;
    }

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Word();
}
