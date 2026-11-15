// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class InstrumentTemplatesTests
{
    public static TheoryData<string> Files => [.. InstrumentTemplates.All().Select(t => $"{t.Group}/{t.File}")];

    [Theory]
    [MemberData(nameof(Files))]
    public void Every_template_is_valid_and_draws(string file)
    {
        var gauge = GaugeDefinition.Parse(InstrumentTemplates.All().Single(t => $"{t.Group}/{t.File}" == file).Json);
        Assert.True(gauge.Problems().Count == 0, $"{file}: {string.Join("; ", gauge.Problems())}");
        using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
        using var bitmap = renderer.Render();
        Assert.Equal(320, bitmap.Width);
    }

    [Fact]
    public void Autopilot_page_uses_knob_modes_on_S1_to_S4_and_S6_for_the_autopilot()
    {
        var ap = GaugeDefinition.Parse(InstrumentTemplates.Autopilot());
        Assert.Equal(["SPD", "HDG", "ALT", "V/S"], ap.Modes.Select(m => m.Name));
        Assert.Equal(["button1", "button2", "button3", "button4"], ap.Modes.Select(m => m.Button));
        Assert.Equal("AP_ALT_VAR_INC", ap.Modes[2].Controls["rightKnobClockwise"].Event);
        Assert.Equal("AP_MASTER", ap.Controls["button6"].Event);
        Assert.Equal("ap", ap.ButtonLights["button6"]);
        var json = InstrumentTemplates.Autopilot();
        Assert.Same(json, KnobDefaults.Fill(json)); // knob modes: no default knobs added on top
    }

    [Theory]
    [InlineData(1, "highlight")]
    [InlineData(2, "hidden")]
    public void The_chosen_mode_is_highlighted(int mode, string expected)
    {
        var gauge = GaugeDefinition.Parse(InstrumentTemplates.Autopilot());
        using var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
        var key = gauge.Values.Keys.First(k => k.StartsWith("sel1_", StringComparison.Ordinal) && k.EndsWith("_hide", StringComparison.Ordinal));
        var moved = renderer.ValueOf(key, new Dictionary<string, double> { [GaugeDefinition.KnobModeValue] = mode });
        Assert.Equal(expected == "highlight" ? 0 : 400, moved, 6);
    }

    [Theory]
    [InlineData(0x1200, 1200)]
    [InlineData(0x7700, 7700)]
    [InlineData(0x0457, 457)]
    public void Transponder_code_is_read_from_packed_digits(int raw, int code)
    {
        var gauge = GaugeDefinition.Parse(InstrumentTemplates.Transponder());
        using var r = new GaugeRenderer(gauge, AppContext.BaseDirectory);
        var values = new Dictionary<string, double> { ["code"] = raw };
        var digits = Enumerable.Range(1, 4).Select(i => (int)r.ValueOf($"d{i}", values)).ToArray();
        Assert.Equal(code, digits[0] * 1000 + digits[1] * 100 + digits[2] * 10 + digits[3]);
    }

    [Fact]
    public void Templates_are_grouped_by_style()
    {
        var groups = InstrumentTemplates.All().GroupBy(t => t.Group).ToDictionary(g => g.Key, g => g.Select(t => t.File).ToList());
        Assert.Equal(["flaps", "autobrake"], groups["boeing-style"]);
        Assert.Equal(["engines", "flaps", "gear", "fcu", "autobrake", "speedbrake"], groups["airbus-style"]);
        var autobrake = GaugeDefinition.Parse(InstrumentTemplates.AirbusAutobrake());
        Assert.Equal([1u, 2u, 3u], new[] { "button1", "button2", "button3" }.Select(b => autobrake.Controls[b].Data));
        Assert.All(new[] { "button1", "button2", "button3" }, b => Assert.Equal("SET_AUTOBRAKE_CONTROL", autobrake.Controls[b].Event));
    }

    [Fact]
    public void Gear_lights_show_down_moving_and_up()
    {
        var gauge = GaugeDefinition.Parse(InstrumentTemplates.GearLights());
        using var r = new GaugeRenderer(gauge, AppContext.BaseDirectory);
        Assert.Equal(1, r.ValueOf("left_down", new Dictionary<string, double> { ["left"] = 1 }), 6);
        Assert.Equal(1, r.ValueOf("left_moving", new Dictionary<string, double> { ["left"] = 0.5 }), 6);
        Assert.Equal(0, r.ValueOf("left_moving", new Dictionary<string, double> { ["left"] = 0 }), 6);
    }

    [Fact]
    public void Knob_modes_and_button_lights_are_checked()
    {
        var bad = GaugeDefinition.Parse("""
            { "name": "Bad", "values": { "x": { "default": 0 } },
              "modes": [ { "name": "A", "button": "button9", "controls": { "rightKnobClockwise": { "event": "not an event" } } } ],
              "buttonLights": { "button7": "x", "button2": "nope" } }
            """);
        var problems = string.Join("\n", bad.Problems());
        Assert.Contains("button must be one of button1 to button6", problems);
        Assert.Contains("isn't a sim event name", problems);
        Assert.Contains("need a value 'knobMode'", problems);
        Assert.Contains("'button7' isn't button1 to button6", problems);
        Assert.Contains("'nope'", problems);
    }
}
