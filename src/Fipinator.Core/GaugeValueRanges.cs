// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
namespace Fipinator.Core;

/// <summary>A sensible slider for one gauge value: its range, step, and whether it's on/off.</summary>
public sealed record ValueRange(string Name, double Min, double Max, double Step, bool IsOnOff, double Default)
{
    /// <summary>Clamps a value into the range.</summary>
    public double Clamp(double value) => Math.Clamp(value, Min, Max);
}

/// <summary>
/// Works out slider ranges for a gauge's values, so the Gauge Maker can offer sliders without
/// any setup. Values worked out by formulas get no slider (they follow the others).
/// </summary>
public static class GaugeValueRanges
{
    public static IReadOnlyList<ValueRange> For(GaugeDefinition gauge)
    {
        // Which scales each value drives, directly (needles, turning/sliding groups).
        var scalesByValue = new Dictionary<string, List<ScaleSpec>>(StringComparer.Ordinal);
        void Note(string value, string scaleName)
        {
            if (!gauge.Scales.TryGetValue(scaleName, out var scale)) return;
            if (!scalesByValue.TryGetValue(value, out var list)) scalesByValue[value] = list = [];
            list.Add(scale);
        }
        void Walk(IEnumerable<Layer> layers)
        {
            foreach (var layer in layers)
            {
                switch (layer)
                {
                    case NeedleLayer n: Note(n.Value, n.Scale); break;
                    case GroupLayer g:
                        foreach (var link in new[] { g.Rotate, g.MoveX, g.MoveY })
                            if (link is not null) Note(link.Value, link.Scale);
                        Walk(g.Layers);
                        break;
                }
            }
        }
        Walk(gauge.Layers);

        var ranges = new List<ValueRange>();
        foreach (var (name, spec) in gauge.Values.OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            if (spec.Expr is not null) continue; // worked out from other values

            var unit = spec.Unit?.Trim().ToLowerInvariant() ?? "";
            if (unit == "bool")
            {
                ranges.Add(new ValueRange(name, 0, 1, 1, IsOnOff: true, spec.Default));
                continue;
            }

            double min, max;
            if (scalesByValue.TryGetValue(name, out var scales) && scales.Count > 0)
            {
                // Cover every scale the value drives; a wrapping scale (altimeter hands) is
                // driven by the value's full range, so use the widest non-wrapping one if any.
                var plain = scales.Where(s => !s.Wrap).ToList();
                var use = plain.Count > 0 ? plain : scales;
                min = use.Min(s => Math.Min(s.Low, s.High));
                max = use.Max(s => Math.Max(s.Low, s.High));
                if (plain.Count == 0) max = Math.Max(max, Math.Abs(spec.Default) * 2); // e.g. altitude
            }
            else if (unit.Contains("degree"))
            {
                (min, max) = spec.Default < 0 ? (-180, 180) : (0, 360);
            }
            else
            {
                // Number shown as text (pressure, distance, ...): around its default.
                var d = Math.Abs(spec.Default);
                (min, max) = d > 0 ? (0, Math.Max(d * 2, 10)) : (0, 100);
            }

            if (spec.Default < min) min = spec.Default;
            if (spec.Default > max) max = spec.Default;
            if (max <= min) max = min + 1;
            ranges.Add(new ValueRange(name, min, max, NiceStep(max - min), IsOnOff: false, spec.Default));
        }
        return ranges;
    }

    /// <summary>A step that gives a few hundred positions across the range, rounded to 1, 2 or 5.</summary>
    private static double NiceStep(double span)
    {
        var raw = span / 400;
        var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var m = raw / power;
        return (m < 1.5 ? 1 : m < 3.5 ? 2 : m < 7.5 ? 5 : 10) * power;
    }
}
