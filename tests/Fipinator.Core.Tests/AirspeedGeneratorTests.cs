// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class AirspeedGeneratorTests
{
    // Speeds the sim reported on the owner's PC (2026-11-07).
    private static readonly AircraftSpeeds Titan = new(70, 83, 163, 241);
    private static readonly AircraftSpeeds Dc3 = new(58, 67, 184, 228);

    [Theory]
    [MemberData(nameof(Aircraft))]
    public void Generated_gauge_is_valid_and_covers_never_exceed(AircraftSpeeds speeds)
    {
        var gauge = GaugeDefinition.Parse(AirspeedGenerator.Create(speeds, "Test"));
        Assert.Empty(gauge.Problems());
        var scale = gauge.Scale("kt");
        Assert.True(scale.High > speeds.NeverExceed);
        Assert.True(scale.Low < speeds.StallClean);
        using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
        using var bitmap = renderer.Render();
        Assert.Equal(320, bitmap.Width);
    }

    public static TheoryData<AircraftSpeeds> Aircraft() => [Titan, Dc3, new AircraftSpeeds(40, 48, 129, 163, 85)];

    [Fact]
    public void Arcs_follow_the_aircraft_speeds()
    {
        var gauge = GaugeDefinition.Parse(AirspeedGenerator.Create(Titan, "Titan"));
        var arcs = gauge.Layers.OfType<ArcLayer>().ToList();
        Assert.Contains(arcs, a => a.From == 83 && a.To == 163 && a.Color == "#3E9E4A");   // green
        Assert.Contains(arcs, a => a.From == 163 && a.To == 241 && a.Color == "#E0B13C");  // yellow
        Assert.DoesNotContain(arcs, a => a.Color == "#E8E5DC");                            // no flap speed known: no white arc
        Assert.Contains(gauge.Layers.OfType<TicksLayer>(), t => t.At is [241] && t.Color == "#D3362B"); // red line
    }

    [Fact]
    public void White_arc_appears_when_the_flap_speed_is_known()
    {
        var gauge = GaugeDefinition.Parse(AirspeedGenerator.Create(new AircraftSpeeds(40, 48, 129, 163, 85), "172"));
        Assert.Contains(gauge.Layers.OfType<ArcLayer>(), a => a.From == 40 && a.To == 85 && a.Color == "#E8E5DC");
    }

    [Fact]
    public void Twin_lines_appear_only_when_given()
    {
        var single = GaugeDefinition.Parse(AirspeedGenerator.Create(Titan, "Single"));
        Assert.DoesNotContain(single.Layers.OfType<TicksLayer>(), t => t.Color == AirspeedGenerator.Blue);

        var twin = GaugeDefinition.Parse(AirspeedGenerator.Create(Titan with { MinControl = 83, BestSingleEngineClimb = 105 }, "Twin"));
        Assert.Contains(twin.Layers.OfType<TicksLayer>(), t => t.At is [83] && t.Color == AirspeedGenerator.Red);
        Assert.Contains(twin.Layers.OfType<TicksLayer>(), t => t.At is [105] && t.Color == AirspeedGenerator.Blue);
    }

    [Fact]
    public void Scale_starts_at_the_top_and_mph_ring_lines_up_with_knots()
    {
        var gauge = GaugeDefinition.Parse(AirspeedGenerator.Create(Titan, "Titan"));
        Assert.Equal(0, gauge.Scale("kt").AngleFor(0), 3);
        Assert.Equal(gauge.Scale("kt").AngleFor(100), gauge.Scale("mph").AngleFor(115.078), 3);
    }

    [Fact]
    public void Missing_speeds_are_refused_plainly()
    {
        var ex = Assert.Throws<ArgumentException>(() => AirspeedGenerator.Create(new AircraftSpeeds(0, 0, 0, 0), "x"));
        Assert.Contains("never-exceed", ex.Message);
    }
}
