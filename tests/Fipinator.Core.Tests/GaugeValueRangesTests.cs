// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class GaugeValueRangesTests
{
    private static GaugeDefinition Load(string file) =>
        GaugeDefinition.Load(Path.Combine(AppContext.BaseDirectory, "repo", "gauges", "dc6", file));

    private static ValueRange Range(GaugeDefinition gauge, string name) =>
        GaugeValueRanges.For(gauge).Single(r => r.Name == name);

    [Fact]
    public void Airspeed_slider_covers_the_whole_dial()
    {
        var ias = Range(Load("airspeed.json"), "ias");
        Assert.Equal((0, 400), (ias.Min, ias.Max));
        Assert.False(ias.IsOnOff);
    }

    [Fact]
    public void Vertical_speed_slider_goes_both_ways()
    {
        var vs = Range(Load("vertical-speed.json"), "vs");
        Assert.Equal((-2000, 2000), (vs.Min, vs.Max));
    }

    [Fact]
    public void On_off_values_become_switches_and_formulas_get_no_slider()
    {
        var gauge = Load("course-ils.json");
        Assert.True(Range(gauge, "has_nav").IsOnOff);
        Assert.DoesNotContain(GaugeValueRanges.For(gauge), r => r.Name == "nav_flag_shift");
    }

    [Fact]
    public void Altimeter_slider_reaches_well_above_its_default()
    {
        var alt = Range(Load("altimeter.json"), "alt");
        Assert.True(alt.Max >= 14700);
        Assert.Equal(0, alt.Min);
    }

    [Fact]
    public void Every_dc6_gauge_gets_usable_ranges()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "repo", "gauges", "dc6");
        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            foreach (var r in GaugeValueRanges.For(GaugeDefinition.Load(file)))
            {
                Assert.True(r.Max > r.Min, $"{Path.GetFileName(file)} {r.Name}");
                Assert.True(r.Step > 0, $"{Path.GetFileName(file)} {r.Name}");
                Assert.InRange(r.Default, r.Min, r.Max);
            }
        }
    }
}
