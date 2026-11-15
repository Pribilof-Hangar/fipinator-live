// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
namespace Fipinator.Core;

/// <summary>A gauge someone can put on a FIP: its name, the group it's listed under, and its file.</summary>
public sealed record GaugeChoice(string Name, string Group, string Path);

/// <summary>
/// The gauges FIPinator Live can offer: the ones shipped with the program (Gauges folder next to it)
/// and the user's own (Documents\FIPinator 3000\Gauges, made with FIPinator 3000). Files that can't
/// be read are skipped and logged, never shown half-broken.
/// </summary>
public static class GaugeCatalog
{
    public static string ShippedFolder => Path.Combine(AppContext.BaseDirectory, "Gauges");

    public static string MyGaugesFolder => Path.Combine(PanelLibrary.ForCurrentUser().Folder, "Gauges");

    /// <summary>All gauges, grouped (e.g. "Classic twin", "DC-6", "My gauges") and sorted.</summary>
    public static IReadOnlyList<GaugeChoice> All(string? shippedFolder = null, string? myFolder = null)
    {
        var list = new List<GaugeChoice>();
        Add(list, shippedFolder ?? ShippedFolder, mine: false);
        Add(list, myFolder ?? MyGaugesFolder, mine: true);
        return [.. list.OrderBy(g => GroupOrder(g.Group)).ThenBy(g => g.Group, StringComparer.CurrentCultureIgnoreCase).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static void Add(List<GaugeChoice> list, string folder, bool mine)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(folder, Path.GetDirectoryName(file)!);
            if (relative.Split(Path.DirectorySeparatorChar).Contains("parts", StringComparer.OrdinalIgnoreCase)) continue;
            try
            {
                var gauge = GaugeDefinition.Load(file);
                list.Add(new GaugeChoice(gauge.Name, mine ? "My gauges" : GroupName(relative), file));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
                Log.Warn($"Gauge list: {file} skipped: {ex.Message}");
            }
        }
    }

    /// <summary>"classic\twin" -> "Classic twin", every DC-6 gauge -> "DC-6" (owner: one group, no searching).</summary>
    public static string GroupName(string relative)
    {
        var parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Where(p => p != ".").ToList();
        if (parts.Count == 0) return "Other";
        var first = parts[0].ToLowerInvariant() switch { "dc6" => "DC-6", "classic" => "Classic", var p => char.ToUpperInvariant(p[0]) + p[1..] };
        if (first == "DC-6") return "DC-6"; // flight, nav, engines, overhead and mini: all in one group
        if (parts.Count == 1) return first;
        if (first == "Turboprop")
            return parts[1].ToLowerInvariant() switch { "saab-340b" => "Dual turboprop", "caravan" => "Single turboprop", var p => $"Turboprop {p}" };
        if (first == "Jet")
            return parts[1].ToLowerInvariant() switch { "737-max" => "Dual jet", "737-max-round" => "Dual jet (round gauges)", "727" => "Triple jet (Boeing 727)", "737-800" => "Boeing 737", var p => $"Jet {p}" };
        if (first == "Templates")
            return parts[1].ToLowerInvariant() switch
            {
                "gear-flaps" => "Gear, flaps and trim", "autopilot-radios" => "Autopilot and radios", "warnings-lights" => "Warnings and lights",
                "weather-small" => "Weather and small gauges", "boeing-style" => "Boeing style", "airbus-style" => "Airbus style", var p => $"Templates {p}",
            };
        if (first == "Classic" && parts[1].Equals("titan", StringComparison.OrdinalIgnoreCase)) return "Cessna 404 Titan";
        var rest = parts[1].ToLowerInvariant() switch { "four" => "four-engine", "nav" => "radios and nav", var p => p };
        return $"{first} {rest}";
    }

    private static int GroupOrder(string group) => group switch
    {
        "My gauges" => 0,
        _ when group.StartsWith("Classic", StringComparison.Ordinal) => 1,
        _ when group.Contains("urboprop", StringComparison.Ordinal) || group.Contains(" jet", StringComparison.Ordinal) || group.StartsWith("Jet", StringComparison.Ordinal) => 2,
        _ when group.StartsWith("DC-6", StringComparison.Ordinal) => 3,
        "Gear, flaps and trim" or "Autopilot and radios" or "Warnings and lights" or "Weather and small gauges" or "Boeing style" or "Airbus style" => 1,
        _ => 4,
    };
}
