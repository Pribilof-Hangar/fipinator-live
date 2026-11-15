// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class FormatAdditionsTests
{
    [Theory]
    [InlineData(40, 0)]
    [InlineData(60, 30)]    // halfway between 40 (0°) and 80 (60°)
    [InlineData(80, 60)]
    [InlineData(240, 220)]  // halfway between 80 (60°) and 400 (380°)
    [InlineData(10, 0)]     // below: held at the stop
    [InlineData(999, 380)]  // above: held at the stop
    public void Uneven_scale_fills_in_between_points(double value, float expected)
    {
        var scale = new ScaleSpec { Points = [[40, 0], [80, 60], [400, 380]] };
        Assert.Equal(expected, scale.AngleFor(value), 3);
        Assert.Equal((40, 400), (scale.Low, scale.High));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(250, 90)]
    [InlineData(1250, 90)]   // second time round
    [InlineData(-250, 270)]  // below zero goes round backwards
    public void Wrapping_scale_goes_round(double value, float expected)
    {
        var scale = new ScaleSpec { Min = 0, Max = 1000, StartAngle = 0, EndAngle = 360, Wrap = true };
        Assert.Equal(expected, scale.AngleFor(value), 3);
    }

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("-a + 10", 7)]
    [InlineData("wrap360(a - 10)", 353)]
    [InlineData("wrap180(270)", -90)]
    [InlineData("clamp(a * 100, -50, 50)", 50)]
    [InlineData("min(a, 2) + max(a, 2) + abs(-1)", 6)]
    [InlineData("10 / 0", 0)] // no crash on divide by zero
    public void Formulas_work_out_values(string formula, double expected)
    {
        var result = ValueExpression.Parse(formula).Evaluate(name => name == "a" ? 3 : 0);
        Assert.Equal(expected, result, 6);
    }

    [Theory]
    [InlineData("1 +")]
    [InlineData("foo(1)")]
    [InlineData("min(1)")]
    [InlineData("(1 + 2")]
    [InlineData("1 2")]
    public void Bad_formulas_give_a_clear_error(string formula)
    {
        var ex = Assert.Throws<FormatException>(() => ValueExpression.Parse(formula));
        Assert.Contains(formula, ex.Message);
    }

    [Fact]
    public void Renderer_works_out_formula_values_from_live_ones()
    {
        var gauge = GaugeDefinition.Parse("""
            { "name": "RMI", "values": {
                "heading": { "sim": "PLANE HEADING DEGREES MAGNETIC", "unit": "degrees" },
                "radial":  { "sim": "NAV RADIAL:1", "unit": "degrees" },
                "bearing": { "expr": "wrap360(radial + 180 - heading)" } } }
            """);
        using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
        var live = new Dictionary<string, double> { ["heading"] = 90, ["radial"] = 45 };
        Assert.Equal(135, renderer.ValueOf("bearing", live), 6);
    }

    [Fact]
    public void Problems_lists_every_mistake_in_plain_words()
    {
        var gauge = GaugeDefinition.Parse("""
            { "name": "Broken",
              "values": {
                "a": { "expr": "b + 1" },
                "b": { "expr": "a * 2" },
                "c": { "expr": "nothere + 1" },
                "d": { "expr": "1 +" } },
              "scales": { "s": { "points": [[10, 0], [5, 90]] } },
              "layers": [
                { "type": "needle", "scale": "missing", "value": "a", "length": 50 },
                { "type": "group", "rotate": { "value": "ghost", "scale": "s" }, "layers": [
                    { "type": "ticks", "scale": "alsomissing", "radius": 50, "length": 5, "every": 10 } ] } ] }
            """);
        var problems = string.Join("\n", gauge.Problems());
        Assert.Contains("'a' depends on itself", problems);
        Assert.Contains("'nothere'", problems);
        Assert.Contains("value 'd'", problems);
        Assert.Contains("increasing value order", problems);
        Assert.Contains("scale 'missing'", problems);
        Assert.Contains("value 'ghost'", problems);
        Assert.Contains("scale 'alsomissing'", problems); // found inside the group too
    }

    [Fact]
    public void Rotating_group_turns_its_contents()
    {
        // A white dot at 12 o'clock on a card that turns with "heading".
        var gauge = GaugeDefinition.Parse("""
            { "name": "Card", "canvas": { "background": "#000000" },
              "values": { "heading": { "default": 0 } },
              "scales": { "card": { "min": 0, "max": 360, "startAngle": 0, "endAngle": -360 } },
              "layers": [ { "type": "group", "rotate": { "value": "heading", "scale": "card" }, "layers": [
                  { "type": "circle", "center": [160, 40], "r": 6, "fill": "#FFFFFF" } ] } ] }
            """);
        using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
        using var north = renderer.Render(new Dictionary<string, double> { ["heading"] = 0 });
        using var east = renderer.Render(new Dictionary<string, double> { ["heading"] = 90 });

        Assert.Equal(255, north.GetPixel(160, 40).Red);
        // Heading 90: the card turns 90° anticlockwise, so the dot moves to 9 o'clock (80 px left of center).
        Assert.Equal(0, east.GetPixel(160, 40).Red);
        Assert.Equal(255, east.GetPixel(80, 120).Red);
    }

    [Fact]
    public void Sliding_group_stays_inside_its_clip()
    {
        var gauge = GaugeDefinition.Parse("""
            { "name": "Bar", "canvas": { "background": "#000000" },
              "values": { "dev": { "default": 0 } },
              "scales": { "dots": { "min": -2, "max": 2, "startAngle": -200, "endAngle": 200 } },
              "layers": [ { "type": "group", "moveX": { "value": "dev", "scale": "dots" }, "clip": 100, "layers": [
                  { "type": "rect", "x": 158, "y": 20, "width": 4, "height": 200, "fill": "#FFFFFF" } ] } ] }
            """);
        using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
        using var left = renderer.Render(new Dictionary<string, double> { ["dev"] = -0.5 });
        using var far = renderer.Render(new Dictionary<string, double> { ["dev"] = 2 });

        Assert.Equal(255, left.GetPixel(110, 120).Red);  // moved 50 px left
        Assert.Equal(0, far.GetPixel(160, 120).Red);     // moved 200 px right: outside the clip, hidden
    }

    private static string GaugesRoot => Path.Combine(AppContext.BaseDirectory, "repo", "gauges");

    // Paths relative to gauges/ (several sets have an airspeed.json, so the name alone isn't enough).
    public static TheoryData<string> GaugeFiles() =>
        [.. Directory.GetFiles(GaugesRoot, "*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(GaugesRoot, f).Replace('\\', '/'))];

    [Theory]
    [MemberData(nameof(GaugeFiles))]
    public void Every_gauge_file_loads_without_mistakes_and_renders(string file)
    {
        var path = Path.Combine(GaugesRoot, file);
        using var renderer = GaugeRenderer.FromFile(path); // Load() throws if the file has mistakes
        using var bitmap = renderer.Render();
        Assert.Equal((320, 240), (bitmap.Width, bitmap.Height));
    }

    [Theory]
    [InlineData(1, 40, 40)]   // signal: needle shows the bearing
    [InlineData(0, 40, 90)]   // no signal: needle parks at 3 o'clock
    public void Choose_picks_by_signal(double signal, double bearing, double expected)
    {
        var f = ValueExpression.Parse("choose(sig, brg, 90)");
        Assert.Equal(expected, f.Evaluate(n => n == "sig" ? signal : bearing));
    }

    [Theory]
    [InlineData(292, 160, 48)]   // radial 160 → station bearing 340; heading 292 → 48° right of the nose
    [InlineData(0, 0, 180)]      // on the 0 radial heading north: station is behind
    [InlineData(90, 270, 0)]     // on the 270 radial heading east: station straight ahead
    public void Vor_rmi_needle_points_to_the_station(double heading, double radial, double expected)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "repo", "gauges", "dc6", "rmi-vor.json");
        using var renderer = GaugeRenderer.FromFile(path);
        var live = new Dictionary<string, double> { ["heading"] = heading, ["vor1_radial"] = radial, ["vor1_signal"] = 1 };
        Assert.Equal(expected, renderer.ValueOf("vor1_needle", live), 6);
    }

    [Fact]
    public void Controls_are_checked()
    {
        var gauge = GaugeDefinition.Parse("""
            { "name": "C", "controls": {
                "rightKnobClockwise": { "event": "KOHLSMAN_INC" },
                "middleKnob": { "event": "KOHLSMAN_DEC" },
                "button1": { "event": "kohlsman inc" },
                "button2": { "event": "BAROMETRIC", "repeat": 0 } } }
            """);
        var problems = string.Join("\n", gauge.Problems());
        Assert.Contains("'middleKnob' isn't a FIP knob or button", problems);
        Assert.Contains("'button1'", problems);
        Assert.Contains("repeat must be", problems);
        Assert.DoesNotContain("control 'rightKnobClockwise'", problems);
    }

    [Theory]
    [InlineData("KOHLSMAN_INC", true)]
    [InlineData("MyPlane.Knob_Inc", true)]   // an add-on's own event (has a dot)
    [InlineData("#70001", true)]             // numbered third-party event
    [InlineData("#0x11000", true)]
    [InlineData("kohlsman inc", false)]      // spaces
    [InlineData("C:\\Windows\\cmd.exe", false)]
    [InlineData("L:my_var", false)]
    [InlineData("#", false)]
    [InlineData("", false)]
    public void Event_names_allow_add_on_events_but_never_paths_or_commands(string name, bool ok) =>
        Assert.Equal(ok, GaugeDefinition.IsEventName(name));

    [Fact]
    public void Missing_font_falls_back_with_a_warning_instead_of_failing()
    {
        var gauge = GaugeDefinition.Parse("""{ "name": "F", "font": "nowhere/NoSuchFont.ttf", "layers": [ { "type": "text", "text": "Hi", "x": 160, "y": 120 } ] }""");
        using var renderer = new GaugeRenderer(gauge, Path.GetTempPath());
        Assert.Contains("NoSuchFont.ttf", renderer.FontWarning);
        using var bitmap = renderer.Render();
        Assert.Equal(320, bitmap.Width);
    }

    [Fact]
    public void Text_can_show_a_value()
    {
        var gauge = GaugeDefinition.Parse("""
            { "name": "T", "values": { "baro": { "default": 29.92 } },
              "layers": [ { "type": "text", "value": "baro", "format": "0.00", "x": 160, "y": 120, "size": 20 } ] }
            """);
        Assert.Empty(gauge.Problems());
        using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
        using var bitmap = renderer.Render(); // draws without error
        Assert.Equal(320, bitmap.Width);
    }
}
