// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

/// <summary>Scales whose line isn't a circle around the pivot (the DC-6's twin pressure dials).</summary>
public class BentScaleTests
{
    private static ScaleSpec Bent => new() { Points = [[0, 150, 60], [100, 90, 40], [200, 30, 60]] };

    [Fact]
    public void Distance_is_filled_in_between_points_and_held_at_the_ends()
    {
        Assert.True(Bent.IsBent);
        Assert.Equal(50, Bent.DistanceFor(50), 3);
        Assert.Equal(40, Bent.DistanceFor(100), 3);
        Assert.Equal(60, Bent.DistanceFor(-10), 3);
        Assert.Equal(60, Bent.DistanceFor(999), 3);
        Assert.Equal(120, Bent.AngleFor(50), 3); // angles work as before
    }

    [Fact]
    public void Layers_on_a_bent_scale_measure_from_the_scale_line()
    {
        Assert.Equal(45, Bent.Reach(100, 5), 3);  // 5 px outside the line at distance 40
        Assert.Equal(52, Bent.Reach(0, -8), 3);   // 8 px inside the line at distance 60
        var round = new ScaleSpec { Points = [[0, 0], [100, 90]] };
        Assert.False(round.IsBent);
        Assert.Equal(80, round.Reach(50, 80), 3); // ordinary scale: radius from the pivot, unchanged
    }

    [Fact]
    public void Mixed_point_kinds_are_reported_plainly()
    {
        var mixed = new ScaleSpec { Points = [[0, 0, 50], [100, 90]] };
        Assert.Contains("don't mix", mixed.Problem());
    }

    [Fact]
    public void Pointer_needle_reaches_the_bent_scale_line()
    {
        // Pivot on the left edge, scale line 60 px to its right at value 50: the needle tip
        // (offset +4) lands about 64 px right of the pivot, and nothing is drawn far beyond it.
        var gauge = GaugeDefinition.Parse("""
            { "name": "Twin", "canvas": { "background": "#000000" },
              "values": { "p": { "default": 50 } },
              "scales": { "s": { "points": [[0, 150, 70], [50, 90, 60], [100, 30, 70]] } },
              "layers": [ { "type": "needle", "style": "pointer", "scale": "s", "value": "p", "center": [60, 120],
                            "length": 4, "width": 3, "tail": 0, "color": "#FFFFFF" } ] }
            """);
        using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
        using var bitmap = renderer.Render();
        Assert.True(bitmap.GetPixel(110, 120).Red > 200);  // on the blade
        Assert.True(bitmap.GetPixel(140, 120).Red < 20);   // well past the tip: empty
    }
}
