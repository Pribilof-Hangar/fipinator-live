// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class PanelSwapTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fipinator swap " + Guid.NewGuid().ToString("N"));
    private static readonly string Gauges = Path.Combine(AppContext.BaseDirectory, "repo", "gauges", "dc6");

    private string MakePanel(PanelLibrary library)
    {
        var path = Path.Combine(library.PanelsFolder, "DC-6 PMDG.json");
        new PanelSettings
        {
            Name = "DC-6 PMDG",
            Fips =
            [
                new FipAssignment { Serial = "AAAA1111", Gauge = Path.Combine(Gauges, "airspeed.json") },
                new FipAssignment { Serial = "BBBB2222", Gauge = Path.Combine(Gauges, "dme-gps.json") },
            ],
        }.Save(path);
        return path;
    }

    [Fact]
    public void Left_knob_paging_is_saved_off_by_default_and_moves_with_the_gauges_on_a_swap()
    {
        var library = new PanelLibrary(_folder);
        var path = Path.Combine(library.PanelsFolder, "Jet.json");
        new PanelSettings
        {
            Name = "Jet",
            Fips =
            [
                new FipAssignment { Serial = "AAAA1111", Gauge = Path.Combine(Gauges, "airspeed.json") },
                new FipAssignment { Serial = "BBBB2222", Gauge = Path.Combine(Gauges, "dme-gps.json"), Pages = [Path.Combine(Gauges, "altimeter.json")], LeftKnobPages = true, AutoFlipSeconds = 5 },
            ],
        }.Save(path);
        Assert.Contains("\"leftKnobPages\": true", File.ReadAllText(path));
        var loaded = PanelSettings.Load(path);
        Assert.False(loaded.Fips.Single(f => f.Serial == "AAAA1111").LeftKnobPages);
        Assert.True(loaded.Fips.Single(f => f.Serial == "BBBB2222").LeftKnobPages);
        Assert.Equal(5, loaded.Fips.Single(f => f.Serial == "BBBB2222").AutoFlipSeconds);
        Assert.Equal(0, loaded.Fips.Single(f => f.Serial == "AAAA1111").AutoFlipSeconds);

        library.Swap(path, "AAAA1111", "BBBB2222");
        var swapped = PanelSettings.Load(path);
        Assert.True(swapped.Fips.Single(f => f.Serial == "AAAA1111").LeftKnobPages);
        Assert.False(swapped.Fips.Single(f => f.Serial == "BBBB2222").LeftKnobPages);
        Assert.Equal(5, swapped.Fips.Single(f => f.Serial == "AAAA1111").AutoFlipSeconds); // auto-flip moves with the gauges too
        Assert.Equal(0, swapped.Fips.Single(f => f.Serial == "BBBB2222").AutoFlipSeconds);
    }

    [Fact]
    public void Swap_trades_gauges_and_reset_puts_them_back()
    {
        var library = new PanelLibrary(_folder);
        var path = MakePanel(library);
        Assert.False(library.HasDefault(path));

        library.Swap(path, "AAAA1111", "BBBB2222");
        var swapped = PanelSettings.Load(path);
        Assert.EndsWith("dme-gps.json", swapped.Fips.Single(f => f.Serial == "AAAA1111").Gauge);
        Assert.EndsWith("airspeed.json", swapped.Fips.Single(f => f.Serial == "BBBB2222").Gauge);
        Assert.True(library.HasDefault(path));

        library.Swap(path, "AAAA1111", "BBBB2222"); // second change keeps the first original
        library.Swap(path, "AAAA1111", "BBBB2222");

        library.ResetToDefault(path);
        var reset = PanelSettings.Load(path);
        Assert.EndsWith("airspeed.json", reset.Fips.Single(f => f.Serial == "AAAA1111").Gauge);
        Assert.False(library.HasDefault(path));
        Assert.Single(library.List()); // the kept copy never shows up as a panel
    }

    [Fact]
    public void Swapping_a_FIP_that_isnt_in_the_panel_is_refused()
    {
        var library = new PanelLibrary(_folder);
        var path = MakePanel(library);
        Assert.Throws<InvalidDataException>(() => library.Swap(path, "AAAA1111", "ZZZZ9999"));
        Assert.False(library.HasDefault(path)); // nothing changed, nothing kept
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (DirectoryNotFoundException) { }
    }
}
