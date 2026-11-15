// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class PanelLibraryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fipinator library " + Guid.NewGuid().ToString("N"));
    private static readonly string Airspeed = Path.Combine(AppContext.BaseDirectory, "repo", "gauges", "dc6", "airspeed.json");

    [Fact]
    public void Old_single_panel_becomes_DC6_PMDG_and_the_original_is_kept()
    {
        Directory.CreateDirectory(_folder);
        var legacy = Path.Combine(_folder, "panel.json");
        new PanelSettings { Fips = [new FipAssignment { Serial = "A", Gauge = Airspeed }] }.Save(legacy);

        var library = new PanelLibrary(_folder);
        var panels = library.List();

        var only = Assert.Single(panels);
        Assert.Equal("DC-6 PMDG", only.Name);
        Assert.Equal("A", PanelSettings.Load(only.Path).Fips[0].Serial);
        Assert.True(File.Exists(legacy)); // original left as a backup
        Assert.Equal(only.Path, library.ActivePath());
    }

    [Fact]
    public void Remembers_the_chosen_panel()
    {
        var library = new PanelLibrary(_folder);
        var dc6 = Path.Combine(library.PanelsFolder, "dc6.json");
        var kingAir = Path.Combine(library.PanelsFolder, "kingair.json");
        new PanelSettings { Name = "DC-6 PMDG", Fips = [new FipAssignment { Serial = "A", Gauge = Airspeed }] }.Save(dc6);
        new PanelSettings { Name = "King Air", Fips = [new FipAssignment { Serial = "A", Gauge = Airspeed }] }.Save(kingAir);

        Assert.Equal(["DC-6 PMDG", "King Air"], library.List().Select(p => p.Name));
        library.SetActive(kingAir);
        Assert.Equal(kingAir, new PanelLibrary(_folder).ActivePath());

        File.Delete(kingAir); // chosen panel removed: fall back to the first one
        Assert.Equal(dc6, new PanelLibrary(_folder).ActivePath());
    }

    [Fact]
    public void Panels_are_copied_from_the_old_AppData_folder_and_the_old_ones_kept()
    {
        var old = Path.Combine(_folder, "old appdata");
        var oldPanel = Path.Combine(old, "panels", "DC-6 PMDG.json");
        new PanelSettings { Name = "DC-6 PMDG", Fips = [new FipAssignment { Serial = "A", Gauge = Airspeed }] }.Save(oldPanel);
        File.WriteAllText(Path.Combine(old, "app-state.json"), """{"Panel":"DC-6 PMDG.json"}""");

        var library = new PanelLibrary(Path.Combine(_folder, "documents"), importFrom: old);
        var only = Assert.Single(library.List());

        Assert.Equal("DC-6 PMDG", only.Name);
        Assert.StartsWith(Path.Combine(_folder, "documents"), only.Path);
        Assert.True(File.Exists(oldPanel)); // old copy left alone
        Assert.Equal(only.Path, library.ActivePath());
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (DirectoryNotFoundException) { }
    }
}
