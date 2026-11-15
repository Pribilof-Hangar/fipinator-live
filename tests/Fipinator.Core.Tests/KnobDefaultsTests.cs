// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class KnobDefaultsTests
{
    private static string Repo(string relative) => Path.Combine(AppContext.BaseDirectory, "repo", "gauges", relative);

    private static Dictionary<string, (string Event, int Repeat)> Knobs(params string[] sims) =>
        KnobDefaults.For(sims).ToDictionary(k => k.Control, k => (k.Event, k.Repeat));

    [Fact]
    public void Each_instrument_gets_the_knobs_a_pilot_would_use()
    {
        var hsi = Knobs("NAV OBS:1", "AUTOPILOT HEADING LOCK DIR", "PLANE HEADING DEGREES MAGNETIC");
        Assert.Equal(("VOR1_OBI_INC", 1), hsi["rightKnobClockwise"]);
        Assert.Equal(("HEADING_BUG_INC", 1), hsi["leftKnobClockwise"]);

        var course2 = Knobs("NAV OBS:2", "NAV CDI:2");
        Assert.Equal(("VOR2_OBI_DEC", 1), course2["rightKnobAnticlockwise"]);
        Assert.Equal(("VOR2_OBI_INC", 10), course2["leftKnobClockwise"]);

        var gyro = Knobs("HEADING INDICATOR", "AUTOPILOT HEADING LOCK DIR");
        Assert.Equal("GYRO_DRIFT_INC", gyro["rightKnobClockwise"].Event);
        Assert.Equal("HEADING_BUG_INC", gyro["leftKnobClockwise"].Event);

        Assert.Equal(("KOHLSMAN_INC", 10), Knobs("INDICATED ALTITUDE", "KOHLSMAN SETTING MB:1")["leftKnobClockwise"]);
        Assert.Equal("INCREASE_DECISION_HEIGHT", Knobs("RADIO HEIGHT", "DECISION HEIGHT")["rightKnobClockwise"].Event);
        Assert.Equal("ADF_CARD_DEC", Knobs("ADF CARD", "ADF RADIAL:1")["rightKnobAnticlockwise"].Event);
        Assert.Equal(("ATTITUDE_BARS_POSITION_UP", 5), Knobs("ATTITUDE BARS POSITION")["leftKnobClockwise"]);
        Assert.Empty(Knobs("AIRSPEED INDICATED", "VERTICAL SPEED", "TURB ENG N1:1"));   // nothing to set
    }

    [Fact]
    public void Knobs_a_gauge_already_has_are_never_changed()
    {
        var json = """{ "name": "Mine", "values": { "alt": { "sim": "KOHLSMAN SETTING HG" } }, "controls": { "button1": { "event": "MY_PLANE.Event" } } }""";
        Assert.Same(json, KnobDefaults.Fill(json));
        var bare = """{ "name": "Alt", "values": { "baro": { "sim": "KOHLSMAN SETTING HG" } } }""";
        Assert.Equal("KOHLSMAN_INC", GaugeDefinition.Parse(KnobDefaults.Fill(bare)).Controls["rightKnobClockwise"].Event);
    }

    [Fact]
    public void Every_bundled_gauge_that_shows_a_setting_has_its_knobs()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "repo", "gauges");
        var missing = new List<string>();
        foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}parts{Path.DirectorySeparatorChar}")))
        {
            var gauge = GaugeDefinition.Parse(File.ReadAllText(file));
            var wanted = KnobDefaults.For(gauge.Values.Values.Select(v => v.Sim ?? ""));
            if (wanted.Count > 0 && gauge.Controls.Count == 0 && gauge.Modes.Count == 0) missing.Add(Path.GetRelativePath(root, file));
        }
        Assert.True(missing.Count == 0, "No knobs: " + string.Join(", ", missing));
    }

    [Fact]
    public void Side_by_side_puts_the_left_gauges_knob_left_and_the_right_gauges_knob_right()
    {
        var combined = GaugeDefinition.Parse(GaugeCombiner.SideBySide(File.ReadAllText(Repo("dc6/altimeter.json")), File.ReadAllText(Repo("dc6/course-ils.json")), "Alt + course"));
        Assert.Empty(combined.Problems());
        Assert.Equal("KOHLSMAN_INC", combined.Controls["leftKnobClockwise"].Event);
        Assert.Equal("KOHLSMAN_DEC", combined.Controls["leftKnobAnticlockwise"].Event);
        Assert.Equal("VOR1_OBI_INC", combined.Controls["rightKnobClockwise"].Event);
        Assert.Equal("VOR1_OBI_DEC", combined.Controls["rightKnobAnticlockwise"].Event);
    }

    [Fact]
    public void A_knob_can_change_a_plane_value_instead_of_sending_an_event()
    {
        // The 727's EPR pages: the right knob changes the FSS 727's own EPR knob value, one click at a time.
        var gauge = GaugeDefinition.Load(Repo(Path.Combine("jet", "727", "epr-2.json")));
        Assert.Empty(gauge.Problems());
        var up = gauge.Controls["rightKnobClockwise"];
        Assert.True(up.ChangesValue);
        Assert.Equal("L:FSS_B727_PCP_EPR_KNOB2", up.Set);
        Assert.Equal(1, up.Add);
        Assert.Equal(-1, gauge.Controls["rightKnobAnticlockwise"].Add);
        Assert.False(gauge.Controls.ContainsKey("leftKnobClockwise")); // the left knob stays free to flip pages

        // The big EPR dial: one knob, all three engines' bugs together.
        var all = GaugeDefinition.Load(Repo(Path.Combine("jet", "727", "epr.json"))).Controls["rightKnobClockwise"];
        Assert.Equal(["L:FSS_B727_PCP_EPR_KNOB1", "L:FSS_B727_PCP_EPR_KNOB2", "L:FSS_B727_PCP_EPR_KNOB3"], all.SetNames);

        var bad = GaugeDefinition.Parse("""{"format":1,"name":"x","canvas":{"width":320,"height":240},"controls":{"rightKnobClockwise":{"set":"FSS_KNOB"}},"layers":[]}""");
        Assert.Contains(bad.Problems(), p => p.Contains("starting with L:", StringComparison.Ordinal));
    }
}
