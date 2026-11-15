// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class GaugeKindsTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "repo", "gauges");
    private static GaugeKind Kind(string relative) => GaugeKinds.Of(GaugeDefinition.Load(Path.Combine(Root, relative)));

    [Theory]
    [InlineData("jet/737-max/standby.json", GaugeKind.FlightNav)]       // has a speed tape, but it's a horizon
    [InlineData("jet/727/hsi.json", GaugeKind.FlightNav)]
    [InlineData("dc6/attitude-standard.json", GaugeKind.FlightNav)]
    [InlineData("classic/nav/rmi-vor-adf.json", GaugeKind.FlightNav)]
    [InlineData("classic/nav/rmi-vor12-adf12.json", GaugeKind.FlightNav)]
    [InlineData("classic/twin/radar-altimeter.json", GaugeKind.FlightNav)]
    [InlineData("dc6/airspeed.json", GaugeKind.Airspeed)]
    [InlineData("jet/737-max/airspeed.json", GaugeKind.Airspeed)]
    [InlineData("jet/737-max/n1.json", GaugeKind.PlaneSpecific)]
    [InlineData("dc6/engines/rpm-1-4.json", GaugeKind.PlaneSpecific)]   // PMDG L: values
    [InlineData("classic/twin/fuel.json", GaugeKind.PlaneSpecific)]
    [InlineData("turboprop/saab-340b/torque.json", GaugeKind.PlaneSpecific)]
    public void Gauges_are_sorted_by_what_they_read(string relative, GaugeKind expected) =>
        Assert.Equal(expected, Kind(relative));

    [Fact]
    public void A_DC6_panel_is_offered_any_horizon_but_no_jet_engines_or_other_airspeeds()
    {
        var all = GaugeCatalog.All(Root, Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid().ToString("N")));
        var panel = new[] { "dc6/attitude-standard.json", "dc6/airspeed.json", "dc6/engines/rpm-1-4.json" }.Select(p => Path.Combine(Root, p)).ToList();
        var offered = GaugeKinds.ChoicesFor(panel, all).Select(c => Path.GetRelativePath(Root, c.Choice.Path).Replace('\\', '/')).ToHashSet();

        Assert.Contains("jet/737-max/standby.json", offered);
        Assert.Contains("jet/727/hsi.json", offered);
        Assert.Contains("dc6/engines/bmep-1-4.json", offered);       // the DC-6's own engines
        Assert.Contains("dc6/airspeed.json", offered);               // its own airspeed
        Assert.DoesNotContain("jet/737-max/n1.json", offered);
        Assert.DoesNotContain("jet/727/epr.json", offered);
        Assert.DoesNotContain("turboprop/saab-340b/torque.json", offered);
        Assert.DoesNotContain("jet/737-max/airspeed.json", offered);  // another plane's airspeed
        Assert.Equal(Root, GaugeKinds.LibraryRoot(panel));
    }
}
