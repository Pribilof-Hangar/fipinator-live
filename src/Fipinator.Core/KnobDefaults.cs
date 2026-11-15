// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fipinator.Core;

/// <summary>
/// The standard FIP knobs for instruments a pilot sets with a knob, worked out from what the gauge
/// shows (its sim values), the same way the DC-6 and classic gauges were set up by hand:
/// <list type="bullet">
/// <item>course (NAV OBS:n): right knob course; left knob heading bug if the gauge has one (HSI),
/// otherwise course in steps of 10</item>
/// <item>directional gyro with a heading bug: right knob aligns the gyro, left knob heading bug</item>
/// <item>heading bug only: heading bug (left knob 10 at a time)</item>
/// <item>baro (KOHLSMAN SETTING ...): baro (left knob 10 at a time)</item>
/// <item>decision height, ADF card, attitude bars, gyro alone: that setting (left knob faster)</item>
/// </list>
/// Event names are stock MSFS events. A gauge that already has any knob or button set keeps exactly
/// what it has: these are only defaults for gauges with none.
/// </summary>
public static class KnobDefaults
{
    /// <summary>The knob actions this gauge should get, or none if it shows nothing a knob sets.</summary>
    public static IReadOnlyList<(string Control, string Event, int Repeat)> For(IEnumerable<string> sims)
    {
        var list = sims.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()).ToList();
        bool Has(string start) => list.Any(s => s.StartsWith(start, StringComparison.Ordinal));
        (string, string, int)[] Both(string inc, string dec, int fast) =>
            [("rightKnobClockwise", inc, 1), ("rightKnobAnticlockwise", dec, 1), ("leftKnobClockwise", inc, fast), ("leftKnobAnticlockwise", dec, fast)];

        var obs = list.FirstOrDefault(s => s.StartsWith("NAV OBS", StringComparison.Ordinal));
        var bug = Has("AUTOPILOT HEADING LOCK DIR");
        if (obs is not null)
        {
            var radio = obs.EndsWith(":2", StringComparison.Ordinal) ? "VOR2" : "VOR1";
            return bug
                ? [("rightKnobClockwise", $"{radio}_OBI_INC", 1), ("rightKnobAnticlockwise", $"{radio}_OBI_DEC", 1),
                   ("leftKnobClockwise", "HEADING_BUG_INC", 1), ("leftKnobAnticlockwise", "HEADING_BUG_DEC", 1)]
                : Both($"{radio}_OBI_INC", $"{radio}_OBI_DEC", 10);
        }
        if (Has("HEADING INDICATOR") && bug)
            return [("rightKnobClockwise", "GYRO_DRIFT_INC", 1), ("rightKnobAnticlockwise", "GYRO_DRIFT_DEC", 1),
                    ("leftKnobClockwise", "HEADING_BUG_INC", 1), ("leftKnobAnticlockwise", "HEADING_BUG_DEC", 1)];
        if (bug) return Both("HEADING_BUG_INC", "HEADING_BUG_DEC", 10);
        if (Has("KOHLSMAN SETTING")) return Both("KOHLSMAN_INC", "KOHLSMAN_DEC", 10);
        if (Has("DECISION HEIGHT")) return Both("INCREASE_DECISION_HEIGHT", "DECREASE_DECISION_HEIGHT", 10);
        if (Has("ADF CARD")) return Both("ADF_CARD_INC", "ADF_CARD_DEC", 10);
        if (Has("ATTITUDE BARS POSITION")) return Both("ATTITUDE_BARS_POSITION_UP", "ATTITUDE_BARS_POSITION_DOWN", 5);
        if (Has("HEADING INDICATOR")) return Both("GYRO_DRIFT_INC", "GYRO_DRIFT_DEC", 10);
        return [];
    }

    /// <summary>
    /// The gauge with its standard knobs added, if it has no knobs or buttons yet and shows something
    /// a knob sets; otherwise the gauge unchanged (same text). Comments and trailing commas are allowed.
    /// </summary>
    public static string Fill(string json)
    {
        var gauge = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
        if (gauge["controls"] is JsonObject existing && existing.Count > 0) return json;
        if (gauge["modes"] is JsonArray modes && modes.Count > 0) return json; // its knobs are set per mode
        var sims = (gauge["values"] as JsonObject ?? []).Select(v => v.Value?["sim"]?.GetValue<string>() ?? "");
        var knobs = For(sims);
        if (knobs.Count == 0) return json;
        var controls = new JsonObject();
        foreach (var (control, ev, repeat) in knobs)
        {
            var c = new JsonObject { ["event"] = ev };
            if (repeat > 1) c["repeat"] = repeat;
            controls[control] = c;
        }
        gauge["controls"] = controls;
        return gauge.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
