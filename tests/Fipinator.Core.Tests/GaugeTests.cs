// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Text.Json;
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class GaugeTests
{
    private static readonly string Dc6Airspeed = Path.Combine(AppContext.BaseDirectory, "repo", "gauges", "dc6", "airspeed.json");

    [Theory]
    [InlineData(0, 0)]
    [InlineData(150, 165)]
    [InlineData(300, 330)]
    [InlineData(-20, 0)]   // below the scale: needle rests on the stop
    [InlineData(400, 330)] // above the scale: needle rests on the stop
    public void Scale_maps_values_to_angles_and_clamps(double value, float expected)
    {
        var scale = new ScaleSpec { Min = 0, Max = 300, StartAngle = 0, EndAngle = 330 };
        Assert.Equal(expected, scale.AngleFor(value), 3);
    }

    [Fact]
    public void Steps_cover_the_range_including_the_end()
    {
        var scale = new ScaleSpec { Min = 0, Max = 300 };
        var labels = new LabelsLayer { Every = 40, From = 40, To = 280 };
        Assert.Equal([40, 80, 120, 160, 200, 240, 280], labels.Steps(scale));
    }

    [Fact]
    public void Dc6_airspeed_loads_with_all_layer_types_it_uses()
    {
        var gauge = GaugeDefinition.Load(Dc6Airspeed);
        Assert.Equal("DC-6 Airspeed", gauge.Name);
        Assert.Equal("AIRSPEED INDICATED", gauge.Values["ias"].Sim);
        Assert.Contains(gauge.Layers, l => l is NeedleLayer { Value: "ias" });
        Assert.IsType<GlareLayer>(gauge.Layers[^1]);
    }

    [Fact]
    public void Dc6_airspeed_renders_at_fip_size_and_needle_moves()
    {
        using var renderer = GaugeRenderer.FromFile(Dc6Airspeed);
        using var slow = renderer.Render(new Dictionary<string, double> { ["ias"] = 60 });
        using var fast = renderer.Render(new Dictionary<string, double> { ["ias"] = 250 });

        Assert.Equal((320, 240), (slow.Width, slow.Height));
        // A point along the needle at 60 kt: covered by the needle in one frame, not the other.
        var p = GaugeRenderer.Polar(new(160, 120), renderer.Gauge.Scale("speed").AngleFor(60), 50);
        Assert.NotEqual(slow.GetPixel((int)p.X, (int)p.Y), fast.GetPixel((int)p.X, (int)p.Y));
    }

    [Fact]
    public void Sim_values_lists_only_values_from_the_sim()
    {
        var gauge = GaugeDefinition.Parse("""
            { "name": "Test", "values": {
                "vs":  { "sim": "VERTICAL SPEED", "unit": "feet per minute" },
                "ias": { "sim": "AIRSPEED INDICATED", "unit": "knots" },
                "test": { "default": 3 } } }
            """);
        Assert.Equal([("ias", "AIRSPEED INDICATED", "knots"), ("vs", "VERTICAL SPEED", "feet per minute")], gauge.SimValues());
    }

    [Fact]
    public void Sim_value_without_a_unit_gives_a_clear_error()
    {
        var gauge = GaugeDefinition.Parse("""{ "name": "Test", "values": { "ias": { "sim": "AIRSPEED INDICATED" } } }""");
        var ex = Assert.Throws<InvalidOperationException>(() => gauge.SimValues());
        Assert.Contains("ias", ex.Message);
    }

    [Fact]
    public void Unknown_layer_type_is_rejected()
    {
        const string json = """{ "name": "Bad", "layers": [ { "type": "wobbler" } ] }""";
        Assert.ThrowsAny<JsonException>(() => GaugeDefinition.Parse(json));
    }

    [Fact]
    public void Missing_scale_gives_a_clear_error()
    {
        var gauge = GaugeDefinition.Parse("""{ "name": "Test" }""");
        var ex = Assert.Throws<KeyNotFoundException>(() => gauge.Scale("speed"));
        Assert.Contains("speed", ex.Message);
    }
}
