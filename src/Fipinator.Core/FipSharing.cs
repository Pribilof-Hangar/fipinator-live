// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
namespace Fipinator.Core;

/// <summary>
/// "Share the FIPs with SPAD.neXt": SPAD.neXt only draws on the FIPs while Logitech's FIP service is stopped,
/// and FIPinator Live needs it running. With sharing on, FIPinator Live starts the service when it opens and
/// stops it when it closes. Windows only lets administrators start and stop services, so a one-time setup
/// (one Windows permission prompt) lets the signed-in user start and stop this one service, and sets it
/// to start only when asked instead of with Windows. Undo puts both back exactly as they were.
/// These are the pure parts (text handling), kept here so they can be tested.
/// </summary>
public static class FipSharing
{
    /// <summary>
    /// The permission added to the service: interactive users (IU, whoever is signed in at the PC) may start
    /// (RP), stop (WP) and read the state (LC) of this service. Nothing else.
    /// </summary>
    public const string StartStopForSignedInUser = "(A;;RPWPLC;;;IU)";

    /// <summary>
    /// The service's security text (SDDL, as "sc sdshow" prints it) with <see cref="StartStopForSignedInUser"/>
    /// added to its permission list; unchanged if it's already there.
    /// </summary>
    /// <exception cref="FormatException">The text has no permission list ("D:").</exception>
    public static string AddStartStop(string sddl)
    {
        sddl = sddl.Trim();
        if (sddl.Contains(StartStopForSignedInUser, StringComparison.OrdinalIgnoreCase)) return sddl;
        var d = sddl.IndexOf("D:", StringComparison.Ordinal);
        if (d < 0) throw new FormatException("The service's permissions couldn't be read (no D: part).");
        // After "D:" may come flags (letters like AI, P) before the first "(": the new entry goes after them.
        var at = d + 2;
        while (at < sddl.Length && char.IsLetter(sddl[at])) at++;
        return sddl[..at] + StartStopForSignedInUser + sddl[at..];
    }

    /// <summary>"sc qc" output to the word "sc config start=" takes: auto, delayed-auto, demand or disabled.</summary>
    public static string? StartType(string scQcOutput)
    {
        var line = scQcOutput.Split('\n').FirstOrDefault(l => l.Contains("START_TYPE", StringComparison.Ordinal));
        if (line is null) return null;
        if (line.Contains("DELAYED", StringComparison.Ordinal)) return "delayed-auto";
        if (line.Contains("AUTO_START", StringComparison.Ordinal)) return "auto";
        if (line.Contains("DEMAND_START", StringComparison.Ordinal)) return "demand";
        if (line.Contains("DISABLED", StringComparison.Ordinal)) return "disabled";
        return null;
    }

    /// <summary>What setup changed, so undo can put it back (saved next to the user's settings).</summary>
    public sealed record Backup(string Service, string Sddl, string StartType, DateTime When);
}
