// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
namespace Fipinator.Core;

/// <summary>What a gauge is for, as far as moving it between planes goes.</summary>
public enum GaugeKind
{
    /// <summary>Flight and nav instruments: work in any plane (horizons, altimeters, VSIs, HSIs, radios, clock...).</summary>
    FlightNav,
    /// <summary>An airspeed gauge: its arcs and red lines belong to one plane.</summary>
    Airspeed,
    /// <summary>Engines, fuel and systems, or anything reading a plane's own L: values: belongs to that plane.</summary>
    PlaneSpecific,
}

/// <summary>
/// Sorts gauges by what they read, so FIPinator 3000 can offer any plane's flight and nav gauges for a
/// panel while keeping engine, fuel, system and airspeed gauges to the panel's own plane.
/// </summary>
public static class GaugeKinds
{
    private static readonly string[] PlaneSpecificStarts =
    [
        // Flaps, gear, trim, suction and the like work the same in any plane, so they're not on this list.
        "L:", "ENG ", "GENERAL ENG", "TURB ENG", "RECIP ENG", "PROP ", "FUEL ", "ELECTRICAL", "HYDRAULIC",
        "APU", "BLEED", "PRESSURIZATION", "TOTAL AIR TEMPERATURE",
    ];

    /// <summary>The kind of a gauge, from the sim values it reads.</summary>
    public static GaugeKind Of(GaugeDefinition gauge)
    {
        var sims = gauge.Values.Values.Select(v => (v.Sim ?? "").Trim().ToUpperInvariant()).Where(s => s.Length > 0).ToList();
        if (sims.Any(s => PlaneSpecificStarts.Any(p => s.StartsWith(p, StringComparison.Ordinal)))) return GaugeKind.PlaneSpecific;
        var attitude = sims.Any(s => s.StartsWith("ATTITUDE INDICATOR", StringComparison.Ordinal) || s.StartsWith("PLANE PITCH", StringComparison.Ordinal));
        if (!attitude && sims.Any(s => s.StartsWith("AIRSPEED", StringComparison.Ordinal))) return GaugeKind.Airspeed;
        return GaugeKind.FlightNav;
    }

    /// <summary>
    /// The gauges that make sense for a panel: flight and nav gauges from every set, the user's own gauges,
    /// and airspeed, engine, fuel and system gauges only from the sets the panel already uses.
    /// </summary>
    public static IReadOnlyList<(GaugeChoice Choice, GaugeKind Kind)> ChoicesFor(IEnumerable<string> panelGaugePaths, IReadOnlyList<GaugeChoice> all)
    {
        var used = new HashSet<string>(panelGaugePaths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        var ownGroups = new HashSet<string>(all.Where(c => used.Contains(Path.GetFullPath(c.Path))).Select(c => c.Group), StringComparer.Ordinal);
        var list = new List<(GaugeChoice, GaugeKind)>();
        foreach (var choice in all)
        {
            GaugeKind kind;
            try { kind = Of(GaugeDefinition.Load(choice.Path)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException) { continue; }
            if (choice.Group == "My gauges" || kind == GaugeKind.FlightNav || ownGroups.Contains(choice.Group)) list.Add((choice, kind));
        }
        return list;
    }

    /// <summary>
    /// The folder a panel's gauges come from (the one holding classic, dc6, jet, turboprop...), so replacements
    /// come from the same place; null if the panel's gauges aren't in such a folder.
    /// </summary>
    public static string? LibraryRoot(IEnumerable<string> panelGaugePaths)
    {
        string[] sets = ["classic", "dc6", "jet", "turboprop"];
        foreach (var path in panelGaugePaths)
        {
            for (var dir = Path.GetDirectoryName(Path.GetFullPath(path)); dir is not null; dir = Path.GetDirectoryName(dir))
                if (sets.Contains(Path.GetFileName(dir), StringComparer.OrdinalIgnoreCase))
                    return Path.GetDirectoryName(dir);
        }
        return null;
    }
}
