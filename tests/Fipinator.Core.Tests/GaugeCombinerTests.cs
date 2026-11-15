// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class GaugeCombinerTests
{
    private static string Repo(string relative) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "repo", "gauges", relative));

    [Fact]
    public void Two_twin_needle_dials_become_one_four_needle_dial()
    {
        var json = GaugeCombiner.MoreNeedles([Repo("dc6/parts/engines/rpm-1-2.json"), Repo("dc6/parts/engines/rpm-3-4.json")], "RPM 1-4");
        var gauge = GaugeDefinition.Parse(json);
        Assert.Empty(gauge.Problems());

        var needles = gauge.Layers.OfType<NeedleLayer>().ToList();
        Assert.Equal(["1", "2", "3", "4"], needles.Select(n => n.Tag));
        // each needle still reads its own engine
        Assert.Equal(4, needles.Select(n => gauge.Values[n.Value].Sim).Distinct().Count());
        // staggered so the number discs don't sit on top of each other
        Assert.Equal(4, needles.Select(n => n.Length).Distinct().Count());

        using var renderer = new GaugeRenderer(gauge, Path.Combine(AppContext.BaseDirectory, "repo", "gauges", "dc6", "parts", "engines"));
        using var bitmap = renderer.Render();
        Assert.Equal(320, bitmap.Width);
    }

    [Fact]
    public void Single_needle_gauges_get_numbered_in_order()
    {
        var json = GaugeCombiner.MoreNeedles(
            [Repo("dc6/parts/engines/bmep-1.json"), Repo("dc6/parts/engines/bmep-2.json"), Repo("dc6/parts/engines/bmep-3.json"), Repo("dc6/parts/engines/bmep-4.json")], "BMEP 1-4");
        var gauge = GaugeDefinition.Parse(json);
        Assert.Empty(gauge.Problems());
        var needles = gauge.Layers.OfType<NeedleLayer>().ToList();
        Assert.Equal(["1", "2", "3", "4"], needles.Select(n => n.Tag));
        Assert.Equal(["L:dc6_317_obj", "L:dc6_318_obj", "L:dc6_319_obj", "L:dc6_320_obj"], needles.Select(n => gauge.Values[n.Value].Sim));
    }

    [Fact]
    public void Different_dials_are_refused_with_a_hint()
    {
        var ex = Assert.Throws<ArgumentException>(() => GaugeCombiner.MoreNeedles([Repo("dc6/airspeed.json"), Repo("dc6/vertical-speed.json")], "x"));
        Assert.Contains("side by side", ex.Message);
    }

    [Fact]
    public void Side_by_side_keeps_both_gauges_working_without_name_clashes()
    {
        var json = GaugeCombiner.SideBySide(Repo("dc6/parts/engines/oil-temperature-1-2.json"), Repo("dc6/parts/engines/oil-temperature-3-4.json"), "Oil temp 1-4");
        var gauge = GaugeDefinition.Parse(json);
        Assert.Empty(gauge.Problems());
        Assert.Equal(4, gauge.SimValues().Count);
        Assert.Contains("left_n1", gauge.Values.Keys);
        Assert.Contains("right_n1", gauge.Values.Keys);

        using var renderer = new GaugeRenderer(gauge, Path.Combine(AppContext.BaseDirectory, "repo", "gauges", "dc6", "parts", "engines"));
        using var bitmap = renderer.Render();
        // something is drawn in both halves (dark dial faces on the light panel)
        Assert.True(bitmap.GetPixel(30, 170).Red < 80);
        Assert.True(bitmap.GetPixel(190, 170).Red < 80);
        Assert.True(bitmap.GetPixel(160, 20).Red > 100); // light panel between and above them
    }

    [Fact]
    public void Side_by_side_renames_values_inside_formulas()
    {
        var withFormula = """
            { "name": "F", "values": { "a": { "default": 1 }, "b": { "expr": "a * 2" } },
              "scales": { "s": { "min": 0, "max": 10, "startAngle": 0, "endAngle": 90 } },
              "layers": [ { "type": "needle", "scale": "s", "value": "b", "length": 50 } ] }
            """;
        var gauge = GaugeDefinition.Parse(GaugeCombiner.SideBySide(withFormula, withFormula, "FF"));
        Assert.Empty(gauge.Problems());
        Assert.Equal("left_a * 2", gauge.Values["left_b"].Expr);
    }
}

