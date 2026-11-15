// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class JetPackTests
{
    [Theory]
    [InlineData(JetPack.Look.Classic, 1)]
    [InlineData(JetPack.Look.Classic, 2)]
    [InlineData(JetPack.Look.Classic, 3)]
    [InlineData(JetPack.Look.Modern, 1)]
    [InlineData(JetPack.Look.Modern, 2)]
    [InlineData(JetPack.Look.Modern, 3)]
    [InlineData(JetPack.Look.Modern, 4)]
    public void Every_jet_gauge_is_valid_and_draws(JetPack.Look look, int engines)
    {
        var gauges = JetPack.Create(JetPack.Boeing737Max with { Engines = engines }, look);
        Assert.Equal(["n1", "egt", "n2", "fuel-flow", "oil"], gauges.Select(g => g.File));
        foreach (var (file, json) in gauges)
        {
            var gauge = GaugeDefinition.Parse(json);
            Assert.True(gauge.Problems().Count == 0, $"{look} {engines} {file}: {string.Join("; ", gauge.Problems())}");
            using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
            using var bitmap = renderer.Render();
            Assert.Equal(320, bitmap.Width);
        }
    }

    [Fact]
    public void The_737_MAX_reads_corrected_N1_and_N2_like_its_own_display()
    {
        foreach (var look in new[] { JetPack.Look.Classic, JetPack.Look.Modern })
        {
            var gauges = JetPack.Create(JetPack.Boeing737Max, look).ToDictionary(g => g.File, g => GaugeDefinition.Parse(g.Json));
            Assert.Equal("TURB ENG CORRECTED N1:1", gauges["n1"].Values["e1"].Sim);
            Assert.Equal("TURB ENG CORRECTED N2:2", gauges["n2"].Values["e2"].Sim);
        }
        Assert.Equal("TURB ENG N1", (JetPack.Boeing737Max with { CorrectedSpeeds = false }).N1Sim);
    }

    [Fact]
    public void Modern_readout_turns_red_past_the_red_line()
    {
        var gauge = GaugeDefinition.Parse(JetPack.Create(JetPack.Boeing737Max, JetPack.Look.Modern)[0].Json);
        using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
        // 0 = shown, 400 = moved off screen.
        Assert.Equal(0, renderer.ValueOf("e1_ok", new Dictionary<string, double> { ["e1"] = 95 }));
        Assert.Equal(400, renderer.ValueOf("e1_hot", new Dictionary<string, double> { ["e1"] = 95 }));
        Assert.Equal(0, renderer.ValueOf("e1_hot", new Dictionary<string, double> { ["e1"] = 105 }));
    }

    [Fact]
    public void Standby_display_is_valid_and_its_tapes_roll_with_the_values()
    {
        var gauge = GaugeDefinition.Parse(JetFlight.Standby("Standby"));
        Assert.Empty(gauge.Problems());
        using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
        using var bitmap = renderer.Render();
        Assert.Equal(320, bitmap.Width);
        var live = new Dictionary<string, double> { ["ias"] = 152, ["alt"] = 2340 };
        Assert.Equal(140, renderer.ValueOf("spd_base", live));   // labels 80..200 around the readout
        Assert.Equal(12, renderer.ValueOf("spd_roll", live));
        Assert.Equal(2300, renderer.ValueOf("alt_base", live));
        Assert.Equal(2400, renderer.ValueOf("alt_base_4", live)); // the label one step above
        Assert.Equal(400, renderer.ValueOf("spd_base_0_hide", new Dictionary<string, double> { ["ias"] = 10 })); // no "-60" kt
        Assert.Equal("KOHLSMAN_INC", gauge.Controls["rightKnobClockwise"].Event);   // BARO knob
        // Flight director: bar above the symbol when the command is above the attitude; hidden when off.
        Assert.Equal(-16, renderer.ValueOf("fd_y", new Dictionary<string, double> { ["fdp"] = 6, ["pitch"] = 2 }), 6);
        Assert.Equal(400, renderer.ValueOf("fd_hide", new Dictionary<string, double> { ["fdon"] = 0 }), 6);
        Assert.Equal(0, renderer.ValueOf("fd_hide", new Dictionary<string, double> { ["fdon"] = 1 }), 6);
    }

    [Theory]
    [InlineData(GaugeLook.Black)]
    [InlineData(GaugeLook.Classic)]
    [InlineData(GaugeLook.Boeing727)]
    [InlineData(GaugeLook.Boeing737Classic)]
    public void Jet_altimeter_and_vertical_speed_are_valid_in_every_look(GaugeLook look)
    {
        foreach (var json in new[] { JetInstruments.Altimeter("Alt", look), JetInstruments.VerticalSpeed("VSI", look) })
        {
            var gauge = GaugeDefinition.Parse(json);
            Assert.Empty(gauge.Problems());
            using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
            using var bitmap = renderer.Render();
            Assert.Equal(320, bitmap.Width);
        }
        var alt = GaugeDefinition.Parse(JetInstruments.Altimeter("Alt", look));
        using var r = new GaugeRenderer(alt, AppContext.BaseDirectory);
        Assert.Equal(60, r.ValueOf("hundreds", new Dictionary<string, double> { ["alt"] = 35060 }), 6);
        Assert.Equal(940, r.ValueOf("hundreds", new Dictionary<string, double> { ["alt"] = -60 }), 6);  // below sea level
        Assert.Equal("KOHLSMAN_INC", alt.Controls["rightKnobClockwise"].Event);
    }

    [Fact]
    public void The_727_set_is_EPR_N1_EGT_N2_fuel_flow_with_three_numbered_needles()
    {
        var set = JetPack.Create(JetPack.Boeing727, JetPack.Look.Classic);
        Assert.Equal(["epr", "n1", "egt", "n2", "fuel-flow"], set.Select(g => g.File));
        foreach (var (file, json) in set)
        {
            var gauge = GaugeDefinition.Parse(json);
            Assert.Empty(gauge.Problems());
            var needles = gauge.Layers.OfType<NeedleLayer>().ToList();
            Assert.Equal(["1", "2", "3"], needles.Select(n => n.Tag));
        }
        var epr = GaugeDefinition.Parse(set[0].Json);
        Assert.Equal("TURB ENG PRESSURE RATIO:3", epr.Values["e3"].Sim);
        Assert.Equal("TURB ENG N1:1", GaugeDefinition.Parse(set[1].Json).Values["e1"].Sim);   // plain N1, like the 727's own gauge
    }

    [Theory]
    [InlineData(GaugeLook.Black)]
    [InlineData(GaugeLook.Boeing727)]
    [InlineData(GaugeLook.Boeing737Classic)]
    public void Airliner_adi_and_hsi_with_windows_are_valid(GaugeLook look)
    {
        foreach (var json in new[] { JetInstruments.Adi("ADI", look), JetInstruments.HsiWithWindows("HSI", look) })
        {
            var gauge = GaugeDefinition.Parse(json);
            Assert.Empty(gauge.Problems());
            using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
            using var bitmap = renderer.Render();
            Assert.Equal(320, bitmap.Width);
        }
        using var adi = new GaugeRenderer(GaugeDefinition.Parse(JetInstruments.Adi("ADI", look)), AppContext.BaseDirectory);
        Assert.Equal(0, adi.ValueOf("rise", new Dictionary<string, double> { ["ra"] = 900 }), 6);     // runway stays down high up
        Assert.Equal(-30, adi.ValueOf("rise", new Dictionary<string, double> { ["ra"] = 0 }), 6);     // fully risen at touchdown
        Assert.Equal(400, adi.ValueOf("gs_hide", new Dictionary<string, double> { ["hasgs"] = 0 }), 6); // no glideslope, no pointer
    }

    [Fact]
    public void Jet_fuel_page_shows_pounds_per_tank_and_total()
    {
        var gauge = GaugeDefinition.Parse(JetPack.Fuel("Fuel"));
        Assert.Empty(gauge.Problems());
        using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
        using var bitmap = renderer.Render();
        var live = new Dictionary<string, double> { ["wpg"] = 6.7, ["l_gal"] = 1000, ["c_gal"] = 0, ["r_gal"] = 500, ["l_cap"] = 1288 };
        Assert.Equal(6700, renderer.ValueOf("l_lb", live), 6);
        Assert.Equal(10050, renderer.ValueOf("total", live), 6);
        Assert.Equal(1000 / 1288.0, renderer.ValueOf("l_full", live), 6);
    }

    [Fact]
    public void Mini_engine_panels_are_valid_and_read_the_same_values_as_the_big_gauges()
    {
        var max = GaugeDefinition.Parse(MiniPanels.JetEngines(JetPack.Boeing737Max));
        var b727 = GaugeDefinition.Parse(MiniPanels.JetEngines(JetPack.Boeing727));
        var dc6 = GaugeDefinition.Parse(MiniPanels.Dc6Engines());
        foreach (var g in new[] { max, b727, dc6, GaugeDefinition.Parse(MiniPanels.JetEngines(JetPack.Boeing737Max with { Engines = 4 })) })
        {
            Assert.Empty(g.Problems());
            using var r = new GaugeRenderer(g, AppContext.BaseDirectory);
            using var bitmap = r.Render();
        }
        Assert.Equal("TURB ENG CORRECTED N1:2", max.Values["n12"].Sim);          // like the MAX's own display
        Assert.Equal("TURB ENG PRESSURE RATIO:3", b727.Values["epr3"].Sim);      // EPR on top for the 727
        Assert.Equal("L:dc6_340_obj", dc6.Values["rpm4"].Sim);                   // same PMDG value as the big RPM gauge
        using var dc = new GaugeRenderer(dc6, AppContext.BaseDirectory);
        Assert.Equal(2300, dc.ValueOf("rpm1_x", new Dictionary<string, double> { ["rpm1"] = 230 }), 6);
        Assert.Equal(34.0, dc.ValueOf("mp1_x", new Dictionary<string, double> { ["mp1"] = 340 }), 6);
    }

    [Fact]
    public void Arc_can_follow_a_live_value()
    {
        var gauge = GaugeDefinition.Parse("""
            { "name": "Sweep", "values": { "v": { "default": 50 } },
              "scales": { "s": { "min": 0, "max": 100, "startAngle": -90, "endAngle": 90 } },
              "layers": [ { "type": "arc", "scale": "s", "from": 0, "toValue": "v", "radius": 40, "width": 80, "color": "#FFFFFF" } ] }
            """);
        Assert.Empty(gauge.Problems());
        using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
        using var half = renderer.Render(new Dictionary<string, double> { ["v"] = 50 });
        using var full = renderer.Render(new Dictionary<string, double> { ["v"] = 100 });
        // A point at 2 o'clock is outside the sweep at 50 (which ends at 12) and inside it at 100.
        Assert.NotEqual(half.GetPixel(200, 100), full.GetPixel(200, 100));

        var broken = GaugeDefinition.Parse("""
            { "name": "B", "scales": { "s": { "min": 0, "max": 1 } },
              "layers": [ { "type": "arc", "scale": "s", "from": 0, "toValue": "nope", "radius": 40 } ] }
            """);
        Assert.Contains(broken.Problems(), p => p.Contains("'nope'"));
    }
}
