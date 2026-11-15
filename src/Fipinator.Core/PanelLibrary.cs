// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Text.Json;

namespace Fipinator.Core;

/// <summary>
/// The user's panels: one settings file per panel (for example one per aircraft) in
/// Documents\FIPinator 3000\Panels, plus which one ran last. In Documents so they're easy to
/// find, back up and share, survive app upgrades and moving the program, and aren't hidden by
/// AppData redirection (which packaged apps such as the Claude desktop app apply).
/// </summary>
public sealed class PanelLibrary(string folder, string? importFrom = null)
{
    /// <summary>Name given to the panel made from the original single panel.json.</summary>
    public const string MigratedName = "DC-6 PMDG";

    public static PanelLibrary ForCurrentUser() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FIPinator 3000"),
        importFrom: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FIPinator 3000"));

    public string Folder { get; } = folder;

    public string PanelsFolder => Path.Combine(Folder, "Panels");

    private string LegacyPath => Path.Combine(Folder, "panel.json");

    private string StatePath => Path.Combine(Folder, "app-state.json");

    /// <summary>Panels found, sorted by name: (name shown to the user, file path).</summary>
    public IReadOnlyList<(string Name, string Path)> List()
    {
        MigrateOldSettings();
        if (!Directory.Exists(PanelsFolder)) return [];
        return [.. Directory.GetFiles(PanelsFolder, "*.json")
            .Select(p => (Name: ReadName(p), Path: p))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>The panel to run: the one chosen last time if it still exists, else the first one.</summary>
    public string ActivePath()
    {
        var panels = List();
        var chosen = ReadState();
        var match = panels.FirstOrDefault(p => string.Equals(Path.GetFileName(p.Path), chosen, StringComparison.OrdinalIgnoreCase));
        if (match.Path is not null) return match.Path;
        return panels.Count > 0 ? panels[0].Path : Path.Combine(PanelsFolder, MigratedName + ".json");
    }

    private string DefaultsFolder => Path.Combine(PanelsFolder, "defaults");

    private string DefaultCopyPath(string panelPath) => Path.Combine(DefaultsFolder, Path.GetFileName(panelPath));

    /// <summary>
    /// Swaps the gauges shown on two FIPs in a panel and saves it. The first change to a panel
    /// keeps a copy of its original layout, so it can be put back with <see cref="ResetToDefault"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">The panel file has mistakes, or a FIP isn't in it.</exception>
    public void Swap(string panelPath, string serialA, string serialB)
    {
        var panel = PanelSettings.Load(panelPath);
        var a = panel.Fips.FindIndex(f => f.Serial.Equals(serialA, StringComparison.OrdinalIgnoreCase));
        var b = panel.Fips.FindIndex(f => f.Serial.Equals(serialB, StringComparison.OrdinalIgnoreCase));
        if (a < 0 || b < 0 || a == b) throw new InvalidDataException("Pick two different FIPs from this panel to swap.");

        if (!File.Exists(DefaultCopyPath(panelPath)))
        {
            Directory.CreateDirectory(DefaultsFolder);
            File.Copy(panelPath, DefaultCopyPath(panelPath));
        }
        var fips = panel.Fips.ToList();
        // Each FIP keeps its serial; the gauges (with their extra pages) trade places.
        // The knob and auto-flip settings belong to the gauges, so they move with them.
        (fips[a], fips[b]) = (fips[a] with { Gauge = fips[b].Gauge, Pages = fips[b].Pages, LeftKnobPages = fips[b].LeftKnobPages, AutoFlipSeconds = fips[b].AutoFlipSeconds },
                              fips[b] with { Gauge = fips[a].Gauge, Pages = fips[a].Pages, LeftKnobPages = fips[a].LeftKnobPages, AutoFlipSeconds = fips[a].AutoFlipSeconds });
        (panel with { Fips = fips }).Save(panelPath);
        Log.Info($"Panel {Path.GetFileName(panelPath)}: swapped {serialA} and {serialB}.");
    }

    /// <summary>True if the panel was changed and its original layout can be put back.</summary>
    public bool HasDefault(string panelPath) => File.Exists(DefaultCopyPath(panelPath));

    /// <summary>Puts a panel's original layout back (the copy kept before its first change).</summary>
    public void ResetToDefault(string panelPath)
    {
        var copy = DefaultCopyPath(panelPath);
        if (!File.Exists(copy)) return;
        var temp = panelPath + ".tmp";
        File.Copy(copy, temp, overwrite: true);
        File.Move(temp, panelPath, overwrite: true);
        File.Delete(copy);
        Log.Info($"Panel {Path.GetFileName(panelPath)}: reset to its original layout.");
    }

    /// <summary>Remembers the panel to run next time.</summary>
    public void SetActive(string path)
    {
        Directory.CreateDirectory(Folder);
        var temp = StatePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new State(Path.GetFileName(path))));
        File.Move(temp, StatePath, overwrite: true);
    }

    /// <summary>
    /// The first version kept one panel in panel.json. Copy it into the panels folder as
    /// "DC-6 PMDG"; the original file is left untouched as a backup.
    /// </summary>
    private void MigrateOldSettings()
    {
        ImportFromOldFolder();
        try
        {
            if (!File.Exists(LegacyPath)) return;
            if (Directory.Exists(PanelsFolder) && Directory.GetFiles(PanelsFolder, "*.json").Length > 0) return;
            var old = JsonSerializer.Deserialize<PanelSettings>(File.ReadAllText(LegacyPath), GaugeDefinition.JsonOptions);
            if (old is null) return;
            (old with { Name = old.Name ?? MigratedName }).Save(Path.Combine(PanelsFolder, MigratedName + ".json"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"Couldn't copy the old panel.json into the panels folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Earlier versions kept panels in %APPDATA%\FIPinator 3000. If the new folder has no panels
    /// yet, copy them across (panel files, the default-layout copies, and the last choice).
    /// The old files are left untouched.
    /// </summary>
    private void ImportFromOldFolder()
    {
        if (importFrom is null || !Directory.Exists(importFrom)) return;
        try
        {
            if (Directory.Exists(PanelsFolder) && Directory.GetFiles(PanelsFolder, "*.json").Length > 0) return;
            var oldPanels = Path.Combine(importFrom, "panels");
            if (Directory.Exists(oldPanels) && Directory.GetFiles(oldPanels, "*.json").Length > 0)
            {
                Directory.CreateDirectory(PanelsFolder);
                foreach (var file in Directory.GetFiles(oldPanels, "*.json"))
                    File.Copy(file, Path.Combine(PanelsFolder, Path.GetFileName(file)), overwrite: false);
                var oldDefaults = Path.Combine(oldPanels, "defaults");
                if (Directory.Exists(oldDefaults))
                {
                    Directory.CreateDirectory(DefaultsFolder);
                    foreach (var file in Directory.GetFiles(oldDefaults, "*.json"))
                        File.Copy(file, Path.Combine(DefaultsFolder, Path.GetFileName(file)), overwrite: false);
                }
                var oldState = Path.Combine(importFrom, "app-state.json");
                if (File.Exists(oldState) && !File.Exists(StatePath)) File.Copy(oldState, StatePath);
                Log.Info($"Panels copied from {oldPanels} to {PanelsFolder}.");
            }
            else if (File.Exists(Path.Combine(importFrom, "panel.json")) && !File.Exists(LegacyPath))
            {
                Directory.CreateDirectory(Folder);
                File.Copy(Path.Combine(importFrom, "panel.json"), LegacyPath); // turned into "DC-6 PMDG" next
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Couldn't copy panels from the old settings folder: {ex.Message}");
        }
    }

    private static string ReadName(string path)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<PanelSettings>(File.ReadAllText(path), GaugeDefinition.JsonOptions);
            if (!string.IsNullOrWhiteSpace(settings?.Name)) return settings.Name.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return Path.GetFileNameWithoutExtension(path);
    }

    private string? ReadState()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath))?.Panel : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private sealed record State(string Panel);
}
