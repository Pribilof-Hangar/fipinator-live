// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class PanelSettingsTests : IDisposable
{
    // A folder with a space and a non-ASCII character, as user folders often have.
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fipinator tests ø " + Guid.NewGuid().ToString("N"));
    private static readonly string Airspeed = Path.Combine(AppContext.BaseDirectory, "repo", "gauges", "dc6", "airspeed.json");

    [Fact]
    public void Saves_and_loads_again()
    {
        var path = Path.Combine(_folder, "sub", "panel.json"); // folder doesn't exist yet
        var settings = new PanelSettings { Fips = [new FipAssignment { Serial = "AB12345678", Gauge = Airspeed }] };
        settings.Save(path);

        var loaded = PanelSettings.Load(path);
        Assert.Equal("AB12345678", Assert.Single(loaded.Fips).Serial);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Desk_positions_are_kept_and_optional()
    {
        var path = Path.Combine(_folder, "desk.json");
        new PanelSettings { Fips = [new FipAssignment { Serial = "A", Gauge = Airspeed, DeskX = 200, DeskY = 30 }, new FipAssignment { Serial = "B", Gauge = Airspeed }] }.Save(path);

        var loaded = PanelSettings.Load(path);
        Assert.Equal((200, 30), (loaded.Fips[0].DeskX, loaded.Fips[0].DeskY));
        Assert.Null(loaded.Fips[1].DeskX); // panels made before the desk view have no positions
    }

    [Fact]
    public void Layouts_save_load_and_fall_back_to_the_main_layout()
    {
        var path = Path.Combine(_folder, "layouts.json");
        var vsi = Path.Combine(AppContext.BaseDirectory, "repo", "gauges", "dc6", "vertical-speed.json");
        new PanelSettings
        {
            LayoutName = "Nav",
            Fips = [new FipAssignment { Serial = "A", Gauge = Airspeed }, new FipAssignment { Serial = "B", Gauge = Airspeed }],
            Layouts = [new PanelLayout { Name = "Engines", Fips = [new FipAssignment { Serial = "A", Gauge = vsi }] }],
            LayoutButton = new LayoutButton { Serial = "B", Button = "button6" },
        }.Save(path);

        var loaded = PanelSettings.Load(path);
        Assert.Equal(["Nav", "Engines"], loaded.AllLayouts().Select(l => l.Name));
        Assert.Equal(vsi, loaded.FipIn(1, "A").Gauge);
        Assert.Equal(Airspeed, loaded.FipIn(1, "B").Gauge); // left out of "Engines": keeps its main gauge
        Assert.Equal("B", loaded.LayoutButton!.Serial);
    }

    [Fact]
    public void Layout_mistakes_are_listed()
    {
        var settings = new PanelSettings
        {
            Fips = [new FipAssignment { Serial = "A", Gauge = Airspeed }],
            Layouts = [new PanelLayout { Name = "Engines", Fips = [new FipAssignment { Serial = "Z", Gauge = Airspeed }] }],
            LayoutButton = new LayoutButton { Serial = "A", Button = "button9" },
        };
        var problems = settings.Problems(_folder);
        Assert.Contains(problems, p => p.Contains("isn't in the main layout"));
        Assert.Contains(problems, p => p.Contains("button9"));
    }

    [Fact]
    public void Relative_gauge_paths_are_relative_to_the_settings_file()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "gauges"));
        File.Copy(Airspeed, Path.Combine(_folder, "gauges", "airspeed.json"));
        var fip = new FipAssignment { Serial = "A", Gauge = "gauges/airspeed.json" };
        Assert.Equal(Path.Combine(_folder, "gauges", "airspeed.json"), PanelSettings.GaugePath(fip, _folder));
    }

    [Fact]
    public void Mistakes_are_listed_plainly()
    {
        var settings = new PanelSettings
        {
            Fips =
            [
                new FipAssignment { Serial = "ABC", Gauge = Airspeed },
                new FipAssignment { Serial = "abc", Gauge = Airspeed },
                new FipAssignment { Serial = "DEF", Gauge = "nowhere.json" },
            ],
        };
        var problems = string.Join("\n", settings.Problems(_folder));
        Assert.Contains("listed 2 times", problems);
        Assert.Contains("gauge file not found", problems);
    }

    [Fact]
    public void Broken_file_gives_a_clear_error()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "panel.json");
        File.WriteAllText(path, "{ this is not json");
        var ex = Assert.Throws<InvalidDataException>(() => PanelSettings.Load(path));
        Assert.Contains("can't be read", ex.Message);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (DirectoryNotFoundException) { }
    }
}
