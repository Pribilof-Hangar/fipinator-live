// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class TurbopropPackTests
{
    public static TheoryData<string> Planes => ["saab", "caravan"];

    private static TurbopropProfile Plane(string name) => name == "saab" ? TurbopropPack.Saab340B : TurbopropPack.Caravan208B;

    [Theory]
    [MemberData(nameof(Planes))]
    public void Every_turboprop_gauge_is_valid_and_draws(string name)
    {
        foreach (var look in new[] { GaugeLook.Classic, GaugeLook.Black })
            foreach (var (file, json) in TurbopropPack.Create(Plane(name), look))
            {
                var gauge = GaugeDefinition.Parse(json);
                Assert.True(gauge.Problems().Count == 0, $"{name} {file}: {string.Join("; ", gauge.Problems())}");
                using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
                using var bitmap = renderer.Render();
                Assert.Equal(320, bitmap.Width);
            }
    }

    [Fact]
    public void Pack_starts_with_torque()
    {
        Assert.Equal(["torque", "itt", "ng", "prop-rpm", "fuel-flow", "oil"], TurbopropPack.Create(TurbopropPack.Saab340B).Select(g => g.File));
    }

    [Theory]
    [InlineData("saab", 2, "TURB ENG MAX TORQUE PERCENT")]
    [InlineData("caravan", 1, "ENG TORQUE")]
    public void Torque_reads_percent_or_foot_pounds_with_a_needle_per_engine(string name, int engines, string sim)
    {
        var gauge = GaugeDefinition.Parse(TurbopropPack.Torque(Plane(name)));
        var needles = gauge.Layers.OfType<NeedleLayer>().ToList();
        Assert.Equal(engines, needles.Count);
        Assert.Equal(Enumerable.Range(1, engines).Select(e => $"{sim}:{e}"), needles.Select(n => gauge.Values[n.Value].Sim));
    }

    [Fact]
    public void Glass_attitude_and_wide_range_airspeed_are_valid_and_draw()
    {
        foreach (var json in new[] { TurbopropFlight.Airspeed("Airspeed"), TurbopropFlight.Eadi("EADI") })
        {
            var gauge = GaugeDefinition.Parse(json);
            Assert.Empty(gauge.Problems());
            using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
            using var bitmap = renderer.Render();
            Assert.Equal(320, bitmap.Width);
        }
        var eadi = GaugeDefinition.Parse(TurbopropFlight.Eadi("EADI"));
        Assert.Equal("ATTITUDE INDICATOR PITCH DEGREES", eadi.Values["pitch"].Sim);
        Assert.Equal("AIRSPEED BARBER POLE", GaugeDefinition.Parse(TurbopropFlight.Airspeed("A")).Values["vmo"].Sim);
    }

    [Fact]
    public void Kilogram_fuel_flow_is_worked_out_from_the_sims_pounds()
    {
        var gauge = GaugeDefinition.Parse(TurbopropPack.FuelFlow(TurbopropPack.Saab340B));
        Assert.Equal("ENG FUEL FLOW PPH:1", gauge.Values["e1_raw"].Sim);
        Assert.Contains("e1_raw", gauge.Values["e1"].Expr);
    }
}
