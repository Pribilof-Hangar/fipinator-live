// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Text.Json;

namespace Fipinator.Core;

/// <summary>Which gauge goes on which FIP. FIPs are matched by serial number, because the order
/// DirectOutput lists them in can change between runs.</summary>
public sealed record PanelSettings
{
    public int Format { get; init; } = 1;

    /// <summary>Name shown in FIPinator's panel list, e.g. "DC-6 PMDG". Falls back to the file name.</summary>
    public string? Name { get; init; }

    public List<FipAssignment> Fips { get; init; } = [];

    /// <summary>
    /// Name of the main layout (the <see cref="Fips"/> above), shown when <see cref="Layouts"/> has more. Null =
    /// "Layout 1".
    /// </summary>
    public string? LayoutName { get; init; }

    /// <summary>
    /// More layouts of the same FIPs (owner, 2026-11-14), e.g. "Nav" in <see cref="Fips"/> and "Engines" here: a
    /// whole different set of gauges on the same FIPs. All FIPs switch to the next layout together when
    /// <see cref="LayoutButton"/> is pressed (no timer). A FIP missing from a layout keeps its main-layout gauges.
    /// </summary>
    public List<PanelLayout> Layouts { get; init; } = [];

    /// <summary>The FIP button that switches every FIP to the next layout, or null. Only used when there are layouts.</summary>
    public LayoutButton? LayoutButton { get; init; }

    /// <summary>Every layout in order: the main one (<see cref="Fips"/>) first.</summary>
    public IReadOnlyList<PanelLayout> AllLayouts() =>
        [new PanelLayout { Name = string.IsNullOrWhiteSpace(LayoutName) ? "Layout 1" : LayoutName, Fips = Fips }, .. Layouts];

    /// <summary>A FIP's assignment in a layout, falling back to the main layout when the layout leaves it out.</summary>
    public FipAssignment FipIn(int layout, string serial)
    {
        var main = Fips.First(f => f.Serial.Equals(serial, StringComparison.OrdinalIgnoreCase));
        if (layout <= 0 || layout > Layouts.Count) return main;
        return Layouts[layout - 1].Fips.FirstOrDefault(f => f.Serial.Equals(serial, StringComparison.OrdinalIgnoreCase)) ?? main;
    }

    /// <summary>The panel FIPinator runs now (see <see cref="PanelLibrary"/>).</summary>
    public static string DefaultPath => PanelLibrary.ForCurrentUser().ActivePath();

    /// <exception cref="InvalidDataException">The file has mistakes; the message lists them.</exception>
    public static PanelSettings Load(string path)
    {
        PanelSettings? settings;
        try { settings = JsonSerializer.Deserialize<PanelSettings>(File.ReadAllText(path), GaugeDefinition.JsonOptions); }
        catch (JsonException ex) { throw new InvalidDataException($"Panel settings file {path} can't be read: {ex.Message}"); }
        settings ??= new PanelSettings();
        var problems = settings.Problems(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (problems.Count > 0)
            throw new InvalidDataException($"Panel settings file {path} has mistakes:\n  - " + string.Join("\n  - ", problems));
        return settings;
    }

    /// <summary>Saves through a temporary file, so a crash mid-save can't leave a half-written file.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions(GaugeDefinition.JsonOptions) { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Gauge file path for an assignment; relative paths are relative to the settings file's folder.</summary>
    public static string GaugePath(FipAssignment fip, string settingsFolder) =>
        Path.GetFullPath(Path.Combine(settingsFolder, fip.Gauge));

    /// <summary>All of a FIP's gauges in page order: its main gauge, then any extra pages.</summary>
    public static IReadOnlyList<string> PagePaths(FipAssignment fip, string settingsFolder) =>
        [GaugePath(fip, settingsFolder), .. fip.Pages.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.GetFullPath(Path.Combine(settingsFolder, p)))];

    public IReadOnlyList<string> Problems(string settingsFolder)
    {
        var problems = new List<string>();
        if (Fips.Count == 0) problems.Add("no FIPs are listed");
        foreach (var dup in Fips.GroupBy(f => f.Serial, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"FIP {dup.Key} is listed {dup.Count()} times");
        foreach (var fip in Fips)
        {
            if (string.IsNullOrWhiteSpace(fip.Serial)) problems.Add("a FIP has no serial number");
            if (string.IsNullOrWhiteSpace(fip.Gauge)) { problems.Add($"FIP {fip.Serial} has no gauge"); continue; }
            foreach (var page in PagePaths(fip, settingsFolder))
                if (!File.Exists(page)) problems.Add($"FIP {fip.Serial}: gauge file not found: {page}");
            if (fip.Pages.Count > 15) problems.Add($"FIP {fip.Serial} has {fip.Pages.Count + 1} pages; 16 is the most");
        }
        foreach (var layout in Layouts)
        {
            var name = string.IsNullOrWhiteSpace(layout.Name) ? "a layout" : $"layout \"{layout.Name}\"";
            foreach (var fip in layout.Fips)
            {
                if (!Fips.Any(f => f.Serial.Equals(fip.Serial, StringComparison.OrdinalIgnoreCase)))
                    problems.Add($"{name} has FIP {fip.Serial}, which isn't in the main layout");
                if (string.IsNullOrWhiteSpace(fip.Gauge)) { problems.Add($"{name}: FIP {fip.Serial} has no gauge"); continue; }
                foreach (var page in PagePaths(fip, settingsFolder))
                    if (!File.Exists(page)) problems.Add($"{name}: FIP {fip.Serial}: gauge file not found: {page}");
                if (fip.Pages.Count > 15) problems.Add($"{name}: FIP {fip.Serial} has {fip.Pages.Count + 1} pages; 16 is the most");
            }
        }
        if (LayoutButton is { } button && Layouts.Count > 0)
        {
            if (!Fips.Any(f => f.Serial.Equals(button.Serial, StringComparison.OrdinalIgnoreCase)))
                problems.Add($"the layout button is on FIP {button.Serial}, which isn't in the panel");
            if (!LayoutButton.Buttons.Contains(button.Button)) problems.Add($"the layout button \"{button.Button}\" isn't one of button1 to button6");
        }
        return problems;
    }
}

/// <summary>One layout: a whole set of gauges for the panel's FIPs (see <see cref="PanelSettings.Layouts"/>).</summary>
public sealed record PanelLayout
{
    public string Name { get; init; } = "";
    public List<FipAssignment> Fips { get; init; } = [];
}

/// <summary>The FIP button (button1 = S1 ... button6 = S6) that switches all FIPs to the next layout.</summary>
public sealed record LayoutButton
{
    public static readonly IReadOnlyList<string> Buttons = ["button1", "button2", "button3", "button4", "button5", "button6"];

    public string Serial { get; init; } = "";
    public string Button { get; init; } = "button6";
}

public sealed record FipAssignment
{
    public string Serial { get; init; } = "";

    /// <summary>The gauge the FIP shows first.</summary>
    public string Gauge { get; init; } = "";

    /// <summary>
    /// More gauges on the same FIP, flipped through with the FIP's page up/down buttons, in this
    /// order after <see cref="Gauge"/>. Empty for a FIP with one gauge.
    /// </summary>
    public List<string> Pages { get; init; } = [];

    /// <summary>
    /// When true, the FIP's left knob flips through <see cref="Gauge"/> and <see cref="Pages"/>
    /// (clockwise: next, anticlockwise: back, wrapping round), instead of the page up/down buttons.
    /// The left knob's own gauge actions are not used on such a FIP. Off by default.
    /// </summary>
    public bool LeftKnobPages { get; init; }

    /// <summary>
    /// Auto-flip: show the next of <see cref="Gauge"/> and <see cref="Pages"/> every this many seconds
    /// (3 to 600; 0 = off, the default). Turning the left knob (when <see cref="LeftKnobPages"/> is on)
    /// pauses it for a while. On such a FIP the page up/down buttons don't flip the gauges.
    /// </summary>
    public int AutoFlipSeconds { get; init; }

    /// <summary>
    /// Where this FIP sits in FIPinator 3000's desk view (pixels from the top left), so the boxes can be laid out
    /// like the real FIPs on the owner's desk. Only for showing; FIPinator Live ignores it. Null = not placed yet.
    /// </summary>
    public int? DeskX { get; init; }

    /// <inheritdoc cref="DeskX"/>
    public int? DeskY { get; init; }
}
