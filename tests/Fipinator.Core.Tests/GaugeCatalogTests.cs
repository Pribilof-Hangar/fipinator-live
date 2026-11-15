// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class GaugeCatalogTests
{
    private static string Gauges => Path.Combine(AppContext.BaseDirectory, "repo", "gauges");

    [Fact]
    public void Lists_shipped_gauges_in_friendly_groups_without_the_parts()
    {
        var all = GaugeCatalog.All(Gauges, Path.Combine(Path.GetTempPath(), "fipinator-no-such-folder"));
        Assert.Contains(all, g => g.Group == "Classic twin" && g.Name.Contains("RPM"));
        Assert.Contains(all, g => g.Group == "Classic radios and nav");
        Assert.Contains(all, g => g.Group == "DC-6" && g.Name == "DC-6 RPM 1-4");
        Assert.Contains(all, g => g.Group == "DC-6" && g.Name == "DC-6 Airspeed");
        Assert.DoesNotContain(all, g => g.Path.Contains($"{Path.DirectorySeparatorChar}parts{Path.DirectorySeparatorChar}"));
        Assert.DoesNotContain(all, g => g.Name.EndsWith("1-2", StringComparison.Ordinal));
    }

    [Fact]
    public void Own_gauges_come_first_and_broken_files_are_skipped()
    {
        var mine = Directory.CreateTempSubdirectory("fipinator-mine").FullName;
        try
        {
            File.Copy(Path.Combine(Gauges, "classic", "single", "rpm.json"), Path.Combine(mine, "my-rpm.json"));
            File.WriteAllText(Path.Combine(mine, "broken.json"), "{ not a gauge");
            var all = GaugeCatalog.All(Gauges, mine);
            Assert.Equal("My gauges", all[0].Group);
            Assert.Single(all, g => g.Group == "My gauges");
        }
        finally { Directory.Delete(mine, recursive: true); }
    }

    [Theory]
    [InlineData("classic\\twin", "Classic twin")]
    [InlineData("turboprop\\saab-340b", "Dual turboprop")]
    [InlineData("turboprop\\caravan", "Single turboprop")]
    [InlineData("jet\\737-max", "Dual jet")]
    [InlineData("jet\\737-max-round", "Dual jet (round gauges)")]
    [InlineData("jet\\727", "Triple jet (Boeing 727)")]
    [InlineData("templates\\gear-flaps", "Gear, flaps and trim")]
    [InlineData("templates\\autopilot-radios", "Autopilot and radios")]
    [InlineData("templates\\boeing-style", "Boeing style")]
    [InlineData("templates\\airbus-style", "Airbus style")]
    [InlineData("dc6", "DC-6")]
    [InlineData("dc6\\overhead", "DC-6")]
    public void Group_names_read_well(string folder, string expected) =>
        Assert.Equal(expected, GaugeCatalog.GroupName(folder.Replace('\\', Path.DirectorySeparatorChar)));
}
