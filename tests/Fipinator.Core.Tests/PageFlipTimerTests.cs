// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class PageFlipTimerTests
{
    private static readonly DateTime Start = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Off_never_flips()
    {
        var timer = new PageFlipTimer(0, Start);
        Assert.False(timer.On);
        Assert.False(timer.Due(Start.AddHours(1)));
    }

    [Fact]
    public void Flips_every_few_seconds()
    {
        var timer = new PageFlipTimer(5, Start);
        Assert.False(timer.Due(Start.AddSeconds(4.9)));
        Assert.True(timer.Due(Start.AddSeconds(5)));
        Assert.False(timer.Due(Start.AddSeconds(9)));   // counted from the last flip
        Assert.True(timer.Due(Start.AddSeconds(10)));
    }

    [Fact]
    public void Turning_the_knob_holds_the_gauge_then_it_carries_on()
    {
        var timer = new PageFlipTimer(5, Start);
        timer.KnobTurned(Start.AddSeconds(3));
        Assert.False(timer.Due(Start.AddSeconds(20)));
        Assert.False(timer.Due(Start.AddSeconds(32.9)));
        Assert.True(timer.Due(Start.AddSeconds(33)));
        Assert.True(timer.Due(Start.AddSeconds(38)));
    }

    [Theory]
    [InlineData(1, 3)]       // too fast to read: 3 seconds
    [InlineData(-4, 0)]      // nonsense: off
    [InlineData(5000, 600)]  // ten minutes at most
    public void Odd_settings_are_brought_into_range(int asked, int used)
    {
        Assert.Equal(used, new PageFlipTimer(asked, Start).Seconds);
    }

    [Fact]
    public void Older_panel_files_have_auto_flip_off()
    {
        var file = Path.Combine(Path.GetTempPath(), $"fipinator-old-panel-{Guid.NewGuid():N}.json");
        var gauge = Path.Combine(AppContext.BaseDirectory, "repo", "gauges", "classic", "twin", "rpm.json").Replace("\\", "/");
        File.WriteAllText(file, $$"""{ "format": 1, "name": "Old", "fips": [ { "serial": "AAAA1111", "gauge": "{{gauge}}", "leftKnobPages": true } ] }""");
        try { Assert.Equal(0, PanelSettings.Load(file).Fips.Single().AutoFlipSeconds); }
        finally { File.Delete(file); }
    }
}
