// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

/// <summary>Every gauge FIPinator ships says it's free and open source, in its licence line (not in its name).</summary>
public class GaugeLicenseTests
{
    private static string Gauges => Path.Combine(AppContext.BaseDirectory, "repo", "gauges");

    [Fact]
    public void Every_shipped_gauge_carries_the_licence_line()
    {
        var files = Directory.GetFiles(Gauges, "*.json", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        var missing = files.Where(f => GaugeDefinition.Load(f).License != GaugeDefinition.ShippedLicense)
            .Select(f => Path.GetRelativePath(Gauges, f)).ToList();
        Assert.True(missing.Count == 0, "No licence line: " + string.Join(", ", missing));
    }

    [Fact]
    public void The_licence_is_not_in_the_name()
    {
        foreach (var f in Directory.GetFiles(Gauges, "*.json", SearchOption.AllDirectories))
            Assert.DoesNotContain("AGPL", GaugeDefinition.Load(f).Name, StringComparison.OrdinalIgnoreCase);
    }
}
