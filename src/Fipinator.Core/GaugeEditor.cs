// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Fipinator.Core;

/// <summary>
/// The editing behind FIPinator 3000's editor: a gauge file as a list of parts (dial face, tick marks,
/// numbers, needles, number boxes...) that can be recoloured, resized, moved, added and removed, with
/// undo and redo. Works on the file's JSON directly, so everything the editor doesn't know about is
/// kept exactly as it was.
/// </summary>
public sealed partial class GaugeEditor
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private const int MaxUndo = 200;

    private JsonObject _root;
    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];

    /// <exception cref="JsonException">Not a gauge file (not JSON, or no layers).</exception>
    public GaugeEditor(string json) => _root = ParseRoot(json);

    /// <summary>The gauge as it is now, as a gauge file.</summary>
    public string Json => _root.ToJsonString(Indented);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Undo() => Step(_undo, _redo);
    public void Redo() => Step(_redo, _undo);

    /// <summary>Remembers the gauge as it is, so the next change can be undone. Called by every edit.</summary>
    public void Checkpoint()
    {
        _undo.Add(Json);
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
    }

    /// <summary>Forgets the last checkpoint if nothing changed since (a click that turned out not to be a drag).</summary>
    public void DropCheckpointIfUnchanged()
    {
        if (_undo.Count > 0 && _undo[^1] == Json) _undo.RemoveAt(_undo.Count - 1);
    }

    /// <summary>Replaces the whole gauge (e.g. the file was changed outside the editor); undo goes back to before.</summary>
    /// <exception cref="JsonException">Not a gauge file.</exception>
    public void Replace(string json)
    {
        var root = ParseRoot(json);
        Checkpoint();
        _root = root;
    }

    private void Step(List<string> from, List<string> to)
    {
        if (from.Count == 0) return;
        to.Add(Json);
        _root = ParseRoot(from[^1]);
        from.RemoveAt(from.Count - 1);
    }

    private static JsonObject ParseRoot(string json)
    {
        var root = JsonNode.Parse(json, documentOptions: Lenient) as JsonObject ?? throw new JsonException("The file is empty.");
        if (root["layers"] is not JsonArray) throw new JsonException("This isn't a gauge file: it has no \"layers\".");
        return root;
    }

    /// <summary>The gauge as the renderer sees it, or null with the reasons if it has mistakes.</summary>
    public GaugeDefinition? Check(out IReadOnlyList<string> problems)
    {
        try
        {
            var gauge = GaugeDefinition.Parse(Json);
            problems = gauge.Problems();
            return problems.Count == 0 ? gauge : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            problems = [ex.Message];
            return null;
        }
    }

    // ---------------------------------------------------------------- parts

    /// <summary>All parts in drawing order (later ones are drawn on top), with groups' parts after the group.</summary>
    public IReadOnlyList<GaugePart> Parts()
    {
        var list = new List<GaugePart>();
        void Walk(JsonArray layers, int[] parent, int depth)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                if (layers[i] is not JsonObject layer) continue;
                int[] path = [.. parent, i];
                var box = IsNumberBox(layer);
                list.Add(new GaugePart(new PartId(path), depth, KindOf(layer), Title(layer)));
                if (layer["layers"] is JsonArray inner && !box) Walk(inner, path, depth + 1);
            }
        }
        Walk(Layers, [], 0);
        return list;
    }

    /// <summary>What can be changed on a part, in the order the editor shows it.</summary>
    public IReadOnlyList<PartSetting> Settings(PartId id)
    {
        var layer = Layer(id);
        var kind = KindOf(layer);
        var list = new List<PartSetting>();
        void Color(string key, string label, JsonObject? on = null) { if ((on ?? layer).ContainsKey(key)) list.Add(PartSetting.Colour(key, label, Str(on ?? layer, key))); }
        void Number(string key, string label, double min, double max, double step, double fallback, JsonObject? on = null) =>
            list.Add(PartSetting.Number(key, label, Num(on ?? layer, key[(key.IndexOf('.') + 1)..]) ?? fallback, min, max, step));

        switch (kind)
        {
            case PartKind.NumberBox:
            {
                var (rect, text) = NumberBoxParts(layer);
                list.Add(PartSetting.Value("text.value", "Shows", Str(text, "value")));
                list.Add(PartSetting.Choice("text.format", "Digits", Str(text, "format") ?? "0", ["0", "00", "000", "0000", "0.0", "0.00"]));
                Number("text.size", "Text size", 8, 60, 1, 14, text);
                list.Add(PartSetting.Colour("text.color", "Number colour", Str(text, "color") ?? "#FFFFFF"));
                list.Add(PartSetting.Colour("rect.fill", "Box colour", Str(rect, "fill")));
                list.Add(PartSetting.Colour("rect.stroke", "Box outline", Str(rect, "stroke")));
                Number("rect.width", "Box width", 10, 320, 1, 60, rect);
                Number("rect.height", "Box height", 8, 240, 1, 24, rect);
                break;
            }
            case PartKind.Needle:
                list.Add(PartSetting.Value("value", "Shows", Str(layer, "value")));
                list.Add(PartSetting.Colour("color", "Colour", Str(layer, "color") ?? "#FFFFFF"));
                list.Add(PartSetting.Choice("style", "Shape", Str(layer, "style") ?? "classic", ["classic", "pointer", "arrow", "doubleArrow"]));
                Number("length", "Length", 5, 200, 1, 80);
                Number("width", "Width", 0.5, 20, 0.5, 3);
                Number("tail", "Tail", 0, 100, 1, 0);
                list.Add(PartSetting.Text("tag", "Number on needle", Str(layer, "tag") ?? ""));
                break;
            case PartKind.Number:
                if (layer.ContainsKey("value")) list.Add(PartSetting.Value("value", "Shows", Str(layer, "value")));
                else list.Add(PartSetting.Text("text", "Text", Str(layer, "text") ?? ""));
                if (layer.ContainsKey("value")) list.Add(PartSetting.Choice("format", "Digits", Str(layer, "format") ?? "0", ["0", "00", "000", "0000", "0.0", "0.00"]));
                list.Add(PartSetting.Colour("color", "Colour", Str(layer, "color") ?? "#FFFFFF"));
                Number("size", "Text size", 6, 80, 1, 14);
                break;
            case PartKind.Text:
                list.Add(PartSetting.Text("text", "Text", Str(layer, "text") ?? ""));
                list.Add(PartSetting.Colour("color", "Colour", Str(layer, "color") ?? "#FFFFFF"));
                Number("size", "Text size", 6, 80, 1, 14);
                break;
            case PartKind.Ticks:
                list.Add(PartSetting.Colour("color", "Colour", Str(layer, "color") ?? "#FFFFFF"));
                Number("radius", "Distance from centre", -150, 200, 1, 90);
                Number("length", "Length", 1, 60, 1, 8);
                Number("width", "Width", 0.5, 20, 0.5, 1);
                break;
            case PartKind.Labels:
                list.Add(PartSetting.Colour("color", "Colour", Str(layer, "color") ?? "#FFFFFF"));
                Number("radius", "Distance from centre", -150, 200, 1, 70);
                Number("size", "Text size", 6, 60, 1, 16);
                break;
            case PartKind.Arc:
                list.Add(PartSetting.Colour("color", "Colour", Str(layer, "color") ?? "#FFFFFF"));
                Number("from", "From (on the scale)", -100000, 100000, 1, 0);
                Number("to", "To (on the scale)", -100000, 100000, 1, 0);
                Number("radius", "Distance from centre", -150, 200, 1, 90);
                Number("width", "Width", 0.5, 40, 0.5, 4);
                break;
            case PartKind.Circle:
                Color("fill", "Colour");
                Color("fillTop", "Top colour");
                Color("fillBottom", "Bottom colour");
                Color("stroke", "Outline");
                Number("r", "Size", 1, 200, 1, 50);
                break;
            case PartKind.Box:
                Color("fill", "Colour");
                Color("stroke", "Outline");
                Number("width", "Width", 1, 320, 1, 40);
                Number("height", "Height", 1, 240, 1, 20);
                Number("radius", "Rounded corners", 0, 60, 1, 0);
                break;
            case PartKind.Hub:
                list.Add(PartSetting.Colour("fill", "Colour", Str(layer, "fill") ?? "#1A1A1A"));
                Number("r", "Size", 1, 60, 1, 10);
                break;
            case PartKind.Glass:
                Number("opacity", "Shine", 0, 1, 0.02, 0.2);
                Number("r", "Size", 1, 200, 1, 100);
                break;
            case PartKind.Group:
                Number("zoom", "Size", 0.1, 3, 0.05, 1);
                break;
        }
        return list;
    }

    /// <summary>Changes one setting (from <see cref="Settings"/>). Numbers come as invariant-culture text.</summary>
    public void Set(PartId id, string key, string? text)
    {
        var layer = Layer(id);
        var (target, prop) = Target(layer, key);
        var setting = Settings(id).FirstOrDefault(s => s.Key == key) ?? throw new ArgumentException($"'{key}' can't be set on this part.");
        // Check first, so a mistake changes nothing (and leaves no empty undo step).
        JsonNode? node = setting.Kind switch
        {
            SettingKind.Number => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n)
                ? JsonValue.Create(Math.Round(Math.Clamp(n, setting.Min, setting.Max), 3))
                : throw new ArgumentException("Type a number."),
            SettingKind.Colour => string.IsNullOrWhiteSpace(text) ? null
                : IsColour(text) ? JsonValue.Create(text.Trim().ToUpperInvariant())
                : throw new ArgumentException("A colour looks like #RRGGBB, for example #FFFFFF for white."),
            SettingKind.Text => string.IsNullOrEmpty(text) ? null : JsonValue.Create(text),
            SettingKind.Choice => text is not null && setting.Choices!.Contains(text) ? JsonValue.Create(text)
                : throw new ArgumentException("Pick one of the choices."),
            SettingKind.Value => !string.IsNullOrWhiteSpace(text) && Values.ContainsKey(text) ? JsonValue.Create(text)
                : throw new ArgumentException("Pick what it shows from the list."),
            _ => throw new ArgumentException("That can't be changed here."),
        };
        if (setting.Current == text) return;
        Checkpoint();
        if (node is null) target.Remove(prop);
        else target[prop] = node;
    }

    /// <summary>Moves a part by (dx, dy) screen pixels. Pass checkpoint false while dragging (one undo per drag).</summary>
    public void Move(PartId id, double dx, double dy, bool checkpoint = true)
    {
        if (checkpoint) Checkpoint();
        var layer = Layer(id);
        // Parts inside a resized group move in the group's own (smaller or bigger) pixels.
        var zoom = 1.0;
        for (var depth = 1; depth < id.Path.Length; depth++)
            zoom *= Num(Layer(new PartId(id.Path[..depth])), "zoom") ?? 1;
        dx /= zoom;
        dy /= zoom;
        switch (KindOf(layer))
        {
            case PartKind.Box or PartKind.Text or PartKind.Number or PartKind.Screw:
                layer["x"] = Math.Round((Num(layer, "x") ?? 0) + dx, 2);
                layer["y"] = Math.Round((Num(layer, "y") ?? 0) + dy, 2);
                break;
            case PartKind.Group or PartKind.NumberBox:
                var offset = layer["offset"] as JsonArray;
                var (ox, oy) = offset is { Count: 2 } ? (D(offset[0]), D(offset[1])) : (0, 0);
                // A group's offset is applied before its own zoom, so it's in the parent's pixels.
                var z = Num(layer, "zoom") ?? 1;
                layer["offset"] = new JsonArray(Math.Round(ox + dx * z, 2), Math.Round(oy + dy * z, 2));
                break;
            default:
                var (cx, cy) = CenterOf(layer);
                layer["center"] = new JsonArray(Math.Round(cx + dx, 2), Math.Round(cy + dy, 2));
                break;
        }
    }

    /// <summary>Removes a part.</summary>
    public void Remove(PartId id)
    {
        Checkpoint();
        var (list, index) = Parent(id);
        list.RemoveAt(index);
    }

    /// <summary>Draws a part one step later (on top, +1) or earlier (behind, -1). Returns its new id.</summary>
    public PartId Reorder(PartId id, int direction)
    {
        var (list, index) = Parent(id);
        var to = index + Math.Sign(direction);
        if (to < 0 || to >= list.Count) return id;
        Checkpoint();
        var node = list[index];
        list.RemoveAt(index);
        list.Insert(to, node);
        return new PartId([.. id.Path[..^1], to]);
    }

    /// <summary>Copies a part, a little to the right and down. Returns the copy's id.</summary>
    public PartId Duplicate(PartId id)
    {
        Checkpoint();
        var (list, index) = Parent(id);
        list.Insert(index + 1, list[index]!.DeepClone());
        var copy = new PartId([.. id.Path[..^1], index + 1]);
        Move(copy, 12, 12, checkpoint: false);
        return copy;
    }

    /// <summary>The sim value a part shows, for parts that show one; null otherwise.</summary>
    public string? ValueOf(PartId id)
    {
        var layer = Layer(id);
        return KindOf(layer) == PartKind.NumberBox ? Str(NumberBoxParts(layer).Text, "value") : Str(layer, "value");
    }

    // ---------------------------------------------------------------- values the gauge reads

    /// <summary>The gauge's values: name and a plain label ("Engine RPM (engine 2)").</summary>
    public IReadOnlyList<(string Name, string Label)> ValueChoices() =>
        [.. Values.Select(v => (v.Key, Label(v.Key, v.Value as JsonObject))).OrderBy(v => v.Item2, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>
    /// Makes sure the gauge reads <paramref name="sim"/> (in <paramref name="unit"/>) and returns the value's
    /// name, adding it if it isn't there yet.
    /// </summary>
    public string UseSimValue(string sim, string unit, double fallback = 0)
    {
        foreach (var (name, spec) in Values)
            if (spec is JsonObject o && string.Equals(Str(o, "sim")?.Trim(), sim.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(Str(o, "unit")?.Trim(), unit.Trim(), StringComparison.OrdinalIgnoreCase))
                return name;
        Checkpoint();
        var wanted = Slug(sim);
        var key = wanted;
        for (var n = 2; Values.ContainsKey(key); n++) key = $"{wanted}{n}";
        Values[key] = new JsonObject { ["sim"] = sim.Trim(), ["unit"] = unit.Trim(), ["default"] = fallback };
        return key;
    }

    /// <summary>
    /// The sim values (and units) used by the given gauge files, with plain labels, sorted by label. Built from
    /// the gauges shipped with FIPinator, these are values known to work in MSFS 2024. Unreadable files are skipped.
    /// </summary>
    public static IReadOnlyList<(string Sim, string Unit, string Label)> KnownSimValues(IEnumerable<string> gaugeFiles)
    {
        var found = new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in gaugeFiles)
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(file), documentOptions: Lenient)?["values"] is not JsonObject values) continue;
                foreach (var (_, spec) in values)
                    if (spec is JsonObject o && Str(o, "sim")?.Trim() is { Length: > 0 } sim && Str(o, "unit")?.Trim() is { Length: > 0 } unit)
                        found.TryAdd($"{sim}|{unit}", (sim, unit, SimLabel(sim, unit)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return [.. found.Values.OrderBy(v => v.Item3, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>A plain label for a sim value:"GENERAL ENG RPM:2" in rpm becomes "General eng rpm (engine 2), rpm".</summary>
    public static string SimLabel(string sim, string? unit)
    {
        var s = sim.Trim();
        var index = "";
        if (IndexSuffix().Match(s) is { Success: true } m)
        {
            index = $" ({(s.Contains("ENG", StringComparison.OrdinalIgnoreCase) ? "engine" : "no.")} {m.Groups[1].Value})";
            s = s[..m.Index];
        }
        var nice = s.Length == 0 ? sim : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
        return unit is null or "" or "number" or "bool" ? nice + index : $"{nice}{index}, {unit}";
    }

    // ---------------------------------------------------------------- adding parts

    /// <summary>Adds a number box showing <paramref name="value"/>, below the centre. Returns its id.</summary>
    public PartId AddNumberBox(string value)
    {
        if (!Values.ContainsKey(value)) throw new ArgumentException("Pick what the number box shows.");
        Checkpoint();
        var (cx, cy) = GaugeCenter;
        var box = new JsonObject
        {
            ["type"] = "group",
            ["offset"] = new JsonArray(0, 0),
            ["layers"] = new JsonArray(
                new JsonObject { ["type"] = "rect", ["x"] = cx - 34, ["y"] = cy + 38, ["width"] = 68, ["height"] = 26, ["radius"] = 3, ["fill"] = "#000000", ["stroke"] = "#5A5A5A", ["strokeWidth"] = 1.5 },
                new JsonObject { ["type"] = "text", ["value"] = value, ["format"] = "0", ["x"] = cx, ["y"] = cy + 58, ["size"] = 20, ["color"] = "#F2F2F2" }),
        };
        return Append(box);
    }

    /// <summary>Adds plain text (a label, a maker's name...) in the middle. Returns its id.</summary>
    public PartId AddText(string text)
    {
        Checkpoint();
        var (cx, cy) = GaugeCenter;
        return Append(new JsonObject { ["type"] = "text", ["text"] = string.IsNullOrWhiteSpace(text) ? "TEXT" : text, ["x"] = cx, ["y"] = cy - 30, ["size"] = 14, ["color"] = "#F2F2F2" });
    }

    /// <summary>Adds a needle showing <paramref name="value"/> on the dial's scale (the first needle's), drawn on top. Returns its id.</summary>
    public PartId AddNeedle(string value)
    {
        if (!Values.ContainsKey(value)) throw new ArgumentException("Pick what the needle shows.");
        var scale = FirstNeedle()?.Layer is { } n ? Str(n, "scale") : Scales.Select(s => s.Key).FirstOrDefault();
        if (scale is null) throw new ArgumentException("This gauge has no dial scale for a needle to follow.");
        Checkpoint();
        var copy = FirstNeedle()?.Layer.DeepClone().AsObject()
                   ?? new JsonObject { ["type"] = "needle", ["length"] = 80, ["tail"] = 16, ["width"] = 3, ["color"] = "#F2F2F2" };
        copy["scale"] = scale;
        copy["value"] = value;
        copy["color"] = "#E0B13C";
        copy.Remove("tag");
        return Insert(AfterLastNeedle(), copy);
    }

    /// <summary>Adds a coloured arc (a green range, a red line...) on the dial's scale. Returns its id.</summary>
    public PartId AddArc(string colour)
    {
        var scaleName = FirstNeedle()?.Layer is { } n ? Str(n, "scale") : Scales.Select(s => s.Key).FirstOrDefault();
        if (scaleName is null || Scales[scaleName] is not JsonObject scale) throw new ArgumentException("This gauge has no dial scale for an arc to follow.");
        Checkpoint();
        var spec = scale.Deserialize<ScaleSpec>(GaugeDefinition.JsonOptions)!;
        var radius = Parts().Select(p => Layer(p.Id)).FirstOrDefault(l => KindOf(l) == PartKind.Ticks && Str(l, "scale") == scaleName) is { } ticks
            ? (Num(ticks, "radius") ?? 90) - 3 : 90;
        var low = spec.Low;
        var high = spec.High;
        var arc = new JsonObject
        {
            ["type"] = "arc", ["scale"] = scaleName, ["from"] = Math.Round(low + (high - low) * 0.6, 2), ["to"] = Math.Round(low + (high - low) * 0.8, 2),
            ["radius"] = radius, ["width"] = 5, ["color"] = IsColour(colour) ? colour : "#3E9E4A",
        };
        if (FirstNeedle()?.Layer["center"] is JsonArray c) arc["center"] = c.DeepClone();
        // Arcs go under the needles.
        var first = FirstNeedle();
        return first is null ? Append(arc) : Insert(first.Value.Id, arc);
    }

    // ---------------------------------------------------------------- one, two or four needles

    /// <summary>
    /// The engine needles on the dial: needles whose sim value ends in an engine number (":1", ":2"...),
    /// on the first needle's scale. 0 if the dial doesn't follow engines.
    /// </summary>
    public int EngineNeedles => EngineNeedleList().Count;

    /// <summary>
    /// One needle per engine, for 1 to 4 engines: adds needles for the missing engines (copies of the
    /// first, reading the next engine) or removes the extra ones. Needles get engine numbers when there
    /// is more than one. Returns null when done, else a plain reason it can't.
    /// </summary>
    public string? SetEngineNeedles(int count)
    {
        if (count is < 1 or > 4) return "Pick 1 to 4 needles.";
        var needles = EngineNeedleList();
        if (needles.Count == 0)
            return "This dial's needle doesn't follow an engine, so it can't be doubled. Use Add part, Needle instead.";
        if (needles.Count == count) return null;
        Checkpoint();

        if (count < needles.Count)
        {
            // Remove the highest engines first (from the end of the list so the ids stay right).
            foreach (var extra in needles.OrderByDescending(n => n.Engine).Take(needles.Count - count).OrderByDescending(n => n.Id.Path, PathOrder.Instance))
            {
                var (list, index) = Parent(extra.Id);
                list.RemoveAt(index);
            }
        }
        else
        {
            var template = needles.OrderBy(n => n.Engine).First();
            var have = needles.Select(n => n.Engine).ToHashSet();
            var spec = (Values[Str(template.Layer, "value")!] as JsonObject)!;
            var at = AfterLastNeedle();
            for (var engine = 1; have.Count < count; engine++)
            {
                if (!have.Add(engine)) continue;
                var sim = IndexSuffix().Replace(Str(spec, "sim")!, $":{engine}");
                var name = UseSimValueQuiet(sim, Str(spec, "unit") ?? "number", Num(spec, "default") ?? 0);
                var copy = template.Layer.DeepClone().AsObject();
                copy["value"] = name;
                Insert(at, copy, checkpoint: false);
                at = new PartId([.. at.Path[..^1], at.Path[^1] + 1]);
            }
        }

        // Number them when there's more than one; staggered pointer needles keep their numbers apart.
        var now = EngineNeedleList().OrderBy(n => n.Engine).ToList();
        var length = Num(now[0].Layer, "length") ?? 80;
        for (var i = 0; i < now.Count; i++)
        {
            if (now.Count > 1) now[i].Layer["tag"] = now[i].Engine.ToString(CultureInfo.InvariantCulture);
            else now[i].Layer.Remove("tag");
            if (Str(now[i].Layer, "style") == "pointer" && now.Count > 1) now[i].Layer["length"] = length - i * 6;
        }
        return null;
    }

    private List<(PartId Id, JsonObject Layer, int Engine)> EngineNeedleList()
    {
        var first = FirstNeedle();
        if (first is null) return [];
        var scale = Str(first.Value.Layer, "scale");
        var list = new List<(PartId, JsonObject, int)>();
        foreach (var (id, l) in DialNeedles())
        {
            if (Str(l, "scale") != scale) continue;
            if (Str(l, "value") is not { } v || Values[v] is not JsonObject spec || Str(spec, "sim") is not { } sim) continue;
            if (IndexSuffix().Match(sim) is { Success: true } m) list.Add((id, l, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));
        }
        // Only a dial where every needle follows the same kind of engine value counts.
        var kinds = list.Select(n => IndexSuffix().Replace(Str(Values[Str(n.Item2, "value")!]!.AsObject(), "sim")!, "")).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return kinds == 1 ? list : [];
    }

    private string UseSimValueQuiet(string sim, string unit, double fallback)
    {
        foreach (var (name, spec) in Values)
            if (spec is JsonObject o && string.Equals(Str(o, "sim"), sim, StringComparison.OrdinalIgnoreCase)) return name;
        var wanted = Slug(sim);
        var key = wanted;
        for (var n = 2; Values.ContainsKey(key); n++) key = $"{wanted}{n}";
        Values[key] = new JsonObject { ["sim"] = sim, ["unit"] = unit, ["default"] = fallback };
        return key;
    }

    // ---------------------------------------------------------------- whole gauge

    public string Name
    {
        get => Str(_root, "name") ?? "";
        set { Checkpoint(); _root["name"] = value.Trim(); }
    }

    public string Background
    {
        get => (_root["canvas"] as JsonObject) is { } c ? Str(c, "background") ?? "#000000" : "#000000";
        set
        {
            if (!IsColour(value)) throw new ArgumentException("A colour looks like #RRGGBB.");
            Checkpoint();
            if (_root["canvas"] is not JsonObject c) _root["canvas"] = c = new JsonObject { ["width"] = 320, ["height"] = 240 };
            c["background"] = value.Trim().ToUpperInvariant();
        }
    }

    /// <summary>Gives the gauge one of the ready-made looks (case and colours), as one undoable change.</summary>
    /// <exception cref="ArgumentException">This gauge can't change its look; the message says why.</exception>
    public void ApplyLook(GaugeLook look)
    {
        var json = GaugeParts.Reskin(Json, look);
        Replace(json);
    }

    // ---------------------------------------------------------------- quick colours (the easy way to recolour)

    /// <summary>The parts' colours sorted into plain groups for "Quick colours", in this order.</summary>
    public static readonly IReadOnlyList<string> ColourRoles =
        ["Background", "Case and dial", "Markings and numbers", "Number readouts", "Needles", "Coloured ranges", "Other parts"];

    /// <summary>
    /// Every colour the gauge uses, per group (background, case and dial, markings, readouts, needles, ranges), most
    /// used first within a group. Changing one with <see cref="ReplaceColour"/> changes it only in that group, so
    /// the needles can turn orange while white numbers stay white.
    /// </summary>
    public IReadOnlyList<(string Role, string Colour, int Count)> ColourGroups()
    {
        var found = new List<(string Role, string Colour)>();
        if (_root["canvas"] is JsonObject canvas && Str(canvas, "background") is { } bg && IsColour(bg)) found.Add(("Background", bg.Trim().ToUpperInvariant()));
        if (_root["layers"] is JsonArray layers) CollectColours(layers, found);
        return [.. found.GroupBy(f => f)
            .Select(g => (g.Key.Role, g.Key.Colour, Count: g.Count()))
            .OrderBy(g => ColourRoles.ToList().IndexOf(g.Role)).ThenByDescending(g => g.Count)];
    }

    /// <summary>Changes one colour to another, only in the parts of one group, as one undoable change.</summary>
    public void ReplaceColour(string role, string from, string to)
    {
        if (!IsColour(to)) throw new ArgumentException($"\"{to}\" isn't a colour like #FFFFFF.");
        from = from.Trim().ToUpperInvariant();
        to = to.Trim().ToUpperInvariant();
        Checkpoint();
        if (role == "Background")
        {
            if (_root["canvas"] is JsonObject canvas && Str(canvas, "background")?.Trim().ToUpperInvariant() == from) canvas["background"] = to;
            return;
        }
        if (_root["layers"] is JsonArray layers) ReplaceColours(layers, role, from, to);
    }

    private static void CollectColours(JsonArray layers, List<(string Role, string Colour)> found)
    {
        foreach (var layer in layers.OfType<JsonObject>())
        {
            var role = RoleOf(layer);
            foreach (var (key, node) in layer)
            {
                if (node is JsonArray children) CollectColours(children, found);
                else if (node is JsonValue v && v.TryGetValue<string>(out var s) && key != "type" && IsColour(s)) found.Add((role, s.Trim().ToUpperInvariant()));
            }
        }
    }

    private static void ReplaceColours(JsonArray layers, string role, string from, string to)
    {
        foreach (var layer in layers.OfType<JsonObject>())
        {
            var mine = RoleOf(layer) == role;
            foreach (var (key, node) in layer.ToList())
            {
                if (node is JsonArray children) ReplaceColours(children, role, from, to);
                else if (mine && node is JsonValue v && v.TryGetValue<string>(out var s) && key != "type" && IsColour(s) && s.Trim().ToUpperInvariant() == from) layer[key] = to;
            }
        }
    }

    private static string RoleOf(JsonObject layer) => Str(layer, "type") switch
    {
        "rect" or "circle" or "glare" or "screw" => "Case and dial",
        "ticks" or "labels" => "Markings and numbers",
        "text" => layer["value"] is null ? "Markings and numbers" : "Number readouts",
        "needle" or "hub" => "Needles",
        "arc" => "Coloured ranges",
        _ => "Other parts",
    };

    // ---------------------------------------------------------------- where parts are (for picking and dragging)

    /// <summary>
    /// Roughly where a part is drawn on the FIP screen, as (x, y, width, height), with its gauge values at
    /// rest. Good enough to show which part is picked and to pick a part by clicking it.
    /// </summary>
    public (double X, double Y, double W, double H)? Bounds(PartId id)
    {
        var layer = Layer(id);
        var (b, ok) = LocalBounds(layer);
        if (!ok) return null;
        // Groups above it: their offset and zoom.
        for (var depth = id.Path.Length - 1; depth >= 1; depth--)
        {
            var group = Layer(new PartId(id.Path[..depth]));
            var z = Num(group, "zoom") ?? 1;
            var (ox, oy) = group["offset"] is JsonArray { Count: 2 } o ? (D(o[0]), D(o[1])) : (0, 0);
            b = (ox + b.X * z, oy + b.Y * z, b.W * z, b.H * z);
        }
        return b;
    }

    /// <summary>The part drawn on top at screen point (x, y), preferring small parts; null if none.</summary>
    public PartId? PartAt(double x, double y)
    {
        GaugePart? best = null;
        var bestArea = double.MaxValue;
        foreach (var part in Parts())
        {
            if (part.Kind is PartKind.Glass) continue; // the glass covers everything
            if (Bounds(part.Id) is not { } b || x < b.X || y < b.Y || x > b.X + b.W || y > b.Y + b.H) continue;
            var area = b.W * b.H;
            if (area <= bestArea) { best = part; bestArea = area; }
        }
        return best?.Id;
    }

    private ((double X, double Y, double W, double H), bool) LocalBounds(JsonObject layer)
    {
        var (cx, cy) = CenterOf(layer);
        (double, double, double, double) Around(double r) => (cx - r, cy - r, r * 2, r * 2);
        switch (KindOf(layer))
        {
            case PartKind.Box:
                return ((Num(layer, "x") ?? 0, Num(layer, "y") ?? 0, Num(layer, "width") ?? 0, Num(layer, "height") ?? 0), true);
            case PartKind.Text or PartKind.Number:
            {
                var size = Num(layer, "size") ?? 14;
                var shown = Str(layer, "value") is not null ? new string('0', Math.Max(2, (Str(layer, "format") ?? "0").Length + 1)) : Str(layer, "text") ?? "";
                var w = Math.Max(size, shown.Length * size * 0.55);
                var x = Num(layer, "x") ?? 0;
                var left = Str(layer, "align") switch { "left" => x, "right" => x - w, _ => x - w / 2 };
                return ((left, (Num(layer, "y") ?? 0) - size * 0.8, w, size), true);
            }
            case PartKind.Screw:
                return (((Num(layer, "x") ?? 0) - 7, (Num(layer, "y") ?? 0) - 7, 14, 14), true);
            case PartKind.Circle or PartKind.Hub or PartKind.Glass:
                return (Around(Num(layer, "r") ?? 10), true);
            case PartKind.Ticks:
                return (Around(Math.Abs(Num(layer, "radius") ?? 90)), true);
            case PartKind.Labels:
                return (Around(Math.Abs(Num(layer, "radius") ?? 70) + (Num(layer, "size") ?? 16)), true);
            case PartKind.Arc:
                return (Around(Math.Abs(Num(layer, "radius") ?? 90) + (Num(layer, "width") ?? 4) / 2), true);
            case PartKind.Needle:
            {
                var (b, _) = NeedleBounds(layer, cx, cy);
                return (b, true);
            }
            case PartKind.Group or PartKind.NumberBox:
            {
                double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, bt = double.MinValue;
                foreach (var child in (layer["layers"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    var (cb, ok) = LocalBounds(child);
                    if (!ok) continue;
                    l = Math.Min(l, cb.X); t = Math.Min(t, cb.Y); r = Math.Max(r, cb.X + cb.W); bt = Math.Max(bt, cb.Y + cb.H);
                }
                if (l == double.MaxValue) return (default, false);
                var z = Num(layer, "zoom") ?? 1;
                var (ox, oy) = layer["offset"] is JsonArray { Count: 2 } o ? (D(o[0]), D(o[1])) : (0, 0);
                return ((ox + l * z, oy + t * z, (r - l) * z, (bt - t) * z), true);
            }
        }
        return (default, false);
    }

    /// <summary>Where the needle points with its value at rest (the gauge's default for it).</summary>
    private ((double, double, double, double), bool) NeedleBounds(JsonObject layer, double cx, double cy)
    {
        var length = Num(layer, "length") ?? 80;
        var angle = 0.0;
        try
        {
            if (Str(layer, "scale") is { } s && Scales[s] is JsonObject so && so.Deserialize<ScaleSpec>(GaugeDefinition.JsonOptions) is { } spec)
            {
                var v = Str(layer, "value") is { } name && Values[name] is JsonObject vs ? Num(vs, "default") ?? 0 : 0;
                angle = spec.AngleFor(v);
                length = spec.Reach(v, (float)length);
            }
        }
        catch (JsonException) { }
        var rad = angle * Math.PI / 180;
        var (tx, ty) = (cx + Math.Sin(rad) * length, cy - Math.Cos(rad) * length);
        const double pad = 6;
        return ((Math.Min(cx, tx) - pad, Math.Min(cy, ty) - pad, Math.Abs(tx - cx) + pad * 2, Math.Abs(ty - cy) + pad * 2), true);
    }

    // ---------------------------------------------------------------- helpers

    private JsonArray Layers => (JsonArray)_root["layers"]!;

    private JsonObject Values
    {
        get
        {
            if (_root["values"] is not JsonObject v) _root["values"] = v = new JsonObject();
            return v;
        }
    }

    private JsonObject Scales => _root["scales"] as JsonObject ?? [];

    private (double X, double Y) GaugeCenter =>
        _root["center"] is JsonArray { Count: 2 } c ? (D(c[0]), D(c[1])) : (160, 120);

    private (double X, double Y) CenterOf(JsonObject layer) =>
        layer["center"] is JsonArray { Count: 2 } c ? (D(c[0]), D(c[1])) : GaugeCenter;

    private JsonObject Layer(PartId id)
    {
        var (list, index) = Parent(id);
        return list[index] as JsonObject ?? throw new ArgumentException("That part is gone.");
    }

    private (JsonArray List, int Index) Parent(PartId id)
    {
        if (id.Path.Length == 0) throw new ArgumentException("No part picked.");
        var list = Layers;
        for (var depth = 0; depth < id.Path.Length - 1; depth++)
            list = (list.ElementAtOrDefault(id.Path[depth]) as JsonObject)?["layers"] as JsonArray ?? throw new ArgumentException("That part is gone.");
        var index = id.Path[^1];
        if (index < 0 || index >= list.Count) throw new ArgumentException("That part is gone.");
        return (list, index);
    }

    private PartId Append(JsonObject layer)
    {
        // New parts go under the glass shine and the needles' centre cap, if there are any at the end.
        var at = Layers.Count;
        while (at > 0 && Layers[at - 1] is JsonObject last && KindOf(last) is PartKind.Glass or PartKind.Hub) at--;
        return Insert(new PartId([at]), layer, checkpoint: false);
    }

    private PartId Insert(PartId at, JsonObject layer, bool checkpoint = false)
    {
        if (checkpoint) Checkpoint();
        var list = Layers;
        for (var depth = 0; depth < at.Path.Length - 1; depth++) list = (JsonArray)((JsonObject)list[at.Path[depth]]!)["layers"]!;
        var index = Math.Clamp(at.Path[^1], 0, list.Count);
        list.Insert(index, layer);
        return new PartId([.. at.Path[..^1], index]);
    }

    /// <summary>The dial's first needle, also inside a group (many gauges keep the whole dial in one group).</summary>
    private (PartId Id, JsonObject Layer)? FirstNeedle() =>
        Parts().FirstOrDefault(p => p.Kind == PartKind.Needle) is { } part ? (part.Id, Layer(part.Id)) : null;

    /// <summary>The needles next to the first needle (same list of layers): (id, layer) in drawing order.</summary>
    private List<(PartId Id, JsonObject Layer)> DialNeedles()
    {
        if (FirstNeedle() is not { } first) return [];
        var (list, _) = Parent(first.Id);
        var prefix = first.Id.Path[..^1];
        var found = new List<(PartId, JsonObject)>();
        for (var i = 0; i < list.Count; i++)
            if (list[i] is JsonObject l && KindOf(l) == PartKind.Needle) found.Add((new PartId([.. prefix, i]), l));
        return found;
    }

    private PartId AfterLastNeedle()
    {
        var needles = DialNeedles();
        if (needles.Count == 0) return new PartId([Layers.Count]);
        var last = needles[^1].Id.Path;
        return new PartId([.. last[..^1], last[^1] + 1]);
    }

    private static (JsonObject Target, string Prop) Target(JsonObject layer, string key)
    {
        if (!key.Contains('.')) return (layer, key);
        var (rect, text) = NumberBoxParts(layer);
        return key.Split('.', 2) is [var which, var prop] ? (which == "rect" ? rect : text, prop) : (layer, key);
    }

    /// <summary>A group made by Add number box: a box with a number in it.</summary>
    private static bool IsNumberBox(JsonObject layer) =>
        Str(layer, "type") == "group" && layer["rotate"] is null && layer["moveX"] is null && layer["moveY"] is null
        && layer["layers"] is JsonArray { Count: 2 } l
        && l[0] is JsonObject a && Str(a, "type") == "rect"
        && l[1] is JsonObject b && Str(b, "type") == "text" && b.ContainsKey("value");

    private static (JsonObject Rect, JsonObject Text) NumberBoxParts(JsonObject layer)
    {
        var l = (JsonArray)layer["layers"]!;
        return ((JsonObject)l[0]!, (JsonObject)l[1]!);
    }

    private static PartKind KindOf(JsonObject layer) => Str(layer, "type") switch
    {
        "rect" => PartKind.Box,
        "circle" => PartKind.Circle,
        "screw" => PartKind.Screw,
        "ticks" => PartKind.Ticks,
        "labels" => PartKind.Labels,
        "arc" => PartKind.Arc,
        "text" => layer.ContainsKey("value") ? PartKind.Number : PartKind.Text,
        "needle" => PartKind.Needle,
        "hub" => PartKind.Hub,
        "glare" => PartKind.Glass,
        "group" => IsNumberBox(layer) ? PartKind.NumberBox : PartKind.Group,
        _ => PartKind.Other,
    };

    private string Title(JsonObject layer)
    {
        // Short names for the list (no unit): "Needle: General eng rpm (engine 2)".
        string Shows(string? value) => value is not null && Values[value] is JsonObject spec
            ? Str(spec, "sim") is { } sim ? SimLabel(sim, null) : Label(value, spec)
            : value ?? "";
        return KindOf(layer) switch
        {
            PartKind.Needle => $"Needle: {Shows(Str(layer, "value"))}",
            PartKind.NumberBox => $"Number box: {Shows(Str(NumberBoxParts(layer).Text, "value"))}",
            PartKind.Number => $"Number: {Shows(Str(layer, "value"))}",
            PartKind.Text => $"Text \"{Shorten(Str(layer, "text") ?? "", 18)}\"",
            PartKind.Ticks => "Tick marks",
            PartKind.Labels => layer["texts"] is JsonObject ? "Scale letters" : "Scale numbers",
            PartKind.Arc => $"Coloured arc ({ColourName(Str(layer, "color"))})",
            PartKind.Circle => Num(layer, "r") is > 60 ? "Dial face / ring" : "Circle",
            PartKind.Box => "Box",
            PartKind.Screw => "Screw",
            PartKind.Hub => "Centre cap",
            PartKind.Glass => "Glass shine",
            PartKind.Group => layer["rotate"] is not null ? "Turning part" : layer["moveX"] is not null || layer["moveY"] is not null ? "Sliding part" : "Group",
            _ => Str(layer, "type") ?? "Part",
        };
    }

    private static string Label(string name, JsonObject? spec) =>
        spec is not null && Str(spec, "sim") is { } sim ? SimLabel(sim, Str(spec, "unit"))
        : spec is not null && Str(spec, "expr") is not null ? $"{Pretty(name)} (worked out)"
        : Pretty(name);

    private static string Pretty(string name)
    {
        var words = name.Replace('_', ' ').Trim();
        return words.Length == 0 ? name : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string ColourName(string? hex)
    {
        if (hex is null || !IsColour(hex)) return "colour";
        var h = hex.TrimStart('#');
        if (h.Length == 8) h = h[2..];
        var (r, g, b) = (Convert.ToInt32(h[..2], 16), Convert.ToInt32(h[2..4], 16), Convert.ToInt32(h[4..], 16));
        if (r > 180 && g < 110 && b < 110) return "red";
        if (g > r + 30 && g > b + 30) return "green";
        if (r > 170 && g > 130 && b < 100) return "yellow";
        if (b > r + 30 && b > g) return "blue";
        if (r > 200 && g > 200 && b > 200) return "white";
        return "colour";
    }

    public static bool IsColour(string? text) => text is not null && ColourPattern().IsMatch(text.Trim());

    private static string Slug(string sim)
    {
        var s = NonWord().Replace(sim.Trim().ToLowerInvariant(), "_").Trim('_');
        return s.Length == 0 ? "value" : s;
    }

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double? Num(JsonObject o, string key) => o[key] is JsonValue v ? AsDouble(v) : null;

    private static double D(JsonNode? node) => node is JsonValue v ? AsDouble(v) ?? 0 : 0;

    /// <summary>A number from the file or one set by the editor (which may be held as int, float...).</summary>
    private static double? AsDouble(JsonValue v) =>
        v.TryGetValue<double>(out var d) ? d
        : v.TryGetValue<int>(out var i) ? i
        : v.TryGetValue<long>(out var l) ? l
        : v.TryGetValue<float>(out var f) ? f
        : v.TryGetValue<decimal>(out var m) ? (double)m
        : null;

    private sealed class PathOrder : IComparer<int[]>
    {
        public static readonly PathOrder Instance = new();
        public int Compare(int[]? a, int[]? b)
        {
            for (var i = 0; i < Math.Min(a!.Length, b!.Length); i++)
                if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return a.Length.CompareTo(b.Length);
        }
    }

    [GeneratedRegex(@":(\d+)$")]
    private static partial Regex IndexSuffix();

    [GeneratedRegex(@"^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex ColourPattern();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonWord();
}

/// <summary>Which part: its position in the layers, through any groups.</summary>
public readonly record struct PartId(int[] Path)
{
    public bool Equals(PartId other) => Path.AsSpan().SequenceEqual(other.Path);
    public override int GetHashCode() => Path.Aggregate(17, (h, i) => h * 31 + i);
    public override string ToString() => string.Join('/', Path);
}

public enum PartKind { Box, Circle, Screw, Ticks, Labels, Arc, Text, Number, NumberBox, Needle, Hub, Glass, Group, Other }

public sealed record GaugePart(PartId Id, int Depth, PartKind Kind, string Title);

public enum SettingKind { Colour, Number, Text, Choice, Value }

/// <summary>One thing that can be changed on a part, with its current value as text.</summary>
public sealed record PartSetting(string Key, string Label, SettingKind Kind, string? Current, double Min = 0, double Max = 0, double Step = 1, IReadOnlyList<string>? Choices = null)
{
    public static PartSetting Colour(string key, string label, string? current) => new(key, label, SettingKind.Colour, current);
    public static PartSetting Text(string key, string label, string current) => new(key, label, SettingKind.Text, current);
    public static PartSetting Value(string key, string label, string? current) => new(key, label, SettingKind.Value, current);
    public static PartSetting Choice(string key, string label, string current, IReadOnlyList<string> choices) => new(key, label, SettingKind.Choice, current, Choices: choices);
    public static PartSetting Number(string key, string label, double current, double min, double max, double step) =>
        new(key, label, SettingKind.Number, current.ToString("0.###", CultureInfo.InvariantCulture), min, max, step);
}
