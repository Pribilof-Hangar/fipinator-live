// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

/// <summary>Panels where a FIP holds several gauges as pages.</summary>
public class PanelPagesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("fipinator-pages").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Gauge(string name)
    {
        var path = Path.Combine(_folder, name + ".json");
        File.WriteAllText(path, $$"""{ "name": "{{name}}", "layers": [] }""");
        return path;
    }

    [Fact]
    public void Pages_come_after_the_main_gauge_and_old_panels_still_load()
    {
        var a = Gauge("a"); var b = Gauge("b"); var c = Gauge("c");
        var panelPath = Path.Combine(_folder, "p.json");
        File.WriteAllText(panelPath, $$"""{ "fips": [ { "serial": "S1", "gauge": "a.json", "pages": [ "b.json", "c.json" ] }, { "serial": "S2", "gauge": "a.json" } ] }""");
        var panel = PanelSettings.Load(panelPath);
        Assert.Equal([a, b, c], PanelSettings.PagePaths(panel.Fips[0], _folder));
        Assert.Equal([a], PanelSettings.PagePaths(panel.Fips[1], _folder)); // no "pages": one gauge, as before
    }

    [Fact]
    public void A_missing_page_file_is_reported_plainly()
    {
        Gauge("a");
        var panel = new PanelSettings { Fips = [new FipAssignment { Serial = "S1", Gauge = "a.json", Pages = ["gone.json"] }] };
        Assert.Contains(panel.Problems(_folder), p => p.Contains("gone.json"));
    }

    [Fact]
    public void Swap_moves_the_pages_with_their_gauge()
    {
        Gauge("a"); Gauge("b"); Gauge("c");
        var library = new PanelLibrary(Path.Combine(_folder, "lib"));
        Directory.CreateDirectory(library.PanelsFolder);
        var path = Path.Combine(library.PanelsFolder, "p.json");
        new PanelSettings
        {
            Fips =
            [
                new FipAssignment { Serial = "S1", Gauge = Path.Combine(_folder, "a.json"), Pages = [Path.Combine(_folder, "c.json")] },
                new FipAssignment { Serial = "S2", Gauge = Path.Combine(_folder, "b.json") },
            ],
        }.Save(path);

        library.Swap(path, "S1", "S2");
        var swapped = PanelSettings.Load(path);
        Assert.Equal("S1", swapped.Fips[0].Serial);
        Assert.EndsWith("b.json", swapped.Fips[0].Gauge);
        Assert.Empty(swapped.Fips[0].Pages);
        Assert.EndsWith("a.json", swapped.Fips[1].Gauge);
        Assert.Single(swapped.Fips[1].Pages);
    }
}
