// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Text.Json.Nodes;
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class GaugeEditorTests
{
    private static string Gauges => Path.Combine(AppContext.BaseDirectory, "repo", "gauges");
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Gauges, .. parts]));
    private static GaugeEditor TwinRpm() => new(Read("classic", "twin", "rpm.json"));

    private static GaugePart Find(GaugeEditor editor, PartKind kind) => editor.Parts().First(p => p.Kind == kind);

    [Fact]
    public void Quick_colours_change_one_group_only_and_can_be_undone()
    {
        var editor = new GaugeEditor(Read("dc6", "altimeter.json"));
        var groups = editor.ColourGroups();
        Assert.Contains(groups, g => g.Role == "Background");
        var needle = groups.First(g => g.Role == "Needles");
        var markingsBefore = groups.Where(g => g.Role == "Markings and numbers").ToList();

        editor.ReplaceColour("Needles", needle.Colour, "#E8892B");
        var after = editor.ColourGroups();
        Assert.Contains(after, g => g.Role == "Needles" && g.Colour == "#E8892B");
        Assert.DoesNotContain(after, g => g.Role == "Needles" && g.Colour == needle.Colour);
        Assert.Equal(markingsBefore, after.Where(g => g.Role == "Markings and numbers").ToList()); // numbers untouched
        Assert.NotNull(editor.Check(out _));

        editor.Undo();
        Assert.Equal(groups, editor.ColourGroups());
    }

    [Fact]
    public void Lists_parts_in_plain_words()
    {
        var parts = TwinRpm().Parts();
        Assert.Contains(parts, p => p.Kind == PartKind.Needle && p.Title == "Needle: General eng rpm (engine 2)");
        Assert.Contains(parts, p => p.Title == "Tick marks");
        Assert.Contains(parts, p => p.Title == "Scale numbers");
        Assert.Contains(parts, p => p.Title == "Screw");
    }

    [Fact]
    public void Every_shipped_gauge_lists_its_parts_and_settings_without_errors()
    {
        foreach (var file in Directory.GetFiles(Gauges, "*.json", SearchOption.AllDirectories))
        {
            var editor = new GaugeEditor(File.ReadAllText(file));
            foreach (var part in editor.Parts())
            {
                _ = editor.Settings(part.Id);
                _ = editor.Bounds(part.Id);
            }
            Assert.NotNull(editor.Check(out var problems));
            Assert.Empty(problems);
        }
    }

    [Fact]
    public void Colour_change_keeps_the_rest_of_the_file_and_can_be_undone()
    {
        var editor = TwinRpm();
        var before = editor.Json;
        var needle = Find(editor, PartKind.Needle);
        editor.Set(needle.Id, "color", "#ff0000");
        Assert.Equal("#FF0000", editor.Settings(needle.Id).Single(s => s.Key == "color").Current);
        Assert.NotNull(editor.Check(out _));

        editor.Undo();
        Assert.Equal(before, editor.Json);
        editor.Redo();
        Assert.Contains("#FF0000", editor.Json);
    }

    [Fact]
    public void Bad_input_changes_nothing()
    {
        var editor = TwinRpm();
        var needle = Find(editor, PartKind.Needle);
        Assert.Throws<ArgumentException>(() => editor.Set(needle.Id, "color", "red-ish"));
        Assert.Throws<ArgumentException>(() => editor.Set(needle.Id, "length", "long"));
        Assert.Throws<ArgumentException>(() => editor.Set(needle.Id, "value", "no_such_value"));
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void Numbers_are_kept_within_sensible_limits()
    {
        var editor = TwinRpm();
        var needle = Find(editor, PartKind.Needle);
        editor.Set(needle.Id, "length", "5000");
        Assert.Equal("200", editor.Settings(needle.Id).Single(s => s.Key == "length").Current);
    }

    [Fact]
    public void Moving_a_dial_part_moves_its_centre_and_a_screw_its_position()
    {
        var editor = TwinRpm();
        var ticks = Find(editor, PartKind.Ticks);
        var before = editor.Bounds(ticks.Id)!.Value;
        editor.Move(ticks.Id, 10, -5);
        var after = editor.Bounds(ticks.Id)!.Value;
        Assert.Equal(before.X + 10, after.X, 3);
        Assert.Equal(before.Y - 5, after.Y, 3);

        var screw = Find(editor, PartKind.Screw);
        var s0 = editor.Bounds(screw.Id)!.Value;
        editor.Move(screw.Id, 3, 4);
        Assert.Equal(s0.X + 3, editor.Bounds(screw.Id)!.Value.X, 3);
        Assert.NotNull(editor.Check(out _));
    }

    [Fact]
    public void Dragging_is_one_undo_step()
    {
        var editor = TwinRpm();
        var before = editor.Json;
        var screw = Find(editor, PartKind.Screw);
        editor.Checkpoint();
        for (var i = 0; i < 20; i++) editor.Move(screw.Id, 1, 1, checkpoint: false);
        editor.Undo();
        Assert.Equal(before, editor.Json);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void Adds_a_number_box_that_draws_and_moves_as_one()
    {
        var editor = TwinRpm();
        var value = editor.ValueChoices().First().Name;
        var id = editor.AddNumberBox(value);
        var part = editor.Parts().Single(p => p.Id == id);
        Assert.Equal(PartKind.NumberBox, part.Kind);
        Assert.DoesNotContain(editor.Parts(), p => p.Id.Path.Length > 1 && p.Id.Path[0] == id.Path[0]); // shown as one part
        Assert.Contains(editor.Settings(id), s => s.Key == "text.value" && s.Current == value);

        Assert.Contains(editor.Settings(id), s => s.Key == "text.size" && s.Current == "20");
        Assert.Contains(editor.Settings(id), s => s.Key == "rect.width" && s.Current == "68");
        editor.Set(id, "text.size", "24");
        Assert.Contains(editor.Settings(id), s => s.Key == "text.size" && s.Current == "24");
        editor.Set(id, "text.format", "0.0");
        editor.Set(id, "rect.fill", "#202020");
        var b0 = editor.Bounds(id)!.Value;
        editor.Move(id, 0, 20);
        Assert.Equal(b0.Y + 20, editor.Bounds(id)!.Value.Y, 3);
        Assert.NotNull(editor.Check(out var problems));
        Assert.Empty(problems);

        using var renderer = new GaugeRenderer(GaugeDefinition.Parse(editor.Json), Path.Combine(Gauges, "classic", "twin"));
        using var bitmap = renderer.Render();
        Assert.Equal(320, bitmap.Width);
    }

    [Fact]
    public void A_new_sim_value_is_added_once()
    {
        var editor = TwinRpm();
        var a = editor.UseSimValue("ENG FUEL FLOW GPH:1", "gallons per hour");
        var b = editor.UseSimValue("ENG FUEL FLOW GPH:1", "gallons per hour");
        Assert.Equal(a, b);
        Assert.Contains(editor.ValueChoices(), v => v.Name == a && v.Label.Contains("engine 1"));
        // An existing one is reused.
        Assert.Equal("e1", editor.UseSimValue("GENERAL ENG RPM:1", "rpm"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Engine_needles_one_two_or_four(int count)
    {
        var editor = TwinRpm();
        Assert.Equal(2, editor.EngineNeedles);
        Assert.Null(editor.SetEngineNeedles(count));
        Assert.Equal(count, editor.EngineNeedles);
        var gauge = editor.Check(out var problems);
        Assert.Empty(problems);
        var sims = gauge!.Layers.OfType<NeedleLayer>().Select(n => gauge.Values[n.Value].Sim).ToList();
        Assert.Equal(Enumerable.Range(1, count).Select(i => $"GENERAL ENG RPM:{i}"), sims.Order());
        var tags = gauge.Layers.OfType<NeedleLayer>().Select(n => n.Tag).ToList();
        if (count == 1) Assert.All(tags, Assert.Null);
        else Assert.Equal(Enumerable.Range(1, count).Select(i => i.ToString()), tags.Order());

        // And back again in one undo.
        editor.Undo();
        Assert.Equal(2, editor.EngineNeedles);
    }

    [Fact]
    public void A_needle_that_follows_no_engine_cant_be_doubled()
    {
        var editor = new GaugeEditor(Read("classic", "single", "airspeed.json"));
        Assert.Equal(0, editor.EngineNeedles);
        Assert.NotNull(editor.SetEngineNeedles(2));
        Assert.False(editor.CanUndo);
    }

    [Theory]
    [InlineData("classic/nav/rmi-vor-adf.json")]
    [InlineData("dc6/rmi-vor-adf.json")]
    [InlineData("classic/nav/rmi-vor.json")]
    [InlineData("classic/nav/rmi-adf.json")]
    [InlineData("classic/single/adf.json")]
    public void Rmi_needles_turn_on_and_off(string file)
    {
        var editor = new GaugeEditor(Read(file.Split('/')));
        Assert.True(editor.IsRmi);
        var before = editor.Json;
        var radios = Enum.GetValues<RmiRadio>();
        var added = radios.Where(r => !editor.HasRmiNeedle(r)).ToList();

        // All four on: each needle reads its own radio.
        foreach (var r in radios) Assert.Null(editor.SetRmiNeedle(r, true));
        Assert.All(radios, r => Assert.True(editor.HasRmiNeedle(r)));
        var gauge = editor.Check(out var problems);
        Assert.Empty(problems);
        var needles = gauge!.Layers.SelectMany(Flatten).OfType<NeedleLayer>().ToList();
        Assert.Equal(4, needles.Count);
        Assert.Equal(4, needles.Select(n => n.Value).Distinct().Count());
        // Each added needle gets one corner key.
        foreach (var r in added)
            Assert.Single(gauge.Layers.OfType<TextLayer>(), t => t.Text is { } s && s.Contains(GaugeEditor.RmiName(r)));
        // The four needles can be told apart by colour and shape.
        Assert.Equal(4, needles.Select(n => (n.Color, n.Style)).Distinct().Count());
        using (var renderer = new GaugeRenderer(gauge, Path.Combine([Gauges, .. file.Split('/')[..^1]])))
        using (var bitmap = renderer.Render()) Assert.Equal(320, bitmap.Width);

        // Off again, down to one; the last one stays.
        foreach (var r in radios.Skip(1)) Assert.Null(editor.SetRmiNeedle(r, false));
        Assert.NotNull(editor.SetRmiNeedle(RmiRadio.Vor1, false));
        Assert.True(editor.HasRmiNeedle(RmiRadio.Vor1));
        Assert.DoesNotContain(editor.Check(out _)!.Layers.OfType<TextLayer>(), t => t.Text is { } s && (s.Contains("VOR 2") || s.Contains("ADF 1") || s.Contains("ADF 2")));
        Assert.NotNull(editor.Check(out problems));
        Assert.Empty(problems);

        // Every step undoes.
        while (editor.CanUndo) editor.Undo();
        Assert.Equal(before, editor.Json);
    }

    [Fact]
    public void Rmi_needle_points_to_the_station()
    {
        // VOR 1 on: radial 90 from the station, heading 0, so the station is due west: needle at 270.
        var editor = new GaugeEditor(Read("classic", "nav", "rmi-adf.json"));
        Assert.Null(editor.SetRmiNeedle(RmiRadio.Vor1, true));
        var gauge = editor.Check(out _)!;
        var needle = gauge.Layers.SelectMany(Flatten).OfType<NeedleLayer>().Single(n => n.Value.StartsWith("vor1"));
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            [gauge.Values.Single(v => v.Value.Sim == "NAV RADIAL:1").Key] = 90,
            [gauge.Values.Single(v => v.Value.Sim == "NAV HAS NAV:1").Key] = 1,
            [gauge.Values.Single(v => v.Value.Sim == "PLANE HEADING DEGREES MAGNETIC").Key] = 0,
        };
        Assert.Equal(270, ValueExpression.Parse(gauge.Values[needle.Value].Expr!).Evaluate(n => values.GetValueOrDefault(n)), 3);
    }

    [Fact]
    public void A_gauge_without_vor_or_adf_needles_has_no_rmi_switches()
    {
        var editor = TwinRpm();
        Assert.False(editor.IsRmi);
        Assert.NotNull(editor.SetRmiNeedle(RmiRadio.Vor1, true));
        Assert.False(editor.CanUndo);
    }

    private static IEnumerable<Layer> Flatten(Layer layer) =>
        layer is GroupLayer g ? g.Layers.SelectMany(Flatten) : [layer];

    [Fact]
    public void Adds_a_needle_and_an_arc_on_the_dial()
    {
        var editor = new GaugeEditor(Read("classic", "single", "airspeed.json"));
        var value = editor.UseSimValue("AIRSPEED INDICATED", "knots");
        var needle = editor.AddNeedle(value);
        Assert.Equal(PartKind.Needle, editor.Parts().Single(p => p.Id == needle).Kind);
        var arc = editor.AddArc("#D3362B");
        Assert.Equal(PartKind.Arc, editor.Parts().Single(p => p.Id == arc).Kind);
        // The arc goes under the needles.
        var firstNeedle = editor.Parts().First(p => p.Kind == PartKind.Needle).Id.Path;
        Assert.Equal(firstNeedle[..^1], arc.Path[..^1]); // same dial (here: inside the dial's group)
        Assert.True(arc.Path[^1] < firstNeedle[^1]);
        Assert.NotNull(editor.Check(out var problems));
        Assert.Empty(problems);
    }

    [Fact]
    public void Remove_reorder_and_duplicate()
    {
        var editor = TwinRpm();
        var count = editor.Parts().Count;
        var screw = Find(editor, PartKind.Screw);
        var copy = editor.Duplicate(screw.Id);
        Assert.Equal(count + 1, editor.Parts().Count);
        var moved = editor.Reorder(copy, +1);
        Assert.Equal(copy.Path[0] + 1, moved.Path[0]);
        editor.Remove(moved);
        Assert.Equal(count, editor.Parts().Count);
        Assert.Equal(screw.Id, editor.Reorder(screw.Id, -1)); // already first: stays
    }

    [Fact]
    public void Clicking_picks_the_smallest_part_under_the_pointer()
    {
        var editor = TwinRpm();
        var screw = Find(editor, PartKind.Screw);
        var b = editor.Bounds(screw.Id)!.Value;
        Assert.Equal(screw.Id, editor.PartAt(b.X + b.W / 2, b.Y + b.H / 2));
    }

    [Fact]
    public void Background_name_and_look()
    {
        var editor = TwinRpm();
        editor.Background = "#123456";
        editor.Name = "  My RPM ";
        Assert.Equal("#123456", editor.Background);
        Assert.Equal("My RPM", editor.Name);
        Assert.Throws<ArgumentException>(() => editor.Background = "blue");
    }

    [Fact]
    public void Outside_change_replaces_the_gauge_and_undo_goes_back()
    {
        var editor = TwinRpm();
        var before = editor.Json;
        var node = JsonNode.Parse(before)!;
        node["name"] = "Changed by my AI";
        editor.Replace(node.ToJsonString());
        Assert.Equal("Changed by my AI", editor.Name);
        editor.Undo();
        Assert.Equal(before, editor.Json);
        Assert.Throws<System.Text.Json.JsonException>(() => editor.Replace("{ \"name\": 1 }"));
    }

    [Fact]
    public void Known_sim_values_come_from_the_shipped_gauges()
    {
        var known = GaugeEditor.KnownSimValues(Directory.GetFiles(Gauges, "*.json", SearchOption.AllDirectories));
        Assert.Contains(known, k => k.Sim == "AIRSPEED INDICATED" && k.Unit == "knots");
        Assert.Contains(known, k => k.Sim == "GENERAL ENG RPM:1");
        Assert.Equal(known.Count, known.Select(k => k.Sim + "|" + k.Unit).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Plain_labels_for_sim_values()
    {
        Assert.Equal("General eng rpm (engine 2), rpm", GaugeEditor.SimLabel("GENERAL ENG RPM:2", "rpm"));
        Assert.Equal("Airspeed indicated, knots", GaugeEditor.SimLabel("AIRSPEED INDICATED", "knots"));
        Assert.Equal("Nav obs (no. 1)", GaugeEditor.SimLabel("NAV OBS:1", "number"));
    }
}
