// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class ClassicPackTests
{
    private static Dictionary<string, string> Sources() => new()
    {
        ["attitude"] = Repo("dc6/attitude-standard.json"),
        ["altimeter"] = Repo("dc6/altimeter.json"),
        ["vertical-speed"] = Repo("dc6/vertical-speed.json"),
    };

    private static string Repo(string relative) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "repo", "gauges", relative));

    private static readonly AircraftProfile Trainer = new("Trainer", 1, false, new AircraftSpeeds(35, 40, 111, 149, 85), 13, 2550, 1900, 2550);
    private static readonly AircraftProfile Twin = new("Twin", 2, true, new AircraftSpeeds(70, 83, 210, 241, 150, 83, 105), 85, 2300, 1800, 2300, 45, 20, 35, 40);

    [Theory]
    [InlineData(GaugeLook.Classic)]
    [InlineData(GaugeLook.Black)]
    [InlineData(GaugeLook.Dc6)]
    [InlineData(GaugeLook.LightTwin)]
    [InlineData(GaugeLook.Boeing727)]
    [InlineData(GaugeLook.Boeing737Classic)]
    public void Every_pack_gauge_is_valid_and_draws(GaugeLook look)
    {
        foreach (var plane in new[] { Trainer, Twin, Twin with { Engines = 4 } })
        {
            foreach (var (file, json) in ClassicPack.Create(plane, look, Sources()))
            {
                var gauge = GaugeDefinition.Parse(json);
                Assert.True(gauge.Problems().Count == 0, $"{plane.Name} {file}: {string.Join("; ", gauge.Problems())}");
                using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
                using var bitmap = renderer.Render();
                Assert.Equal(320, bitmap.Width);
            }
        }
    }

    [Fact]
    public void Pack_is_in_priority_order_and_leaves_out_manifold_pressure_when_there_is_none()
    {
        Assert.Equal(["airspeed", "attitude", "altimeter", "turn-coordinator", "heading", "vertical-speed", "rpm", "oil", "fuel", "turn-and-slip", "hsi",
                "fuel-flow", "egt", "radar-altimeter", "clock", "adf"],
            ClassicPack.Create(Trainer, GaugeLook.Classic, Sources()).Select(g => g.File));
        Assert.Contains("manifold-pressure", ClassicPack.Create(Twin, GaugeLook.Classic, Sources()).Select(g => g.File));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Engine_gauges_get_a_needle_per_engine_reading_that_engine(int engines)
    {
        var rpm = GaugeDefinition.Parse(EngineDialGenerator.Rpm(Twin with { Engines = engines }));
        var needles = rpm.Layers.OfType<NeedleLayer>().ToList();
        Assert.Equal(engines, needles.Count);
        Assert.Equal(Enumerable.Range(1, engines).Select(e => $"GENERAL ENG RPM:{e}"), needles.Select(n => rpm.Values[n.Value].Sim));
        Assert.All(needles, n => Assert.Equal(engines > 1, n.Tag is not null));

        var oil = GaugeDefinition.Parse(OilGenerator.Create(Twin with { Engines = engines }));
        Assert.Equal(engines * 2, oil.Layers.OfType<NeedleLayer>().Count()); // temperature and pressure
    }

    [Theory]
    [InlineData("dc6/course-ils.json")]
    [InlineData("dc6/rmi-vor-adf.json")]
    [InlineData("dc6/rmi-adf.json")]
    [InlineData("dc6/rmi-vor12-adf12.json")]
    [InlineData("dc6/dme-gps.json")]   // a plate with windows, not a round dial
    public void Nav_gauges_reskin_into_the_classic_look_keeping_values_and_knobs(string file)
    {
        var original = GaugeDefinition.Parse(Repo(file));
        var reskinned = GaugeDefinition.Parse(GaugeParts.Reskin(Repo(file), GaugeLook.Classic));
        Assert.Empty(reskinned.Problems());
        Assert.Equal(original.Values.Keys.Order(), reskinned.Values.Keys.Order());
        Assert.Equal(original.Controls.Keys.Order(), reskinned.Controls.Keys.Order());
        Assert.Equal("#8D908E", reskinned.Canvas.Background);
        // words that were outside the dial don't sit on the new screws
        foreach (var t in reskinned.Layers.OfType<TextLayer>())
            Assert.DoesNotContain(reskinned.Layers.OfType<ScrewLayer>(), s => Math.Abs(s.X - t.X) < 8 && Math.Abs(s.Y - t.Y) < 8);
        using var renderer = new GaugeRenderer(reskinned, AppContext.BaseDirectory);
        using var bitmap = renderer.Render();
        Assert.Equal(320, bitmap.Width);
    }

    [Fact]
    public void Reskin_keeps_values_and_knobs_and_swaps_the_case()
    {
        var original = GaugeDefinition.Parse(Repo("dc6/altimeter.json"));
        var reskinned = GaugeDefinition.Parse(GaugeParts.Reskin(Repo("dc6/altimeter.json"), GaugeLook.Classic, "Alt"));
        Assert.Empty(reskinned.Problems());
        Assert.Equal(original.Values.Keys.Order(), reskinned.Values.Keys.Order());
        Assert.Equal(original.Controls.Keys.Order(), reskinned.Controls.Keys.Order());
        Assert.Contains(reskinned.Layers.OfType<ScrewLayer>(), s => s.Kind == "steel");
        Assert.Equal("#8D908E", reskinned.Canvas.Background);
    }
}
