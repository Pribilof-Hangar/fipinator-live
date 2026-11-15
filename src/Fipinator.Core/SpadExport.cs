// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using SkiaSharp;

namespace Fipinator.Core;

/// <summary>
/// Saves a gauge as a SPAD.neXt FIP gauge: a .zip with an XML file and PNG pictures, written to
/// SPAD.neXt's published gauge layout (gauge.xsd in github.com/SPAD-neXt/DemoCode, MIT) and its docs
/// (docs.spadnext.com, "Gauges and Extensions"). Nothing is taken from SPAD itself or anyone's gauges:
/// our renderer draws every picture.
///
/// How a gauge is split: everything drawn before the first needle becomes the background picture; each
/// needle becomes its own picture (drawn pointing right, cut to size) that SPAD turns around the pivot;
/// anything drawn on top of a needle (hub, glass glare) becomes a see-through cover picture.
///
/// Exported: needles, number displays, growing arcs, worked-out values, turning cards (compass cards, a
/// heading bug on the card), parts that slide one way (ILS bars, flags), and knobs and S buttons that send a
/// plain sim event. Not yet: parts that slide two ways or slide and turn (attitude horizon) and needles riding
/// on a card: <see cref="Check"/> lists them, and nothing is written for such a gauge. Left-knob page flipping, auto-flip and knob modes are FIPinator Live only.
/// </summary>
public static class SpadExport
{
    /// <param name="ZipFile">The gauge file to give SPAD.neXt.</param>
    /// <param name="XmlName">The gauge description's name inside the .zip.</param>
    public sealed record Result(string ZipFile, string XmlName, IReadOnlyList<string> LeftOut);

    private const string EastScale = "__spad_east", EastValue = "__spad_rest";

    /// <summary>What stops this gauge from being exported (empty if it can be).</summary>
    public static List<string> Check(GaugeDefinition gauge)
    {
        var problems = new List<string>();
        // PMDG 737 data ("PMDG:" values) comes through PMDG's own SimConnect data block, which only FIPinator Live reads.
        if (gauge.Values.Values.Any(v => v.Sim?.StartsWith("PMDG:", StringComparison.Ordinal) == true))
            problems.Add("the plane's own data (737-800 add-on), which only FIPinator Live can read");
        var moving = CheckMovingGroups(gauge, gauge.Layers, [], problems);
        foreach (var layer in Flatten(gauge.Layers))
        {
            switch (layer)
            {
                case TextLayer { Value: not null } t:
                    moving++;
                    if (ValueRpn(gauge, t.Value) is null) problems.Add($"a number display with a formula the export can't translate ('{t.Value}')");
                    else if (NumberStyleOf(t.Format) is null) problems.Add($"a number display in a format the export can't do yet ('{t.Format}')");
                    break;
                case ArcLayer { ToValue: not null } a:
                    moving++;
                    if (ValueRpn(gauge, a.ToValue) is null) problems.Add($"a growing arc with a formula the export can't translate ('{a.ToValue}')");
                    break;
                case NeedleLayer n:
                    moving++;
                    if (!gauge.Scales.TryGetValue(n.Scale, out var scale)) { problems.Add($"a needle on a missing scale '{n.Scale}'"); break; }
                    if (ValueRpn(gauge, n.Value) is null) problems.Add($"a needle with a formula the export can't translate ('{n.Value}')");
                    break;
            }
        }
        if (moving == 0) problems.Add("no needle or number display (nothing for SPAD to move)");
        return problems.Distinct().ToList();
    }

    /// <summary>
    /// Turning cards (a compass card, an ADF card, a heading bug on the card, a turn coordinator's airplane):
    /// a group that turns around its centre. SPAD.neXt turns the card as one picture, like a big needle.
    /// Sliding parts (an ILS needle, a warning flag that slides out of sight): a group that moves one way.
    /// It's drawn at each position (cut to the dial window), and SPAD.neXt picks the picture by value.
    /// Still to come: parts that slide two ways or slide and turn (attitude horizon), parts on parts that
    /// slide, and needles riding on a card. Returns how many turning cards and sliding parts there are.
    /// </summary>
    private static int CheckMovingGroups(GaugeDefinition gauge, List<Layer> layers, List<GroupLayer> around, List<string> problems)
    {
        var cards = 0;
        foreach (var g in layers.OfType<GroupLayer>())
        {
            if (g.MoveX is not null || g.MoveY is not null)
            {
                cards++;
                foreach (var link in new[] { g.MoveX, g.MoveY }.OfType<ValueLink>())
                {
                    if (!gauge.Scales.TryGetValue(link.Scale, out var slideScale)) problems.Add($"a sliding part on a missing scale '{link.Scale}'");
                    else if (slideScale.Points is { Length: > 0 }) problems.Add("a sliding part on an uneven scale");
                    else if (slideScale.Max == slideScale.Min) problems.Add("a sliding part on a scale with no range");
                    if (ValueRpn(gauge, link.Value) is null) problems.Add($"a sliding part with a formula the export can't translate ('{link.Value}')");
                }
                if (g.Rotate is not null) { problems.Add("a part that slides and turns at once"); continue; }
                if (around.Any(a => a.Rotate is not null))
                {
                    // On a turning part (an attitude horizon, an HSI's CDI bar): one element that slides and turns.
                    if (g.MoveX is not null && g.MoveY is not null) problems.Add("a part that slides two ways on a turning part");
                    else if (around.Any(a => a.MoveX is not null || a.MoveY is not null)) problems.Add("a part that slides on another sliding, turning part");
                    else if (around.First(a => a.Rotate is not null) is var turning && HorizonProblem(gauge, around, turning, g) is { } horizon) problems.Add(horizon);
                    if (Flatten(g.Layers).Any(l => l is GroupLayer { Rotate: not null } or GroupLayer { MoveX: not null } or GroupLayer { MoveY: not null }
                                                   or NeedleLayer or TextLayer { Value: not null } or ArcLayer { ToValue: not null }))
                        problems.Add("a needle, number or moving part riding on a sliding, turning part");
                    continue;
                }
                // Sliding parts on sliding parts (a flight-director bar that hides, a localizer pointer that moves both
                // ways): drawn as pictures for each position. Parts that only hide don't count as moves; at most two moves.
                var slides = around.Where(a => a.MoveX is not null || a.MoveY is not null).Append(g).ToList();
                var moves = slides.Where(a => !Hides(gauge, a)).Sum(a => (a.MoveX is null ? 0 : 1) + (a.MoveY is null ? 0 : 1));
                if (moves > 2) problems.Add("a part that slides more than two ways");
                if (Flatten(g.Layers).Any(l => l is GroupLayer { Rotate: not null } or NeedleLayer or ArcLayer { ToValue: not null }))
                    problems.Add("a needle or turning part riding on a sliding part");
                // A number on a part that only hides (a radio altitude that disappears high up) blanks its digits.
                foreach (var t in g.Layers.OfType<TextLayer>().Where(t => t.Value is not null))
                    if (moves > 0) problems.Add("a number riding on a sliding part");
                cards += CheckMovingGroups(gauge, g.Layers, [.. around, g], problems);
                continue;
            }
            if (g.Rotate is { } rot)
            {
                cards++;
                var turningAround = around.Where(a => a.Rotate is not null).ToList();
                if (g.ClipBox is not null) problems.Add("a turning part cut to a box");
                if (!gauge.Scales.TryGetValue(rot.Scale, out var scale)) problems.Add($"a turning part on a missing scale '{rot.Scale}'");
                else if (scale.Points is { Length: > 0 } && (turningAround.Count > 0 || Flatten(g.Layers).Any(l => l is GroupLayer { Rotate: not null })))
                    problems.Add("a turning part on an uneven scale, on or under another turning part");
                if (ValueRpn(gauge, rot.Value) is null) problems.Add($"a turning part with a formula the export can't translate ('{rot.Value}')");
                // A card on a card (a heading bug): it must turn around the same centre.
                if (turningAround.Count > 0)
                {
                    var outer = turningAround[0];
                    var between = around.SkipWhile(a => !ReferenceEquals(a, outer)).Skip(1).Append(g);
                    if (between.Any(b => b.Offset is { Length: 2 } o && (o[0] != 0 || o[1] != 0) || b.Zoom is > 0 and not 1)
                        || !SameCenter(g.Center ?? gauge.Center, outer.Center ?? gauge.Center))
                        problems.Add("a turning part on another turning part with a different centre");
                }
                if (Flatten(g.Layers).Any(l => l is NeedleLayer or TextLayer { Value: not null } or ArcLayer { ToValue: not null }))
                    problems.Add("a needle or number riding on a turning card (like an HSI's course needle)");
            }
            cards += CheckMovingGroups(gauge, g.Layers, [.. around, g], problems);
        }
        return cards;
    }

    /// <summary>
    /// A part that slides inside a turning card (an attitude horizon: pitch slides it, bank turns it). SPAD.neXt
    /// does this in one element (Shift + Rotate, as in its bundled Saitek attitude gauge), with a cover picture
    /// on top that hides what spills outside the round window. So the windows must be circles round the turning
    /// centre, and nothing between the outermost window and the part may move or resize it.
    /// </summary>
    private static string? HorizonProblem(GaugeDefinition gauge, List<GroupLayer> around, GroupLayer turning, GroupLayer slide)
    {
        var chain = around.Append(slide).ToList();
        var center = turning.Center ?? gauge.Center;
        var firstTurnIndex = chain.IndexOf(turning);
        // A box window must be fixed on the screen (outside the turning part), and nothing that turns may be drawn
        // before the horizon (its cover would have to turn with it).
        if (chain.Skip(firstTurnIndex).Any(g => g.ClipBox is not null)) return "a turning, sliding part cut to a turning box";
        if (chain.Any(g => g.ClipBox is not null)
            && !ReferenceEquals(Flatten(gauge.Layers).OfType<GroupLayer>().FirstOrDefault(g => g.Rotate is not null), turning))
            return "a turning, sliding part in a box window with other turning parts under it";
        // Several turns (an HSI's CDI bar: the course pointer on the compass card): all round one centre, nothing
        // between them that moves or resizes.
        var firstTurn = chain.IndexOf(turning);
        for (var i = firstTurn + 1; i < chain.Count; i++)
        {
            var g = chain[i];
            if (g.Offset is { Length: 2 } o && (o[0] != 0 || o[1] != 0) || g.Zoom is > 0 and not 1)
                return "a turning, sliding part moved or resized inside its turning card";
            if (g.Rotate is not null && !SameCenter(g.Center ?? gauge.Center, center))
                return "a sliding part on turning parts with different centres";
        }
        var first = chain.FindIndex(g => g.Clip is not null);
        if (first < 0) return null;
        for (var i = first; i < chain.Count; i++)
        {
            var g = chain[i];
            if (i > first && (g.Offset is { Length: 2 } o && (o[0] != 0 || o[1] != 0) || g.Zoom is > 0 and not 1))
                return "a turning, sliding part moved or resized inside its window";
            if (g.Clip is not null && i >= firstTurnIndex && !SameCenter(g.Center ?? gauge.Center, center))
                return "a turning, sliding part whose window isn't round its turning centre";
        }
        return null;
    }

    /// <summary>
    /// A part that slides only to hide (a flag: here, or pushed off the FIP): its slide is 0 for one value and at
    /// least the FIP's size for the other.
    /// </summary>
    private static bool Hides(GaugeDefinition gauge, GroupLayer g)
    {
        var link = (g.MoveX ?? g.MoveY)!;
        if (!gauge.Scales.TryGetValue(link.Scale, out var scale) || scale.Points is { Length: > 0 } || ValueRpn(gauge, link.Value) is null) return false;
        return HideShifts(gauge, link.Value, scale, g.MoveX is not null);
    }

    private static bool HideShifts(GaugeDefinition gauge, string value, ScaleSpec scale, bool across)
    {
        var edge = across ? gauge.Canvas.Width : gauge.Canvas.Height;
        bool HereOrGone(double x, double y) => Math.Min(Math.Abs(scale.AngleFor(x)), Math.Abs(scale.AngleFor(y))) < 0.5
                                              && Math.Max(Math.Abs(scale.AngleFor(x)), Math.Abs(scale.AngleFor(y))) * 0.8 >= edge;
        // The usual way to hide: "choose(condition, 0, 400)" (here, or pushed off the FIP), whatever the condition;
        // the sampled inputs may never reach the hiding point (a radio altitude only hides above 2,500 ft).
        if (gauge.Values.TryGetValue(value, out var spec) && spec.Expr is { } expr
            && System.Text.RegularExpressions.Regex.Match(expr.Trim(), @"^choose\(.*,\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\)$") is { Success: true } m
            && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var first)
            && double.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var second))
            return HereOrGone(first, second);
        var (lo, hi) = Span(gauge, value);
        return HereOrGone(lo, hi);
    }

    private static bool SameCenter(float[] a, float[] b) => Math.Abs(a[0] - b[0]) < 0.5 && Math.Abs(a[1] - b[1]) < 0.5;

    /// <summary>
    /// Writes the gauge as <paramref name="parent"/>\FIPinator - &lt;gauge name&gt;.zip. Never replaces a
    /// .zip that isn't one of our own exports. Throws <see cref="InvalidOperationException"/> if the gauge can't
    /// be exported yet (see <see cref="Check"/>).
    /// </summary>
    /// <param name="category">The group SPAD.neXt lists it under (its gauge list doesn't show subfolders), e.g.
    /// "DC-6"; shown as "FIPinator: DC-6". Default "FIPinator".</param>
    public static Result Export(string gaugePath, string parent, string? category = null)
    {
        gaugePath = Path.GetFullPath(gaugePath);
        var gauge = GaugeDefinition.Load(gaugePath);
        var problems = Check(gauge);
        if (problems.Count > 0)
            throw new InvalidOperationException($"\"{gauge.Name}\" can't be saved for SPAD.neXt yet. It has {string.Join("; ", problems)}.");

        var baseDir = Path.GetDirectoryName(gaugePath)!;
        var json = JsonNode.Parse(File.ReadAllText(gaugePath))!.AsObject();
        // Fonts are found relative to the gauge file: keep the path absolute for the copies we draw.
        if (json["font"]?.GetValue<string>() is { } font && !Path.IsPathRooted(font))
            json["font"] = Path.GetFullPath(Path.Combine(baseDir, font));

        // SPAD.neXt keeps each shared gauge as one file (a .zip), so the parts are made in a scratch
        // folder and packed into "<parent>\FIPinator - <name>.zip".
        var safe = SafeName("FIPinator - " + gauge.Name);
        Directory.CreateDirectory(parent);
        var zipFile = Path.Combine(parent, safe + ".zip");
        if (File.Exists(zipFile) && !IsOurs(zipFile))
            throw new IOException($"A file called \"{safe}.zip\" already exists and wasn't made by FIPinator. Rename or move it first.");
        var folder = Directory.CreateTempSubdirectory("fipinator-spad-").FullName;
        // SPAD.neXt reads a gauge's pictures from the "1024" folder inside the .zip (its log asks for
        // "1024\background.png"); the XML names them without the folder.
        var art = Directory.CreateDirectory(Path.Combine(folder, ArtFolder)).FullName;
        try
        {

        // Number every drawn layer in drawing order, then cut the list at each moving part (a needle or
        // a number display); the still parts between them become pictures.
        var leaves = new List<JsonObject>();
        Collect(json["layers"]!.AsArray(), leaves);
        var leafLayers = Flatten(gauge.Layers).Where(l => l is not GroupLayer).ToList(); // same order as leaves
        // Each still part belongs to the innermost turning card or sliding part around it (null: it stays put).
        // Horizon parts that move exactly alike (the sky and ground, and the pitch ladder in its own round window:
        // same turns, same slide) become one picture; each maps to the first of them in drawing order.
        var sameHorizon = SameHorizonParts(json["layers"]!.AsArray());
        // Not turning: the innermost sliding part that really moves (else the outermost one that hides) owns it;
        // the parts around it that move or hide are drawn into its pictures.
        var cardOf = leaves.Select(l => Around(json["layers"]!.AsArray(), l) is { } chain
            ? chain.Any(g => g["rotate"] is JsonObject)
                ? chain.LastOrDefault(g => Moves(g) && !(Slides(g) && chain.TakeWhile(a => !ReferenceEquals(a, g)).Any(Slides))) is { } c
                    ? sameHorizon.GetValueOrDefault(c, c) : null
                : chain.Where(Slides).LastOrDefault(g => !HidesJson(gauge, g)) ?? chain.FirstOrDefault(Slides)
            : null).ToList();
        var segments = new List<(bool Needle, JsonObject? Card, List<int> Leaves)>();
        for (var i = 0; i < leaves.Count; i++)
        {
            var moves = leafLayers[i] is NeedleLayer or TextLayer { Value: not null } or ArcLayer { ToValue: not null };
            var card = moves ? null : cardOf[i];
            if (moves || segments.Count == 0 || segments[^1].Needle || !ReferenceEquals(segments[^1].Card, card)) segments.Add((moves, card, []));
            segments[^1].Leaves.Add(i);
        }
        var cardCount = 0;
        var slideCount = 0;
        var numberCount = 0;
        var arcCount = 0;

        var xml = new XElement("Gauge", new XAttribute("Name", gauge.Name), new XAttribute("Version", "1.0"));
        var size = $"{gauge.Canvas.Width},{gauge.Canvas.Height},{gauge.Canvas.Width},{gauge.Canvas.Height}";
        var needleCount = 0;
        var coverCount = 0;
        var horizonCount = 0;
        // The still pictures drawn so far (a window cover over a horizon repeats them outside the window).
        var stills = new List<(bool Background, List<int> Keep)>();
        var cards = new List<List<int>>();
        var drawn = new List<(bool Card, int Index)>();
        XElement Cover(List<int> keep, bool background, SKBitmap? window)
        {
            var name = $"cover{++coverCount}.png";
            using (var picture = RenderOnly(json.DeepClone().AsObject(), leaves.Count, keep, background, baseDir))
            {
                if (window is not null) Punch(picture, window);
                Save(picture, Path.Combine(art, name));
            }
            return new XElement("Element", new XAttribute("id", $"cover{coverCount}"),
                new XElement("Position", new XAttribute("X", 0), new XAttribute("Y", 0)),
                new XElement("Image", new XAttribute("Name", name), new XAttribute("ImageSizes", size), new XAttribute("Alpha", true)));
        }
        // A gauge that starts with a moving part still needs its background (the canvas colour).
        if (segments.Count > 0 && (segments[0].Needle || segments[0].Card is not null))
        {
            Draw(json, leaves, [], gaugeBackground: true, baseDir, Path.Combine(art, "background.png"));
            xml.Add(new XElement("Image", new XAttribute("Name", "background.png"), new XAttribute("ImageSizes", size)));
            stills.Add((true, []));
            drawn.Add((false, stills.Count - 1));
        }
        for (var s = 0; s < segments.Count; s++)
        {
            var (isNeedle, card, keep) = segments[s];
            if (card is not null && Slides(card) && Around(json["layers"]!.AsArray(), card)!.Any(g => g["rotate"] is JsonObject))
            {
                // An attitude horizon: one picture that slides and turns, then covers that put back what was
                // under it outside its round window (SPAD.neXt has no clip; its own attitude gauge does the same).
                xml.Add(HorizonElement(gauge, json, leaves, keep, card, ++horizonCount, baseDir, art,
                    [.. sameHorizon.Where(p => ReferenceEquals(p.Value, card)).Select(p => p.Key)]));
                // No window around it (an HSI's CDI bar): nothing to cover.
                if (!Around(json["layers"]!.AsArray(), card)!.Append(card).Any(g => g["clip"] is not null || g["clipBox"] is not null)) continue;
                using var window = Window(json, card, baseDir);
                foreach (var (isCard, index) in drawn)
                    xml.Add(isCard
                        ? CardElement(gauge, json, leaves, cards[index], ++cardCount, baseDir, art, window)
                        : Cover(stills[index].Keep, stills[index].Background, window));
                continue;
            }
            if (card is not null && Slides(card))
            {
                xml.Add(SlideElement(gauge, json, leaves, keep, card, ++slideCount, baseDir, art));
                continue;
            }
            if (card is not null)
            {
                xml.Add(CardElement(gauge, json, leaves, keep, ++cardCount, baseDir, art));
                cards.Add(keep);
                drawn.Add((true, cards.Count - 1));
                continue;
            }
            if (s == 0 && !isNeedle)
            {
                Draw(json, leaves, keep, gaugeBackground: true, baseDir, Path.Combine(art, "background.png"));
                xml.Add(new XElement("Image", new XAttribute("Name", "background.png"), new XAttribute("ImageSizes", size)));
                stills.Add((true, keep));
                drawn.Add((false, stills.Count - 1));
                continue;
            }
            if (!isNeedle)
            {
                xml.Add(Cover(keep, false, null));
                stills.Add((false, keep));
                drawn.Add((false, stills.Count - 1));
                continue;
            }
            if (leafLayers[keep[0]] is ArcLayer arc)
            {
                xml.Add(ArcElement(gauge, json, leaves, keep[0], arc, ++arcCount, baseDir, art));
                continue;
            }
            if (leafLayers[keep[0]] is TextLayer number)
            {
                var hiding = (Around(json["layers"]!.AsArray(), leaves[keep[0]]) ?? []).Where(Slides).Select(g => HideRpn(gauge, g)).ToList();
                var hidden = hiding.Count == 0 ? null : string.Join(" ", hiding) + string.Concat(Enumerable.Repeat(" max", hiding.Count - 1));
                foreach (var element in NumberElements(gauge, json, leaves[keep[0]], number, ++numberCount, baseDir, art, hidden)) xml.Add(element);
                continue;
            }
            xml.Add(NeedleElement(gauge, json, leaves, keep[0], ++needleCount, baseDir, art));
        }

        // Knobs: SPAD.neXt reads a gauge's knobs from <Mouse> blocks (its gauge.xsd: Mouse > Area > Area with a
        // DownArrow or UpArrow cursor and a Click event), the old FS gauge way its own bundled Saitek gauges use:
        // one block per knob, the right knob first, DownArrow = turning anticlockwise. Our first try, Button
        // lines with FIP_RIGHTDIAL_..., did nothing on the owner's FIP. S buttons still go in as Button lines
        // (untested). A SPAD.neXt profile's own button settings win over these. SPAD sends one event per knob
        // click, so a faster knob (repeat) moves one step there. Knob modes and page flipping are Live only.
        var leftOut = new List<string>();
        var buttons = new XElement("Buttons");
        var slower = false;
        // Knobs that change a plane's own value (L:) are FIPinator Live only for now.
        bool Plain(ControlSpec spec) => !spec.ChangesValue && spec.Data == 0 && !spec.Event.StartsWith('#');
        string What(ControlSpec spec) => spec.ChangesValue ? $"changes {spec.Set}" : spec.Event;
        foreach (var (control, spec) in gauge.Controls.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            if (KnobControls.Contains(control))
            {
                if (Plain(spec)) slower |= spec.Repeat > 1;
                else leftOut.Add($"{control} ({What(spec)})");
                continue;
            }
            if (control.StartsWith("button", StringComparison.Ordinal) && int.TryParse(control[6..], out var n) && n is >= 1 and <= 6 && Plain(spec))
                buttons.Add(new XElement("Button", new XAttribute("BoundTo", $"FIP_BUTTON_S{n}"), new XAttribute("Event", spec.Event)));
            else leftOut.Add($"{control} ({What(spec)})");
        }
        var knobs = new List<XElement>();
        (string Down, string Up)? Knob(string side)
        {
            var up = gauge.Controls.GetValueOrDefault(side + "KnobClockwise");
            var down = gauge.Controls.GetValueOrDefault(side + "KnobAnticlockwise");
            return up is not null && down is not null && Plain(up) && Plain(down) ? (down.Event, up.Event) : null;
        }
        var right = Knob("right");
        var left = Knob("left");
        // The right knob's block comes first; a gauge with only a left knob gives it to both.
        foreach (var knob in new[] { right ?? left, right is null ? null : left })
        {
            if (knob is not { } k) continue;
            knobs.Add(new XElement("Mouse", new XElement("Area",
                new XElement("Area", new XElement("Cursor", new XAttribute("Type", "DownArrow")), new XElement("Click", new XAttribute("Event", k.Down), new XAttribute("Repeat", "Yes"))),
                new XElement("Area", new XElement("Cursor", new XAttribute("Type", "UpArrow")), new XElement("Click", new XAttribute("Event", k.Up), new XAttribute("Repeat", "Yes"))))));
        }
        if (slower) leftOut.Add("the faster knob steps (one step per click in SPAD.neXt)");
        if (gauge.Modes.Count > 0) leftOut.Add("knob modes (S buttons choosing what the knobs set)");
        if (gauge.ButtonLights.Count > 0) leftOut.Add("button lights");

        using (var preview = new GaugeRenderer(GaugeDefinition.Parse(json.ToJsonString()), baseDir))
            File.WriteAllBytes(Path.Combine(art, "preview.png"), preview.RenderPng());
        var settings = new XElement("GaugeSettings",
            new XElement("Author", string.IsNullOrWhiteSpace(gauge.Author) ? "FIPinator 3000" : gauge.Author),
            new XElement("Description", ((gauge.Description ?? "") + " Made with FIPinator 3000." + (gauge.License is { Length: > 0 } license ? " " + license : "")).Trim()),
            new XElement("Thumbnail", "preview.png"),
            new XElement("Category", category is null ? "FIPinator" : $"FIPinator: {category}"),
            new XElement("Simulation", "MSFS"));
        if (buttons.HasElements) settings.Add(buttons);
        foreach (var knob in knobs) xml.Add(knob);
        xml.Add(settings);

        var xmlFile = Path.Combine(folder, safe + ".xml");
        using (var writer = System.Xml.XmlWriter.Create(xmlFile, new System.Xml.XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
            new XDocument(new XDeclaration("1.0", "utf-8", null), xml).Save(writer);
        File.WriteAllText(Path.Combine(folder, Marker), ReadMe(gauge, leftOut), Encoding.UTF8);

        // Pack it; write to a temporary name first so a failure never leaves half a zip behind.
        var temp = zipFile + ".tmp";
        File.Delete(temp);
        System.IO.Compression.ZipFile.CreateFromDirectory(folder, temp);
        File.Move(temp, zipFile, overwrite: true);
        return new Result(zipFile, safe + ".xml", leftOut);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    /// <summary>
    /// Documents\FIPinator 3000\FIPinator SPAD Gauges (FREE): the one folder users add in SPAD.neXt
    /// ("Add location"). SPAD also reads its subfolders: "My gauges" (the user's exports) and later
    /// "FIPinator" (the ready-made ones the installer puts there).
    /// </summary>
    public static string GaugesFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FIPinator 3000", "FIPinator SPAD Gauges (FREE)");

    /// <summary>Where FIPinator 3000 saves the SPAD versions of the user's own gauges, by itself.</summary>
    public static string MyGaugesFolder => Path.Combine(GaugesFolder, "My gauges");

    /// <summary>Where the installer puts the SPAD versions of the built-in gauges.</summary>
    public static string ReadyMadeFolder => Path.Combine(GaugesFolder, "FIPinator");

    /// <summary>Our knob controls: they go into the SPAD gauge as Mouse blocks (see Export).</summary>
    internal static readonly HashSet<string> KnobControls = new(StringComparer.Ordinal)
    {
        "rightKnobClockwise", "rightKnobAnticlockwise", "leftKnobClockwise", "leftKnobAnticlockwise",
    };

    /// <summary>The official SPAD.neXt help page for the FIPs (for setting up knobs and buttons there).</summary>
    public const string SpadFipHelp = "https://docs.spadnext.com/hardware-specific/saitek-fip/";

    /// <summary>How the export works and how to improve it, for people who fork FIPinator (docs/spad-export.md).</summary>
    public const string DeveloperGuide = "https://github.com/Pribilof-Hangar/fipinator-live/blob/main/docs/spad-export.md";

    /// <summary>The "FIPinator gauges for SPAD.neXt" download: what's ported, what's tested, known issues.</summary>
    public const string GaugesRepo = "https://github.com/Pribilof-Hangar/fipinator-spad-gauges";

    /// <summary>"Using SPAD.neXt and FIPinator Live together" (the guide is also installed with FIPinator 3000).</summary>
    public const string TogetherGuide = "https://github.com/Pribilof-Hangar/fipinator-live/blob/main/docs/guides/spad-and-live.md";

    /// <summary>
    /// SPAD.neXt is on this PC (its folders in AppData or Documents, or it's running). Only then do
    /// FIPinator's apps mention SPAD at all.
    /// </summary>
    public static bool SpadInstalled()
    {
        try
        {
            if (Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SPAD.neXt"))) return true;
            if (Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SPAD.neXt"))) return true;
            var running = System.Diagnostics.Process.GetProcessesByName("SPAD.neXt");
            foreach (var p in running) p.Dispose();
            return running.Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { return false; }
    }

    /// <summary>
    /// Keeps the SPAD version of a gauge up to date in <paramref name="parent"/>, quietly: saves it when the
    /// gauge can go to SPAD, removes our old copy when it no longer can. Never throws (problems are logged),
    /// so saving a gauge never fails because of SPAD. Returns true if a SPAD version was saved.
    /// </summary>
    public static bool SaveQuietly(string gaugePath, string parent, string? category = null)
    {
        // One at a time: saving a gauge and opening it both ask for its SPAD version, and two saves at once
        // would trip over each other's temporary file.
        lock (SaveGate) return SaveQuietlyNow(gaugePath, parent, category);
    }

    private static readonly Lock SaveGate = new();

    private static bool SaveQuietlyNow(string gaugePath, string parent, string? category)
    {
        try
        {
            var gauge = GaugeDefinition.Load(gaugePath);
            if (Check(gauge).Count == 0)
            {
                Export(gaugePath, parent, category);
                return true;
            }
            var old = Path.Combine(parent, SafeName("FIPinator - " + gauge.Name) + ".zip");
            if (File.Exists(old) && IsOurs(old)) File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException
                                   or System.Text.Json.JsonException or KeyNotFoundException or ArgumentException)
        {
            Log.Warn($"SPAD version of {gaugePath} not saved: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// The SPAD versions of every built-in gauge under <paramref name="gaugesRoot"/>, in matching subfolders of
    /// <see cref="ReadyMadeFolder"/> (e.g. "Classic single", "DC-6"). Our old copies that can't be
    /// made any more are removed; nothing else in the folder is touched. Returns how many were saved.
    /// </summary>
    public static int MakeReadyMade(string gaugesRoot, string? target = null)
    {
        target ??= ReadyMadeFolder;
        var saved = 0;
        var made = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(gaugesRoot, "*.json", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(gaugesRoot, Path.GetDirectoryName(file)!);
            if (relative.Split(Path.DirectorySeparatorChar).Contains("parts")) continue; // building blocks, not gauges
            // FIPinator 3000 installs the DC-6 gauges at the top of its Templates folder (and its "engines",
            // "mini"... subfolders): name those like the repo's dc6 folder.
            var first = relative.Split(Path.DirectorySeparatorChar)[0].ToLowerInvariant();
            if (first is not ("classic" or "turboprop" or "jet" or "templates" or "dc6"))
                relative = relative == "." ? "dc6" : Path.Combine("dc6", relative);
            var group = GaugeCatalog.GroupName(relative);
            var folder = Path.Combine(target, SafeName(group));
            if (SaveQuietly(file, folder, group))
            {
                saved++;
                made.Add(Path.GetFullPath(Path.Combine(folder, SafeName("FIPinator - " + GaugeDefinition.Load(file).Name) + ".zip")));
            }
        }
        // Our copies of gauges that are gone from the set (e.g. the old Classic four-engine group): removed, with
        // their emptied group folders. Only our own zips, only in the ready-made folder.
        try
        {
            if (Directory.Exists(target))
            {
                foreach (var zip in Directory.EnumerateFiles(target, "*.zip", SearchOption.AllDirectories).ToList())
                    if (!made.Contains(Path.GetFullPath(zip)) && IsOurs(zip)) File.Delete(zip);
                foreach (var dir in Directory.EnumerateDirectories(target).ToList())
                {
                    // An empty folder can be held open (OneDrive, or SPAD.neXt watching it): harmless, try the rest.
                    try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Info($"Empty folder {dir} left in place: {ex.Message}"); }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Old SPAD copies in {target} not tidied: {ex.Message}");
        }
        return saved;
    }

    /// <summary>Plain words for the gauge maker: "Works in SPAD.neXt too" or what keeps it Live-only.</summary>
    public static (bool Ready, string Text) Status(GaugeDefinition gauge) =>
        Check(gauge) is { Count: > 0 } problems
            ? (false, $"SPAD.neXt: not yet. This gauge has {string.Join("; ", problems)}. It works in FIPinator Live.")
            : (true, "SPAD.neXt: works there too (Export for SPAD.neXt).");

    /// <summary>The readme inside every export; it also marks a .zip as one of ours (safe to replace).</summary>
    public const string Marker = "fipinator-export.txt";

    /// <summary>The folder inside the .zip that SPAD.neXt reads the pictures from.</summary>
    public const string ArtFolder = "1024";

    private static bool IsOurs(string zipFile)
    {
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(zipFile);
            return zip.GetEntry(Marker) is not null;
        }
        catch (InvalidDataException) { return false; }
    }

    /// <summary>One needle: its own picture pointing right (east), turned by SPAD around the pivot.</summary>
    private static XElement NeedleElement(GaugeDefinition gauge, JsonObject json, List<JsonObject> leaves, int leaf, int number, string baseDir, string folder)
    {
        var node = leaves[leaf];
        var needle = (NeedleLayer)Flatten(gauge.Layers).OfType<NeedleLayer>().ElementAt(number - 1);
        // A hand that goes round: the formula below brings the value back into the dial's range, so the
        // point table is built without wrapping (else the dial's end, 1000 ft, would count as 0).
        var wraps = gauge.Scale(needle.Scale).Wrap;
        var scale = gauge.Scale(needle.Scale) with { Wrap = false };

        // Where the pivot lands on the FIP, through any groups (offset, zoom) around the needle.
        var (zoom, ox, oy) = Placement(json["layers"]!.AsArray(), node) ?? (1f, 0f, 0f);
        var local = needle.Center ?? gauge.Center;
        var pivot = new SKPoint(ox + local[0] * zoom, oy + local[1] * zoom);

        // Draw the needle alone, pointing right, then cut the picture to the needle.
        var originalScale = node["scale"]!.GetValue<string>();
        var originalValue = node["value"]!.GetValue<string>();
        node["scale"] = EastScale;
        node["value"] = EastValue;
        // A bent scale (DC-6 engine dials): the needle's reach changes along the dial in Live. SPAD.neXt
        // only turns a picture, so the needle gets one length: its average reach along the scale line.
        var originalLength = node["length"]?.DeepClone();
        var bentReach = gauge.Scale(needle.Scale) is { IsBent: true } bent ? (float)(bent.Points!.Average(p => p[2]) + needle.Length) : (float?)null;
        if (bentReach is { } r) node["length"] = r;
        var copy = json.DeepClone().AsObject();
        if (bentReach is not null) node["length"] = originalLength;
        node["scale"] = originalScale;
        node["value"] = originalValue;
        copy["scales"]!.AsObject()[EastScale] = new JsonObject { ["min"] = 0, ["max"] = 1, ["startAngle"] = 90, ["endAngle"] = 90 };
        copy["values"]!.AsObject()[EastValue] = new JsonObject { ["default"] = 0 };
        using var full = RenderOnly(copy, leaves.Count, [leaf], gaugeBackground: false, baseDir);
        var bounds = Visible(full);
        var name = $"needle{number}.png";
        using (var cut = new SKBitmap(bounds.Width, bounds.Height))
        {
            full.ExtractSubset(cut, bounds);
            Save(cut, Path.Combine(folder, name));
        }
        var axis = new SKPoint(pivot.X - bounds.Left, pivot.Y - bounds.Top);

        // Value -> where the needle points. Points on a circle around the pivot (FSX gauge style).
        var reach = Math.Max(bentReach ?? needle.Length, 40) * zoom;
        // Many points, at most 10 degrees apart, so the needle follows the dial however SPAD fills in between.
        var corners = scale.Points is { Length: > 0 } points ? points.Select(p => p[0]).ToList() : [scale.Min, scale.Max];
        var steps = new List<double> { corners[0] };
        for (var i = 1; i < corners.Count; i++)
        {
            var pieces = Math.Max(1, (int)Math.Ceiling(Math.Abs(scale.AngleFor(corners[i]) - scale.AngleFor(corners[i - 1])) / 10));
            for (var k = 1; k <= pieces; k++) steps.Add(corners[i - 1] + (corners[i] - corners[i - 1]) * k / pieces);
        }
        var nonlinearity = new XElement("Nonlinearity");
        foreach (var v in steps)
        {
            var radians = scale.AngleFor(v) * Math.PI / 180;
            nonlinearity.Add(new XElement("Item",
                new XAttribute("Value", Num(v)),
                new XAttribute("X", Num(pivot.X + reach * Math.Sin(radians))),
                new XAttribute("Y", Num(pivot.Y - reach * Math.Cos(radians)))));
        }

        return new XElement("Element", new XAttribute("id", $"needle{number}"),
            new XElement("Position", new XAttribute("X", Num(pivot.X)), new XAttribute("Y", Num(pivot.Y))),
            new XElement("Image", new XAttribute("Name", name), new XAttribute("ImageSizes", $"{bounds.Width},{bounds.Height},{bounds.Width},{bounds.Height}"),
                new XAttribute("Alpha", true), new XAttribute("PointsTo", "East"),
                new XElement("Axis", new XAttribute("X", Num(axis.X)), new XAttribute("Y", Num(axis.Y)))),
            new XElement("Rotate",
                new XElement("Value", new XAttribute("Minimum", Num(scale.Low)), new XAttribute("Maximum", Num(scale.High)),
                    wraps ? Wrapped(ValueRpn(gauge, needle.Value)!, scale.Low, scale.High - scale.Low) : ValueRpn(gauge, needle.Value)),
                nonlinearity));
    }

    /// <summary>
    /// A turning card: drawn upright on its own (cut to size), and turned by SPAD.neXt around the card's centre,
    /// like a needle. A card on a card (heading bug) turns by both angles added together.
    /// </summary>
    private static XElement CardElement(GaugeDefinition gauge, JsonObject json, List<JsonObject> leaves, List<int> keep, int number, string baseDir, string art, SKBitmap? window = null)
    {
        var chain = Around(json["layers"]!.AsArray(), leaves[keep[0]])!.Where(g => g["rotate"] is JsonObject).ToList();
        var outer = chain[0];

        // The centre it turns around, on the FIP: through the groups around the card, then the card's own place.
        var (zoom, ox, oy) = Placement(json["layers"]!.AsArray(), outer) ?? (1f, 0f, 0f);
        var gz = outer["zoom"]?.GetValue<float>() is { } z and > 0 ? z : 1;
        var off = outer["offset"]?.AsArray() is { Count: 2 } o ? (o[0]!.GetValue<float>(), o[1]!.GetValue<float>()) : (0f, 0f);
        var c = outer["center"]?.AsArray() is { Count: 2 } cc ? (cc[0]!.GetValue<float>(), cc[1]!.GetValue<float>()) : (gauge.Center[0], gauge.Center[1]);
        var pivot = new SKPoint(ox + zoom * (off.Item1 + gz * c.Item1), oy + zoom * (off.Item2 + gz * c.Item2));

        // The angle, in degrees clockwise: each card's scale is a straight line from value to angle.
        var parts = new List<string>();
        foreach (var g in chain)
        {
            var link = g["rotate"]!.AsObject();
            var scale = gauge.Scale(link["scale"]!.GetValue<string>()) with { Wrap = false };
            if (scale.Points is { Length: > 0 }) continue; // uneven: a table of points instead (below)
            var (a0, a1) = (scale.AngleFor(scale.Min), scale.AngleFor(scale.Max));
            var perUnit = (a1 - a0) / (scale.Max - scale.Min);
            parts.Add($"{ValueRpn(gauge, link["value"]!.GetValue<string>())} {Num(scale.Min)} - {Num(perUnit)} * {Num(a0)} +");
        }
        var angle = string.Join(" ", parts) + string.Concat(Enumerable.Repeat(" +", Math.Max(0, parts.Count - 1)));

        // Draw the card upright: the same gauge with every turning part at rest, only this card's layers kept.
        var copy = json.DeepClone().AsObject();
        StopTurning(copy["layers"]!.AsArray());
        using var full = RenderOnly(copy, leaves.Count, keep, gaugeBackground: false, baseDir);
        // A copy over a horizon: the round window is cut out (it's round its turning centre, so it fits any turn).
        if (window is not null) Punch(full, window);
        var bounds = Visible(full);
        var name = $"card{number}.png";
        using (var cut = new SKBitmap(bounds.Width, bounds.Height))
        {
            full.ExtractSubset(cut, bounds);
            Save(cut, Path.Combine(art, name));
        }
        var axis = new SKPoint(pivot.X - bounds.Left, pivot.Y - bounds.Top);

        // Like a needle drawn pointing right: at angle a the picture's right-hand side points at a + 90 degrees.
        const float reach = 100;
        XElement Item(double value, double a)
        {
            var radians = (a + 90) * Math.PI / 180;
            return new XElement("Item", new XAttribute("Value", Num(value)),
                new XAttribute("X", Num(pivot.X + reach * Math.Sin(radians))),
                new XAttribute("Y", Num(pivot.Y - reach * Math.Cos(radians))));
        }
        var nonlinearity = new XElement("Nonlinearity");
        XElement rotateValue;
        var only = gauge.Scale(chain[0]["rotate"]!["scale"]!.GetValue<string>()) with { Wrap = false };
        if (chain.Count == 1 && only.Points is { Length: > 0 } points)
        {
            // An uneven scale (a radar altimeter's DH bug): the sim value itself, with the scale's points at most
            // 10 degrees apart, so the card follows the dial however SPAD fills in between.
            var steps = new List<double> { points[0][0] };
            for (var i = 1; i < points.Length; i++)
            {
                var pieces = Math.Max(1, (int)Math.Ceiling(Math.Abs(points[i][1] - points[i - 1][1]) / 10));
                for (var k = 1; k <= pieces; k++) steps.Add(points[i - 1][0] + (points[i][0] - points[i - 1][0]) * k / pieces);
            }
            foreach (var v in steps) nonlinearity.Add(Item(v, only.AngleFor(v)));
            rotateValue = new XElement("Value", new XAttribute("Minimum", Num(only.Low)), new XAttribute("Maximum", Num(only.High)),
                ValueRpn(gauge, chain[0]["rotate"]!["value"]!.GetValue<string>()));
        }
        else
        {
            for (var a = 0; a <= 360; a += 10) nonlinearity.Add(Item(a, a));
            rotateValue = new XElement("Value", new XAttribute("Minimum", 0), new XAttribute("Maximum", 360), Wrapped(angle, 0, 360));
        }
        return new XElement("Element", new XAttribute("id", $"card{number}"),
            new XElement("Position", new XAttribute("X", Num(pivot.X)), new XAttribute("Y", Num(pivot.Y))),
            new XElement("Image", new XAttribute("Name", name), new XAttribute("ImageSizes", $"{bounds.Width},{bounds.Height},{bounds.Width},{bounds.Height}"),
                new XAttribute("Alpha", true), new XAttribute("PointsTo", "East"),
                new XElement("Axis", new XAttribute("X", Num(axis.X)), new XAttribute("Y", Num(axis.Y)))),
            new XElement("Rotate", rotateValue, nonlinearity));
    }

    private static bool Slides(JsonObject group) => group["moveX"] is JsonObject || group["moveY"] is JsonObject;

    private static bool Moves(JsonObject group) => group["rotate"] is JsonObject || Slides(group);

    /// <summary>
    /// A sliding part (an ILS bar, a flag that slides out of sight): drawn at every position it can reach, about
    /// 2 pixels apart, each picture already cut to the gauge's window, and SPAD.neXt picks one by value (its
    /// documented Dynamic image, like growing arcs). SPAD.neXt's own Shift + MaskImage was tried first: SPAD
    /// drew the mask pictures as white circles over the gauge (owner's FIP, 2026-11-11).
    /// </summary>
    private static XElement SlideElement(GaugeDefinition gauge, JsonObject json, List<JsonObject> leaves, List<int> keep, JsonObject slide, int number, string baseDir, string art)
    {
        var layers = json["layers"]!.AsArray();
        var chain = Around(layers, slide)!.Append(slide).Where(Slides).ToList();
        // What moves it (one or two slides: a localizer pointer moves sideways and up) and what hides it (around it,
        // or parts inside it), each as (group, link, axis).
        var movers = new List<(JsonObject Group, string Axis)>();
        foreach (var g in chain.Where(g => !HidesJson(gauge, g)))
            foreach (var axis in new[] { "moveX", "moveY" })
                if (g[axis] is JsonObject) movers.Add((g, axis));
        var hiders = chain.Where(g => HidesJson(gauge, g)).Concat(Descendants(slide).Where(g => Slides(g) && HidesJson(gauge, g))).ToList();
        var hides = hiders.Select(g => HideRpn(gauge, g)).ToList();

        // The values each mover really takes, the pixels it travels, and how many steps to draw.
        var stepPx = movers.Count > 1 ? 3.0 : 2.0;
        var axes = new List<(JsonObject Link, string ValueName, double Lo, double Hi, int Count)>();
        foreach (var (g, axis) in movers)
        {
            var link = g[axis]!.AsObject();
            var valueName = link["value"]!.GetValue<string>();
            var scale = gauge.Scale(link["scale"]!.GetValue<string>()) with { Wrap = false };
            var (lowest, highest) = Span(gauge, valueName);
            var (lo, hi) = (Math.Max(Math.Min(lowest, highest), scale.Min), Math.Min(Math.Max(lowest, highest), scale.Max));
            if (hi < lo) (lo, hi) = (scale.Min, scale.Max);
            var (zoom, _, _) = Placement(layers, g) ?? (1f, 0f, 0f);
            if (g["zoom"]?.GetValue<float>() is { } own and > 0) zoom *= own;
            var travel = Math.Abs(scale.AngleFor(hi) - scale.AngleFor(lo)) * zoom;
            var cap = movers.Count > 1 ? 40 : MaxSlideSteps;
            axes.Add((link, valueName, lo, hi, hi > lo ? Math.Clamp((int)Math.Ceiling(travel / stepPx) + 1, 2, cap) : 1));
        }
        var count = axes.Aggregate(1, (n, a) => n * a.Count);

        // While drawing: hiding parts at rest (shown), each mover pointed at a fixed value of its own.
        var saved = new List<(JsonObject Node, string Key, JsonNode? Value)>();
        foreach (var g in hiders)
            foreach (var key in new[] { "moveX", "moveY" })
                if (g[key] is { } v) { saved.Add((g, key, v.DeepClone())); g.Remove(key); }
        for (var i = 0; i < axes.Count; i++) axes[i].Link["value"] = SlideValue + i;
        var pictures = new List<SKBitmap>();
        try
        {
            for (var k = 0; k < count; k++)
            {
                var copy = json.DeepClone().AsObject();
                var rest = k;
                for (var i = 0; i < axes.Count; i++)
                {
                    var a = axes[i];
                    var step = rest % a.Count;
                    rest /= a.Count;
                    copy["values"]!.AsObject()[SlideValue + i] = new JsonObject { ["default"] = a.Count == 1 ? a.Lo : a.Lo + (a.Hi - a.Lo) * step / (a.Count - 1) };
                }
                pictures.Add(RenderOnly(copy, leaves.Count, keep, gaugeBackground: false, baseDir));
            }
        }
        finally
        {
            for (var i = 0; i < axes.Count; i++) axes[i].Link["value"] = axes[i].ValueName;
            foreach (var (node, key, value) in saved) node[key] = value;
        }
        try
        {
            // One box for all of them: around everything any step shows.
            int left = int.MaxValue, top = int.MaxValue, right = 0, bottom = 0;
            foreach (var picture in pictures)
            {
                var b = Visible(picture);
                if (b.Width <= 1 && b.Height <= 1) continue; // nothing shows (slid out of sight)
                (left, top, right, bottom) = (Math.Min(left, b.Left), Math.Min(top, b.Top), Math.Max(right, b.Right), Math.Max(bottom, b.Bottom));
            }
            if (left == int.MaxValue) (left, top, right, bottom) = (0, 0, 1, 1);
            var box = new SKRectI(left, top, right, bottom);
            var prefix = $"slide{number}_";
            for (var k = 0; k < count; k++)
            {
                using var cut = new SKBitmap(box.Width, box.Height);
                cut.Erase(SKColors.Transparent);
                using (var canvas = new SKCanvas(cut))
                using (var image = SKImage.FromBitmap(pictures[k]))
                    canvas.DrawImage(image, -box.Left, -box.Top, new SKSamplingOptions(SKFilterMode.Nearest));
                Save(cut, Path.Combine(art, $"{prefix}{k}.png"));
            }
            // The picture number: each mover's step, the first counting fastest.
            var parts = new List<string>();
            var stride = 1;
            foreach (var a in axes)
            {
                if (a.Count > 1)
                {
                    var last1 = a.Count - 1;
                    var index = $"{ValueRpn(gauge, a.ValueName)} {Num(a.Lo)} - {Num(a.Hi - a.Lo)} / {last1} * 0.5 + flr 0 max {last1} min";
                    parts.Add(stride == 1 ? index : $"{index} {stride} *");
                }
                stride *= a.Count;
            }
            var expression = parts.Count == 0 ? "0" : string.Join(" ", parts) + string.Concat(Enumerable.Repeat(" +", parts.Count - 1));
            var last = count - 1;
            if (hides.Count > 0)
            {
                // Picture "count": blank, for when the part (or a part inside it) is out of sight.
                using (var blank = new SKBitmap(box.Width, box.Height))
                {
                    blank.Erase(SKColors.Transparent);
                    Save(blank, Path.Combine(art, $"{prefix}{count}.png"));
                }
                var anyHidden = string.Join(" ", hides) + string.Concat(Enumerable.Repeat(" max", hides.Count - 1));
                expression = $"{anyHidden} if{{ {count} }} els{{ {expression} }}";
                last = count;
            }
            return new XElement("Element", new XAttribute("id", $"slide{number}"),
                new XElement("Position", new XAttribute("X", box.Left), new XAttribute("Y", box.Top)),
                new XElement("Image", new XAttribute("Name", $"{prefix}0.png"), new XAttribute("Dynamic", $"{prefix}{{VALUE}}.png"),
                    new XAttribute("ImageSizes", $"{box.Width},{box.Height},{box.Width},{box.Height}"), new XAttribute("Alpha", true),
                    new XElement("Value", new XAttribute("Minimum", 0), new XAttribute("Maximum", last), expression)));
        }
        finally
        {
            foreach (var picture in pictures) picture.Dispose();
        }
    }

    /// <summary>
    /// Sliding parts on turning parts that move exactly alike (same turning groups around them, same slide value,
    /// scale and direction), each mapped to the first of them in drawing order.
    /// </summary>
    private static Dictionary<JsonObject, JsonObject> SameHorizonParts(JsonArray layers)
    {
        var map = new Dictionary<JsonObject, JsonObject>(ReferenceEqualityComparer.Instance);
        var first = new Dictionary<string, JsonObject>();
        void Walk(JsonArray list, List<JsonObject> around)
        {
            foreach (var g in list.OfType<JsonObject>().Where(l => l["type"]?.GetValue<string>() == "group"))
            {
                var turns = around.Where(a => a["rotate"] is JsonObject).ToList();
                if (Slides(g) && turns.Count > 0 && g["rotate"] is null)
                {
                    var axis = g["moveX"] is JsonObject ? "moveX" : "moveY";
                    var link = g[axis]!.AsObject();
                    var key = string.Join(",", turns.Select(t => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(t)))
                              + $"|{axis}|{link["value"]}|{link["scale"]}";
                    if (!first.TryGetValue(key, out var owner)) first[key] = owner = g;
                    map[g] = owner;
                }
                Walk(g["layers"]?.AsArray() ?? [], [.. around, g]);
            }
        }
        Walk(layers, []);
        return map;
    }

    /// <summary>A part (as a gauge file node) that slides only to hide: see <see cref="Hides"/>.</summary>
    private static bool HidesJson(GaugeDefinition gauge, JsonObject g)
    {
        if (g["moveX"] is JsonObject && g["moveY"] is JsonObject) return false;
        var across = g["moveX"] is JsonObject;
        var link = (across ? g["moveX"] : g["moveY"])?.AsObject();
        if (link is null) return false;
        var value = link["value"]!.GetValue<string>();
        if (!gauge.Scales.TryGetValue(link["scale"]!.GetValue<string>(), out var scale) || scale.Points is { Length: > 0 } || ValueRpn(gauge, value) is null) return false;
        return HideShifts(gauge, value, scale, across);
    }

    /// <summary>RPN that is 1 while a hiding part is pushed out of sight (its slide isn't 0), else 0.</summary>
    private static string HideRpn(GaugeDefinition gauge, JsonObject g)
    {
        var link = (g["moveX"] ?? g["moveY"])!.AsObject();
        var scale = gauge.Scale(link["scale"]!.GetValue<string>()) with { Wrap = false };
        var (a0, a1) = (scale.AngleFor(scale.Min), scale.AngleFor(scale.Max));
        var px = $"{ValueRpn(gauge, link["value"]!.GetValue<string>())} {Num(scale.Min)} - {Num((a1 - a0) / (scale.Max - scale.Min))} * {Num(a0)} +";
        return $"{px} abs 1 >";
    }

    private static IEnumerable<JsonObject> Descendants(JsonObject group)
    {
        foreach (var layer in (group["layers"]?.AsArray() ?? []).OfType<JsonObject>())
        {
            if (layer["type"]?.GetValue<string>() != "group") continue;
            yield return layer;
            foreach (var inner in Descendants(layer)) yield return inner;
        }
    }

    /// <summary>The most pictures one sliding part is drawn in (enough for 2-pixel steps across the FIP).</summary>
    public const int MaxSlideSteps = 161;

    private const string SlideValue = "__spad_slide";

    /// <summary>
    /// An attitude horizon: the sliding part drawn once, uncut and level, on a big sheet (it slides a long way),
    /// slid by SPAD.neXt (Shift: value x Scale pixels) and turned around the card's centre (Rotate, in radians),
    /// both in one element, the way SPAD.neXt's bundled Saitek attitude gauge does it.
    /// </summary>
    private static XElement HorizonElement(GaugeDefinition gauge, JsonObject json, List<JsonObject> leaves, List<int> keep, JsonObject slide, int number, string baseDir, string art,
        List<JsonObject>? alike = null)
    {
        var layers = json["layers"]!.AsArray();
        var turns = Around(layers, slide)!.Where(g => g["rotate"] is JsonObject).ToList();
        var turning = turns[0]; // the outermost turn: its centre (the others turn round the same one)

        // The turning centre on the FIP (as for a card).
        var (tz, tx, ty) = Placement(layers, turning) ?? (1f, 0f, 0f);
        var gz = turning["zoom"]?.GetValue<float>() is { } z and > 0 ? z : 1;
        var off = turning["offset"]?.AsArray() is { Count: 2 } o ? (o[0]!.GetValue<float>(), o[1]!.GetValue<float>()) : (0f, 0f);
        var c = turning["center"]?.AsArray() is { Count: 2 } cc ? (cc[0]!.GetValue<float>(), cc[1]!.GetValue<float>()) : (gauge.Center[0], gauge.Center[1]);
        var pivot = new SKPoint(tx + tz * (off.Item1 + gz * c.Item1), ty + tz * (off.Item2 + gz * c.Item2));

        // Bank (or course minus heading): each turn's angle in degrees clockwise, added up, as radians (SPAD.neXt turns
        // a positive value clockwise: banks matched the Asobo 737 on a FIP, 2026-11-11).
        var angles = turns.Select(t =>
        {
            var turn = t["rotate"]!.AsObject();
            var turnScale = gauge.Scale(turn["scale"]!.GetValue<string>()) with { Wrap = false };
            var (t0, t1) = (turnScale.AngleFor(turnScale.Min), turnScale.AngleFor(turnScale.Max));
            return $"{ValueRpn(gauge, turn["value"]!.GetValue<string>())} {Num(turnScale.Min)} - {Num((t1 - t0) / (turnScale.Max - turnScale.Min))} * {Num(t0)} +";
        }).ToList();
        var degrees = string.Join(" ", angles) + string.Concat(Enumerable.Repeat(" +", angles.Count - 1));
        var radians = $"{degrees} {Math.PI / 180:0.0000000} *";

        // Pitch: pixels of slide, times the zoom of everything around it.
        var across = slide["moveX"] is JsonObject;
        var link = (across ? slide["moveX"] : slide["moveY"])!.AsObject();
        var scale = gauge.Scale(link["scale"]!.GetValue<string>()) with { Wrap = false };
        var (a0, a1) = (scale.AngleFor(scale.Min), scale.AngleFor(scale.Max));
        var shift = $"{ValueRpn(gauge, link["value"]!.GetValue<string>())} {Num(scale.Min)} - {Num((a1 - a0) / (scale.Max - scale.Min))} * {Num(a0)} +";
        var (zoom, _, _) = Placement(layers, slide) ?? (1f, 0f, 0f);
        if (slide["zoom"]?.GetValue<float>() is { } own and > 0) zoom *= own;

        // The picture: level, at rest, no windows, on a sheet five times the FIP's size (the FIP in the middle), big enough to fill the window at steep pitch and bank.
        var (w, h) = (gauge.Canvas.Width, gauge.Canvas.Height);
        var copy = json.DeepClone().AsObject();
        StopTurning(copy["layers"]!.AsArray());
        // Its own windows go (SPAD has no clipping: covers put that back); a part sharing it (the pitch ladder) keeps
        // its window, drawn at rest.
        var ownChain = Chain(copy["layers"]!.AsArray(), PathTo(layers, slide)!);
        foreach (var g in ownChain) { g.Remove("clip"); g.Remove("clipBox"); }
        foreach (var part in (alike ?? []).Append(slide))
        {
            var target = Chain(copy["layers"]!.AsArray(), PathTo(layers, part)!)[^1];
            target.Remove("moveX");
            target.Remove("moveY");
        }
        copy["canvas"]!["width"] = w * 5;
        copy["canvas"]!["height"] = h * 5;
        var inner = copy["layers"]!.AsArray();
        copy["layers"] = new JsonArray(new JsonObject { ["type"] = "group", ["offset"] = new JsonArray(2 * w, 2 * h), ["layers"] = inner.DeepClone() });
        using var full = RenderOnly(copy, leaves.Count, keep, gaugeBackground: false, baseDir);
        var bounds = Visible(full);
        var name = $"horizon{number}.png";
        using (var cut = new SKBitmap(bounds.Width, bounds.Height))
        {
            full.ExtractSubset(cut, bounds);
            Save(cut, Path.Combine(art, name));
        }
        var axis = new SKPoint(pivot.X + 2 * w - bounds.Left, pivot.Y + 2 * h - bounds.Top);

        return new XElement("Element", new XAttribute("id", $"horizon{number}"),
            new XElement("Position", new XAttribute("X", Num(pivot.X)), new XAttribute("Y", Num(pivot.Y))),
            new XElement("Image", new XAttribute("Name", name), new XAttribute("ImageSizes", $"{bounds.Width},{bounds.Height},{bounds.Width},{bounds.Height}"),
                new XAttribute("Alpha", true),
                new XElement("Axis", new XAttribute("X", Num(axis.X)), new XAttribute("Y", Num(axis.Y)))),
new XElement("Shift",
                new XElement("Value", new XAttribute("Minimum", Num(Math.Min(a0, a1))), new XAttribute("Maximum", Num(Math.Max(a0, a1))), shift),
                // SPAD.neXt slides a positive Y upward (seen on a FIP in the Asobo 737, 2026-11-11: pulling up showed
                // more ground), so Y is negated; X slides right as expected (checked with the HSI/ILS bar).
                new XElement("Scale", new XAttribute(across ? "X" : "Y", Num(across ? zoom : -zoom)))),
            new XElement("Rotate", new XElement("Value", radians)));
    }

    /// <summary>
    /// The window a sliding part shows through, on the FIP: white where it may show. Made by drawing a huge white
    /// sheet in the part's place, at rest, with every window around it still cutting.
    /// </summary>
    private static SKBitmap Window(JsonObject json, JsonObject slide, string baseDir)
    {
        var copy = json.DeepClone().AsObject();
        var target = Chain(copy["layers"]!.AsArray(), PathTo(json["layers"]!.AsArray(), slide)!)[^1];
        StopTurning(copy["layers"]!.AsArray());
        target.Remove("moveX");
        target.Remove("moveY");
        var sheet = new JsonObject { ["type"] = "rect", ["x"] = -5000, ["y"] = -5000, ["width"] = 10000, ["height"] = 10000, ["fill"] = "#FFFFFFFF" };
        target["layers"] = new JsonArray(sheet);
        var all = new List<JsonObject>();
        Collect(copy["layers"]!.AsArray(), all);
        return RenderOnly(copy, all.Count, [all.IndexOf(sheet)], gaugeBackground: false, baseDir);
    }

    /// <summary>Makes the picture see-through wherever the window is.</summary>
    private static void Punch(SKBitmap picture, SKBitmap window)
    {
        using var canvas = new SKCanvas(picture);
        using var image = SKImage.FromBitmap(window);
        using var paint = new SKPaint { BlendMode = SKBlendMode.DstOut };
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), paint);
    }

    private static void Unclip(JsonArray layers)
    {
        foreach (var layer in layers.OfType<JsonObject>())
        {
            if (layer["type"]?.GetValue<string>() != "group") continue;
            layer.Remove("clip");
            layer.Remove("clipBox");
            Unclip(layer["layers"]?.AsArray() ?? []);
        }
    }

    /// <summary>Where <paramref name="target"/> sits: the index at each level of groups (null if it isn't there).</summary>
    private static List<int>? PathTo(JsonArray layers, JsonObject target)
    {
        for (var i = 0; i < layers.Count; i++)
        {
            if (layers[i] is not JsonObject layer) continue;
            if (ReferenceEquals(layer, target)) return [i];
            if (layer["type"]?.GetValue<string>() == "group" && PathTo(layer["layers"]?.AsArray() ?? [], target) is { } inner) return [i, .. inner];
        }
        return null;
    }

    /// <summary>The layers along <paramref name="path"/>, outermost first (the last one is the target).</summary>
    private static List<JsonObject> Chain(JsonArray layers, List<int> path)
    {
        var chain = new List<JsonObject>();
        foreach (var i in path)
        {
            var layer = layers[i]!.AsObject();
            chain.Add(layer);
            layers = layer["layers"]?.AsArray() ?? [];
        }
        return chain;
    }

    /// <summary>The groups around <paramref name="target"/>, outermost first (null if it isn't there).</summary>
    private static List<JsonObject>? Around(JsonArray layers, JsonObject target)
    {
        foreach (var layer in layers.OfType<JsonObject>())
        {
            if (ReferenceEquals(layer, target)) return [];
            if (layer["type"]?.GetValue<string>() != "group") continue;
            if (Around(layer["layers"]?.AsArray() ?? [], target) is { } inner) return [layer, .. inner];
        }
        return null;
    }

    private static void StopTurning(JsonArray layers)
    {
        foreach (var layer in layers.OfType<JsonObject>())
        {
            if (layer["type"]?.GetValue<string>() != "group") continue;
            layer.Remove("rotate");
            StopTurning(layer["layers"]?.AsArray() ?? []);
        }
    }

    /// <summary>How many steps a growing arc is drawn in, from empty to full.</summary>
    public const int ArcSteps = 60;

    /// <summary>
    /// A growing arc (the white "pie" sweep on modern engine dials): SPAD.neXt can't bend an arc live, so it's
    /// drawn in <see cref="ArcSteps"/> pictures from empty to full and SPAD picks one by value (its documented
    /// Dynamic image). All pictures share one box, cut to the full arc.
    /// </summary>
    private static XElement ArcElement(GaugeDefinition gauge, JsonObject json, List<JsonObject> leaves, int leaf, ArcLayer arc, int number, string baseDir, string art)
    {
        var scale = gauge.Scale(arc.Scale);
        var (from, full) = (arc.From, scale.High);
        var node = leaves[leaf];
        SKBitmap Step(double to)
        {
            // Change the arc to a fixed end for a moment, copy the gauge, then put the arc back as it was.
            var (oldTo, oldToValue) = (node["to"]?.DeepClone(), node["toValue"]?.DeepClone());
            node.Remove("toValue");
            node["to"] = to;
            var copy = json.DeepClone().AsObject();
            node["to"] = oldTo;
            if (oldToValue is not null) node["toValue"] = oldToValue;
            if (oldTo is null) node.Remove("to");
            return RenderOnly(copy, leaves.Count, [leaf], gaugeBackground: false, baseDir);
        }
        SKRectI box;
        using (var whole = Step(full)) box = Visible(whole);
        var prefix = $"arc{number}_";
        for (var k = 0; k < ArcSteps; k++)
        {
            using var cut = new SKBitmap(box.Width, box.Height);
            cut.Erase(SKColors.Transparent);
            if (k > 0) // step 0: nothing yet
            {
                using var picture = Step(from + (full - from) * k / (ArcSteps - 1));
                using var canvas = new SKCanvas(cut);
                using var image = SKImage.FromBitmap(picture);
                canvas.DrawImage(image, -box.Left, -box.Top, new SKSamplingOptions(SKFilterMode.Nearest));
            }
            Save(cut, Path.Combine(art, $"{prefix}{k}.png"));
        }
        var v = ValueRpn(gauge, arc.ToValue!)!;
        var last = ArcSteps - 1;
        var expression = $"{v} {Num(from)} - {Num(full - from)} / {last} * 0.5 + flr 0 max {last} min";
        return new XElement("Element", new XAttribute("id", $"arc{number}"),
            new XElement("Position", new XAttribute("X", box.Left), new XAttribute("Y", box.Top)),
            new XElement("Image", new XAttribute("Name", $"{prefix}0.png"), new XAttribute("Dynamic", $"{prefix}{{VALUE}}.png"),
                new XAttribute("ImageSizes", $"{box.Width},{box.Height},{box.Width},{box.Height}"), new XAttribute("Alpha", true),
                new XElement("Value", new XAttribute("Minimum", 0), new XAttribute("Maximum", last), expression)));
    }

    /// <summary>
    /// A value that goes round and round (altimeter hands, clocks): brought back into low..low+span with
    /// FSX-style RPN, without the % sign (SPAD's docs warn about it): v - span * flr((v - low) / span).
    /// </summary>
    internal static string Wrapped(string v, double low, double span) =>
        $"{v} {v} {Num(low)} - {Num(span)} / flr {Num(span)} * -";

    /// <summary>
    /// "0", "0.0", "000", "#,0", "#,##0.0"...: (integer digits always shown, decimals, thousands commas);
    /// null for other formats.
    /// </summary>
    internal static (int Whole, int Decimals, bool Commas)? DigitFormat(string format)
    {
        var commas = false;
        if (format.StartsWith("#,", StringComparison.Ordinal))
        {
            // "#,0" / "#,##0": the part before the zeros is only # and commas.
            var zeros = format.IndexOf('0');
            if (zeros < 0 || format[..zeros].Any(c => c is not ('#' or ','))) return null;
            commas = true;
            format = format[zeros..];
        }
        var parts = format.Split('.');
        // ".00" (a Mach number): no whole-number digit, just the decimals.
        if (parts.Length == 2 && parts[0].Length == 0 && !commas && parts[1].Length > 0 && parts[1].All(c => c == '0')) return (0, parts[1].Length, false);
        if (parts.Length > 2 || parts[0].Length == 0 || parts.Any(p => p.Length == 0 || p.Any(c => c != '0'))) return null;
        return (Math.Max(parts[0].Length, 1), parts.Length == 2 ? parts[1].Length : 0, commas);
    }

    /// <summary>
    /// A number format with words or signs around the digits: "0%", "000°", "0 KT", "0 °C;-0 °C",
    /// "+0000;-0000;0000", "+#,0;-#,0;0". Plus/Minus/Zero: what goes in front of the digits for a positive,
    /// negative or zero number (.NET's ";" sections); Suffix: what goes after (the same in every section);
    /// Times: 100 for "%" (.NET shows the value times 100). Null if the export can't do it.
    /// </summary>
    public sealed record NumberStyle(int Whole, int Decimals, bool Commas, string Plus, string Minus, string Zero, string Suffix, double Times)
    {
        /// <summary>Just digits (and a plain minus sign): drawn the original way.</summary>
        public bool Plain => Plus == "" && Minus == "-" && Zero == "" && Suffix == "" && Times == 1;
    }

    public static NumberStyle? NumberStyleOf(string format)
    {
        const string digitChars = "#0,.";
        var sections = format.Split(';');
        if (sections.Length > 3) return null;
        var split = new List<(string Pre, string Core, string Post)>();
        foreach (var section in sections)
        {
            var i = section.IndexOfAny(digitChars.ToCharArray());
            if (i < 0) return null;
            var j = i;
            while (j < section.Length && digitChars.Contains(section[j])) j++;
            var (pre, core, post) = (section[..i], section[i..j], section[j..]);
            // Plain words and signs only: no quotes, escapes, exponents or more digits.
            if ((pre + post).Any(c => c is '#' or '0' or '\\' or '\'' or '"' or 'E' or 'e' or '\u2030' || char.IsDigit(c))) return null;
            split.Add((pre, core, post));
        }
        if (DigitFormat(split[0].Core) is not { } digits) return null;
        // The other sections: the same digits (a zero needs no thousands comma) and the same words after them.
        if (split.Skip(1).Any(x => DigitFormat(x.Core) is not { } d || d.Whole != digits.Whole || d.Decimals != digits.Decimals || x.Post != split[0].Post)) return null;
        var times = format.Contains('%') ? 100.0 : 1.0;
        var minus = split.Count > 1 ? split[1].Pre : "-" + split[0].Pre;
        var zero = split.Count > 2 ? split[2].Pre : split[0].Pre;
        return new NumberStyle(digits.Whole, digits.Decimals, digits.Commas, split[0].Pre, minus, zero, split[0].Post, times);
    }

    /// <summary>
    /// A number display, digit by digit: each digit is a picture SPAD.neXt picks by value (its documented
    /// "Dynamic" image: name_{VALUE}.png), drawn by us in the gauge's own font and colour. Pictures 0-9 are
    /// the digits, 10 is blank (no leading zeros), 11 is a minus sign. The decimal point is a still picture.
    /// </summary>
    private static IEnumerable<XElement> NumberElements(GaugeDefinition gauge, JsonObject json, JsonObject node, TextLayer text, int number, string baseDir, string art, string? hidden = null)
    {
        var style = NumberStyleOf(text.Format)!;
        var (whole, decimals, commas) = (style.Whole, style.Decimals, style.Commas);
        var (lowest, highest) = Span(gauge, text.Value!);
        (lowest, highest) = (lowest * style.Times, highest * style.Times);
        var biggest = Math.Max(Math.Abs(highest), Math.Abs(lowest));
        var natural = biggest >= 1 ? (int)Math.Floor(Math.Log10(Math.Round(biggest))) + 1 : 0;
        var digits = Math.Clamp(Math.Max(whole, natural), whole == 0 && decimals > 0 ? 0 : 1, 7);
        var negative = style.Plain && lowest < 0;

        // Size and place, through any groups (offset, zoom) around the display.
        var (zoom, ox, oy) = Placement(json["layers"]!.AsArray(), node) ?? (1f, 0f, 0f);
        var size = text.Size * zoom;
        using var typeface = LoadTypeface(json["font"]?.GetValue<string>());
        using var font = new SKFont(typeface, size);
        var glyphs = "0123456789-".Select(c => font.MeasureText(c.ToString())).ToArray();
        var cell = (float)Math.Ceiling(glyphs.Max());
        var dot = (float)Math.Ceiling(font.MeasureText("."));
        var comma = (float)Math.Ceiling(font.MeasureText(","));
        var commaCount = commas ? (digits - 1) / 3 : 0;
        var ascent = -font.Metrics.Ascent;
        var height = (int)Math.Ceiling(ascent + font.Metrics.Descent) + 2;
        // Words or signs around the digits (not for plain digits): measured in the gauge's font.
        string[] fronts = style.Plain ? [] : [style.Plus, style.Minus, style.Zero];
        var front = fronts.Length == 0 ? 0 : (float)Math.Ceiling(fronts.Max(t => font.MeasureText(t)));
        var back = style.Suffix == "" ? 0 : (float)Math.Ceiling(font.MeasureText(style.Suffix));
        // A one-character sign ("+", "-") moves to the slot just left of the first digit, like Live's text.
        var signInSlots = fronts.Length > 0 && fronts.All(t => t.Length <= 1) && front <= cell;
        if (signInSlots) front = cell;
        var width = (negative ? cell : 0) + front + digits * cell + commaCount * comma + (decimals > 0 ? dot + decimals * cell : 0) + back;
        var x = ox + text.X * zoom;
        var left = text.Align switch { "left" => x, "right" => x - width, _ => x - width / 2 };
        var top = oy + text.Y * zoom - ascent - 1;

        // The pictures: 0-9, blank, minus, and the point, each the width of one cell.
        var color = SKColor.TryParse(text.Color, out var c) ? c : SKColors.White;
        var prefix = $"number{number}_";
        for (var i = 0; i <= (signInSlots ? 14 : 11); i++)
        {
            using var cellImage = new SKBitmap((int)cell, height);
            using (var canvas = new SKCanvas(cellImage))
            {
                canvas.Clear(SKColors.Transparent);
                using var paint = new SKPaint { Color = color, IsAntialias = true };
                using var glyphFont = new SKFont(typeface, size) { SkewX = text.Italic ? -0.22f : 0, Edging = SKFontEdging.Antialias };
                // 12-14: the sign for a positive, negative or zero number (signs that sit next to the digits).
                var glyph = i switch { 10 => "", 11 => "-", >= 12 => fronts[i - 12], _ => i.ToString(CultureInfo.InvariantCulture) };
                if (glyph != "") canvas.DrawText(glyph, cell / 2, ascent + 1, SKTextAlign.Center, glyphFont, paint);
            }
            Save(cellImage, Path.Combine(art, $"{prefix}{i}.png"));
        }

        // The RPN for the rounded number as a whole number of its last shown digit: abs(v) * 10^decimals, rounded.
        var v = ValueRpn(gauge, text.Value!)!;
        if (style.Times != 1) v = $"{v} {Num(style.Times)} *";
        var scaled = $"{v} abs {Num(Math.Pow(10, decimals))} * 0.5 + flr";
        var cellSizes = $"{(int)cell},{height},{(int)cell},{height}";
        // On a part that hides (a radio altitude that disappears high up): every digit shows blank (10) while hidden.
        XElement Digit(string id, float at, string shown) =>
            DigitElement(id, at, hidden is null ? shown : $"{hidden} if{{ 10 }} els{{ {shown} }}");
        XElement DigitElement(string id, float at, string expression) =>
            new("Element", new XAttribute("id", id),
                new XElement("Position", new XAttribute("X", Num(at)), new XAttribute("Y", Num(top))),
                new XElement("Image", new XAttribute("Name", $"{prefix}10.png"), new XAttribute("Dynamic", $"{prefix}{{VALUE}}.png"),
                    new XAttribute("ImageSizes", cellSizes), new XAttribute("Alpha", true),
                    new XElement("Value", new XAttribute("Minimum", 0), new XAttribute("Maximum", signInSlots ? 14 : 11), expression)));

        // A word or sign drawn into a picture of its own (right-aligned when it goes in front of the digits).
        XElement Words(string id, string picture, float at, float w, string words, bool right)
        {
            using var image = new SKBitmap(Math.Max(1, (int)w), height);
            using (var canvas = new SKCanvas(image))
            {
                canvas.Clear(SKColors.Transparent);
                using var paint = new SKPaint { Color = color, IsAntialias = true };
                using var wordFont = new SKFont(typeface, size) { SkewX = text.Italic ? -0.22f : 0, Edging = SKFontEdging.Antialias };
                if (words != "") canvas.DrawText(words, right ? w : 0, ascent + 1, right ? SKTextAlign.Right : SKTextAlign.Left, wordFont, paint);
            }
            Save(image, Path.Combine(art, picture));
            return new XElement("Element", new XAttribute("id", id),
                new XElement("Position", new XAttribute("X", Num(at)), new XAttribute("Y", Num(top))),
                new XElement("Image", new XAttribute("Name", picture), new XAttribute("ImageSizes", $"{Math.Max(1, (int)w)},{height},{Math.Max(1, (int)w)},{height}"), new XAttribute("Alpha", true)));
        }

        // A still part of the number (the point, words after it): a plain picture, or, on a part that hides, two
        // pictures (0 blank, 1 shown) picked by the hide rule.
        XElement Still(string id, string picture, float at, float w)
        {
            var sizes = $"{Math.Max(1, (int)w)},{height},{Math.Max(1, (int)w)},{height}";
            if (hidden is null)
                return new XElement("Element", new XAttribute("id", id),
                    new XElement("Position", new XAttribute("X", Num(at)), new XAttribute("Y", Num(top))),
                    new XElement("Image", new XAttribute("Name", picture + ".png"), new XAttribute("ImageSizes", sizes), new XAttribute("Alpha", true)));
            File.Copy(Path.Combine(art, picture + ".png"), Path.Combine(art, picture + "1.png"), overwrite: true);
            using (var blank = new SKBitmap(Math.Max(1, (int)w), height)) { blank.Erase(SKColors.Transparent); Save(blank, Path.Combine(art, picture + "0.png")); }
            return new XElement("Element", new XAttribute("id", id),
                new XElement("Position", new XAttribute("X", Num(at)), new XAttribute("Y", Num(top))),
                new XElement("Image", new XAttribute("Name", picture + "1.png"), new XAttribute("Dynamic", picture + "{VALUE}.png"),
                    new XAttribute("ImageSizes", sizes), new XAttribute("Alpha", true),
                    new XElement("Value", new XAttribute("Minimum", 0), new XAttribute("Maximum", 1), $"{hidden} if{{ 0 }} els{{ 1 }}")));
        }

        var at = left;
        if (negative)
        {
            yield return Digit($"number{number}_sign", at, $"{v} 0 < if{{ 11 }} els{{ 10 }}");
            at += cell;
        }
        // Which sign: 0 positive, 1 negative, 2 shows as zero.
        var whichSign = $"{scaled} 1 < if{{ 2 }} els{{ {v} 0 < if{{ 1 }} els{{ 0 }} }}";
        var topPlace = digits + decimals - 1;
        var topBlankable = signInSlots && topPlace - decimals >= whole;
        // With the sign in the slots, the front picture shows it only when the top slot holds a digit (3 = blank).
        var frontExpression = topBlankable ? $"{scaled} {Num(Math.Pow(10, topPlace))} < if{{ 3 }} els{{ {whichSign} }}" : whichSign;
        if (hidden is not null) frontExpression = $"{hidden} if{{ 3 }} els{{ {frontExpression} }}";
        if (fronts.Length > 0)
        {
            // In front: picture 0 for a positive number, 1 for a negative one, 2 when it shows as zero.
            for (var i = 0; i < 4; i++) _ = Words("", $"{prefix}front{i}.png", 0, front, i < 3 ? fronts[i] : "", right: !signInSlots); // just the pictures
            yield return new XElement("Element", new XAttribute("id", $"number{number}_front"),
                new XElement("Position", new XAttribute("X", Num(at)), new XAttribute("Y", Num(top))),
                new XElement("Image", new XAttribute("Name", $"{prefix}front0.png"), new XAttribute("Dynamic", $"{prefix}front{{VALUE}}.png"),
                    new XAttribute("ImageSizes", $"{Math.Max(1, (int)front)},{height},{Math.Max(1, (int)front)},{height}"), new XAttribute("Alpha", true),
                    new XElement("Value", new XAttribute("Minimum", 0), new XAttribute("Maximum", 3), frontExpression)));
            at += front;
        }
        // ".00": the point comes first.
        if (digits == 0 && decimals > 0)
        {
            using var point = new SKBitmap((int)dot, height);
            using (var canvas = new SKCanvas(point))
            {
                canvas.Clear(SKColors.Transparent);
                using var paint = new SKPaint { Color = color, IsAntialias = true };
                canvas.DrawText(".", dot / 2, ascent + 1, SKTextAlign.Center, font, paint);
            }
            Save(point, Path.Combine(art, $"{prefix}point.png"));
            yield return Still($"number{number}_point", $"{prefix}point", at, dot);
            at += dot;
        }
        // Left to right: the highest place first. Place p counts from the last shown digit (p = 0).
        var places = digits + decimals;
        for (var k = 0; k < places; k++)
        {
            var p = places - 1 - k;
            var digit = $"{scaled} {Num(Math.Pow(10, p))} / flr {scaled} {Num(Math.Pow(10, p + 1))} / flr 10 * -";
            // Whole-number places beyond the format's own zeros stay blank while the number is smaller.
            var blankable = p - decimals >= whole;
            var blank = "10";
            if (signInSlots && blankable)
            {
                // The sign goes here when this slot is blank and the next one holds the first digit.
                var sign = $"{whichSign} 12 +";
                blank = p - 1 - decimals >= whole ? $"{scaled} {Num(Math.Pow(10, p - 1))} < if{{ 10 }} els{{ {sign} }}" : sign;
            }
            yield return Digit($"number{number}_d{k}", at, blankable ? $"{scaled} {Num(Math.Pow(10, p))} < if{{ {blank} }} els{{ {digit} }}" : digit);
            at += cell;
            // Thousands comma after the thousands (millions...) digit, shown only once the number gets that big.
            if (commas && p - decimals > 0 && (p - decimals) % 3 == 0)
            {
                var commaSizes = $"{(int)comma},{height},{(int)comma},{height}";
                for (var shown = 0; shown <= 1; shown++)
                {
                    using var picture = new SKBitmap((int)comma, height);
                    using (var canvas = new SKCanvas(picture))
                    {
                        canvas.Clear(SKColors.Transparent);
                        using var paint = new SKPaint { Color = color, IsAntialias = true };
                        if (shown == 1) canvas.DrawText(",", comma / 2, ascent + 1, SKTextAlign.Center, font, paint);
                    }
                    Save(picture, Path.Combine(art, $"{prefix}comma{shown}.png"));
                }
                yield return new XElement("Element", new XAttribute("id", $"number{number}_comma{p - decimals}"),
                    new XElement("Position", new XAttribute("X", Num(at)), new XAttribute("Y", Num(top))),
                    new XElement("Image", new XAttribute("Name", $"{prefix}comma0.png"), new XAttribute("Dynamic", $"{prefix}comma{{VALUE}}.png"),
                        new XAttribute("ImageSizes", commaSizes), new XAttribute("Alpha", true),
                        new XElement("Value", new XAttribute("Minimum", 0), new XAttribute("Maximum", 1),
                            hidden is null ? $"{scaled} {Num(Math.Pow(10, p))} < if{{ 0 }} els{{ 1 }}" : $"{hidden} if{{ 0 }} els{{ {scaled} {Num(Math.Pow(10, p))} < if{{ 0 }} els{{ 1 }} }}")));
                at += comma;
            }
            if (decimals > 0 && p == decimals)
            {
                using var point = new SKBitmap((int)dot, height);
                using (var canvas = new SKCanvas(point))
                {
                    canvas.Clear(SKColors.Transparent);
                    using var paint = new SKPaint { Color = color, IsAntialias = true };
                    canvas.DrawText(".", dot / 2, ascent + 1, SKTextAlign.Center, font, paint);
                }
                Save(point, Path.Combine(art, $"{prefix}point.png"));
                yield return Still($"number{number}_point", $"{prefix}point", at, dot);
                at += dot;
            }
        }
        if (back > 0)
        {
            _ = Words("", $"{prefix}back.png", at, back, style.Suffix, right: false); // the picture
            yield return Still($"number{number}_back", $"{prefix}back", at, back);
        }
    }

    private static SKTypeface LoadTypeface(string? font)
    {
        foreach (var candidate in new[] { font, font is null ? null : Path.Combine(AppContext.BaseDirectory, Path.GetFileName(font)) })
            if (candidate is not null && File.Exists(candidate) && SKTypeface.FromFile(candidate) is { } typeface) return typeface;
        return SKTypeface.FromFamilyName(null);
    }

    /// <summary>
    /// A value as FSX-style RPN for SPAD.neXt: a sim variable, a worked-out formula (translated, with the
    /// values it uses translated too), or a fixed number for a value nothing sets (e.g. "knobMode").
    /// Null if a formula can't be translated.
    /// </summary>
    public static string? ValueRpn(GaugeDefinition gauge, string value, int depth = 0)
    {
        if (depth > 16 || !gauge.Values.TryGetValue(value, out var spec)) return null;
        if (spec.Expr is { } expr)
        {
            try { return ValueExpression.Parse(expr).ToRpn(n => ValueRpn(gauge, n, depth + 1)); }
            catch (FormatException) { return null; }
        }
        return SimValue(gauge, value) ?? Num(spec.Default);
    }

    /// <summary>
    /// The smallest and largest a value gets (to size number displays): its slider range, or for a worked-out
    /// value, the formula tried with its inputs across their ranges.
    /// </summary>
    private static (double Low, double High) Span(GaugeDefinition gauge, string value)
    {
        var ranges = GaugeValueRanges.For(gauge);
        if (ranges.FirstOrDefault(r => r.Name == value) is { } own) return (own.Min, own.Max);
        using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
        var (low, high) = (double.MaxValue, double.MinValue);
        foreach (var f in new[] { 0, 0.25, 0.5, 0.75, 1 })
        {
            var inputs = ranges.ToDictionary(r => r.Name, r => r.Min + (r.Max - r.Min) * f);
            var v = renderer.ValueOf(value, inputs);
            if (!double.IsFinite(v)) continue;
            (low, high) = (Math.Min(low, v), Math.Max(high, v));
        }
        return low <= high ? (low, high) : (0, 999);
    }

    /// <summary>The sim variable in FSX gauge style, e.g. "(A:AIRSPEED INDICATED, knots)"; null if worked out.</summary>
    public static string? SimValue(GaugeDefinition gauge, string value)
    {
        if (!gauge.Values.TryGetValue(value, out var spec) || spec.Sim is null || spec.Expr is not null) return null;
        var unit = string.IsNullOrWhiteSpace(spec.Unit) ? "number" : spec.Unit;
        return spec.Sim.StartsWith("L:", StringComparison.OrdinalIgnoreCase)
            ? $"({spec.Sim}, {unit})"
            : $"(A:{spec.Sim}, {unit})";
    }

    private static void Draw(JsonObject json, List<JsonObject> leaves, List<int> keep, bool gaugeBackground, string baseDir, string file)
    {
        using var bitmap = RenderOnly(json.DeepClone().AsObject(), leaves.Count, keep, gaugeBackground, baseDir);
        Save(bitmap, file);
    }

    /// <summary>Draws only the chosen layers (by drawing-order number), on the gauge's background or see-through.</summary>
    private static SKBitmap RenderOnly(JsonObject copy, int count, List<int> keep, bool gaugeBackground, string baseDir)
    {
        var leaves = new List<JsonObject>();
        Collect(copy["layers"]!.AsArray(), leaves);
        var drop = Enumerable.Range(0, count).Where(i => !keep.Contains(i)).Select(i => leaves[i]).ToHashSet();
        Prune(copy["layers"]!.AsArray(), drop);
        if (!gaugeBackground) copy["canvas"]!["background"] = "#00000000";
        using var renderer = new GaugeRenderer(GaugeDefinition.Parse(copy.ToJsonString()), baseDir);
        return renderer.Render();
    }

    private static void Collect(JsonArray layers, List<JsonObject> into)
    {
        foreach (var layer in layers.OfType<JsonObject>())
        {
            if (layer["type"]?.GetValue<string>() == "group") Collect(layer["layers"]?.AsArray() ?? [], into);
            else into.Add(layer);
        }
    }

    private static void Prune(JsonArray layers, HashSet<JsonObject> drop)
    {
        for (var i = layers.Count - 1; i >= 0; i--)
        {
            if (layers[i] is not JsonObject layer) continue;
            if (layer["type"]?.GetValue<string>() == "group")
            {
                Prune(layer["layers"]?.AsArray() ?? [], drop);
                if (layer["layers"]?.AsArray().Count is null or 0) layers.RemoveAt(i);
            }
            else if (drop.Contains(layer)) layers.RemoveAt(i);
        }
    }

    /// <summary>The zoom and offset the groups around <paramref name="target"/> apply (null if not found).</summary>
    private static (float Zoom, float X, float Y)? Placement(JsonArray layers, JsonObject target)
    {
        foreach (var layer in layers.OfType<JsonObject>())
        {
            if (ReferenceEquals(layer, target)) return (1, 0, 0);
            if (layer["type"]?.GetValue<string>() != "group") continue;
            if (Placement(layer["layers"]?.AsArray() ?? [], target) is not { } inner) continue;
            var zoom = layer["zoom"]?.GetValue<float>() is { } z and > 0 ? z : 1;
            var offset = layer["offset"]?.AsArray();
            var (ox, oy) = offset is { Count: 2 } ? (offset[0]!.GetValue<float>(), offset[1]!.GetValue<float>()) : (0f, 0f);
            // Drawing: translate(offset), scale(zoom), then the inner placement.
            return (zoom * inner.Zoom, ox + zoom * inner.X, oy + zoom * inner.Y);
        }
        return null;
    }

    private static IEnumerable<Layer> Flatten(IEnumerable<Layer> layers)
    {
        foreach (var layer in layers)
        {
            yield return layer;
            if (layer is GroupLayer g) foreach (var inner in Flatten(g.Layers)) yield return inner;
        }
    }

    /// <summary>The smallest box around the picture's non-see-through pixels (1 px margin).</summary>
    private static SKRectI Visible(SKBitmap bitmap)
    {
        int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).Alpha > 0)
                {
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
        if (right < 0) return new SKRectI(0, 0, 1, 1);
        return new SKRectI(Math.Max(0, left - 1), Math.Max(0, top - 1), Math.Min(bitmap.Width, right + 2), Math.Min(bitmap.Height, bottom + 2));
    }

    private static void Save(SKBitmap bitmap, string file)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(file, data.ToArray());
    }

    private static string Num(double v) => Math.Round(v, 2).ToString(CultureInfo.InvariantCulture);

    private static string SafeName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();

    private static string ReadMe(GaugeDefinition gauge, List<string> leftOut) =>
        $"""
        {gauge.Name}: a SPAD.neXt FIP gauge made with FIPinator 3000 (free, open source, AGPL-3.0).

        To use it: put this .zip in a folder SPAD.neXt knows as a gauge location (on the FIP page,
        "add location"), refresh, then pick the gauge. Don't unzip it, and don't put it inside SPAD.neXt's
        own program folder.

        Exported: the dial, everything that moves, and the knobs that send a plain sim event (new: tell us
        if a knob does nothing; your own SPAD.neXt knob settings win over them).
        FIPinator Live only: page flipping with the left knob, flipping by itself{(leftOut.Count > 0 ? ", " + string.Join(", ", leftOut) : "")}.

        This file marks the .zip as a FIPinator export, so saving again may replace it.
        """;
}
