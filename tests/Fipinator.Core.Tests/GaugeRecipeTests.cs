// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

/// <summary>The Gauge Maker's built-in recipes make valid gauges that draw.</summary>
public class GaugeRecipeTests
{
    public static TheoryData<string> Recipes() =>
    [
        FuelGenerator.Create(26, "Fuel"),
        FuelGenerator.Create(26, "Fuel DC-6", GaugeLook.Dc6),
        TurnSlipGenerator.Create("Turn and slip"),
        TurnSlipGenerator.Create("Turn and slip DC-6", GaugeLook.Dc6),
        FlapsGenerator.Create(50, [15, 20, 30, 50], "Flaps DC-6", GaugeLook.Dc6),
        FlapsGenerator.Create(50, [15, 20, 30, 50], "Flaps"),
        FlapsGenerator.Create(30, [], "Flaps small"),
        TurnAndSlipGenerator.Create("Turn and slip (needle)"),
        TurnAndSlipGenerator.Create("Turn and slip black", GaugeLook.Black),
        HsiGenerator.Create("HSI"),
        HsiGenerator.Create("HSI black", GaugeLook.Black),
    ];

    [Theory]
    [MemberData(nameof(Recipes))]
    public void Recipe_makes_a_valid_gauge_that_draws(string json)
    {
        var gauge = GaugeDefinition.Parse(json);
        Assert.Empty(gauge.Problems());
        using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
        using var bitmap = renderer.Render();
        Assert.Equal(320, bitmap.Width);
    }

    [Fact]
    public void Fuel_scale_matches_the_tank_size()
    {
        var gauge = GaugeDefinition.Parse(FuelGenerator.Create(40, "Fuel"));
        Assert.Equal(40, gauge.Scale("left").High);
        Assert.Equal(40, gauge.Scale("right").High);
    }

    [Fact]
    public void Slip_ball_rolls_right_for_positive_values_and_turn_needle_leans_right()
    {
        var gauge = GaugeDefinition.Parse(TurnSlipGenerator.Create("T"));
        Assert.True(gauge.Scale("ball").AngleFor(127) < 0);   // counter-clockwise swing = ball to the right
        Assert.True(gauge.Scale("turn").AngleFor(0.05) > 0);  // right turn = needle to the right
    }

    [Fact]
    public void Bad_numbers_are_refused_plainly()
    {
        Assert.Throws<ArgumentException>(() => FuelGenerator.Create(0, "x"));
        Assert.Throws<ArgumentException>(() => FlapsGenerator.Create(0, [], "x"));
    }
}
