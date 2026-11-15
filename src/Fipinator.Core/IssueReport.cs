// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Fipinator.Core;

/// <summary>
/// Problem reports, shared by both apps: hiding personal details, the end of today's log, and the "Report a problem"
/// GitHub page. The apps never post anything themselves: they open GitHub's "New issue" page already filled in, and
/// the user reads it and submits it (no GitHub key or sign-in inside the apps; owner, 2026-11-14).
/// </summary>
public static partial class IssueReport
{
    /// <summary>FIPinator Live's repository (the main app, and the shared code).</summary>
    public const string ProjectPage = "https://github.com/Pribilof-Hangar/fipinator-live";

    /// <summary>FIPinator 3000's repository (Live's accessory).</summary>
    public const string GaugeMakerPage = "https://github.com/Pribilof-Hangar/fipinator-3000";

    /// <summary>Keeps the pre-filled page well under what browsers and GitHub accept in a link.</summary>
    private const int MaxUrlLength = 7000;

    /// <summary>Hides the Windows profile folder, the user name and FIP serial numbers (keeps their last 4 characters).</summary>
    public static string Hide(string report, IEnumerable<string>? serials = null)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 3) report = report.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        var user = Environment.UserName;
        if (user.Length >= 3) report = Regex.Replace(report, $@"\b{Regex.Escape(user)}\b", "<user>", RegexOptions.IgnoreCase);
        foreach (var serial in (serials ?? []).Where(s => s.Length > 4)) report = report.Replace(serial, "..." + serial[^4..], StringComparison.OrdinalIgnoreCase);
        // FIP serials the caller didn't know about (e.g. in older log lines): two letters and eight hex digits.
        return FipSerial().Replace(report, m => "..." + m.Value[^4..]);
    }

    /// <summary>The last lines of today's log (both apps write to it), or a note why there are none.</summary>
    public static IReadOnlyList<string> LogTail(int lines)
    {
        try
        {
            if (!File.Exists(Log.CurrentFile)) return ["(no log yet today)"];
            using var stream = new FileStream(Log.CurrentFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var all = reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            return all.Skip(Math.Max(0, all.Count - lines)).ToList();
        }
        catch (IOException ex) { return [$"(log couldn't be read: {ex.Message})"]; }
    }

    /// <summary>A short report for an app without a running panel (FIPinator 3000): version, Windows, end of the log.</summary>
    public static string Simple(string app, string version)
    {
        var text = new StringBuilder();
        text.AppendLine($"{app} {version} problem report, {DateTime.Now:yyyy-MM-dd HH:mm}");
        text.AppendLine($"Windows: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        text.AppendLine();
        text.AppendLine("--- end of today's log ---");
        foreach (var line in LogTail(150)) text.AppendLine(line);
        return Hide(text.ToString());
    }

    /// <summary>
    /// GitHub's "New issue" page, filled in: a title, a place for the user to say what happened, the setup lines
    /// and the latest warnings and errors from the log (personal details hidden). Cut to fit a link; the full log
    /// can be attached on GitHub.
    /// </summary>
    public static string GitHubIssueUrl(string app, IEnumerable<string> setupLines, string projectPage = ProjectPage)
    {
        var setup = Hide(string.Join("\n", setupLines));
        var problems = LogTail(400).Where(l => l.Contains(" WARN ") || l.Contains(" ERROR ")).TakeLast(15).Select(l => Hide(l)).ToList();
        string Url(int keep)
        {
            var body = new StringBuilder();
            body.AppendLine("**What happened?** (what you did, what you saw, roughly when)");
            body.AppendLine();
            body.AppendLine();
            body.AppendLine("**Setup** (filled in by the app; personal details hidden)");
            body.AppendLine("```");
            body.AppendLine(setup);
            body.AppendLine("```");
            body.AppendLine("**Latest warnings and errors from the log**");
            body.AppendLine("```");
            foreach (var line in problems.TakeLast(keep)) body.AppendLine(line.Length > 300 ? line[..300] + "..." : line);
            if (keep == 0 || problems.Count == 0) body.AppendLine("(none)");
            body.AppendLine("```");
            body.AppendLine("The full log can be attached here: it's in the app's Logs folder.");
            return $"{projectPage}/issues/new?title={Uri.EscapeDataString($"{app}: ")}&body={Uri.EscapeDataString(body.ToString())}";
        }
        for (var keep = problems.Count; keep > 0; keep--)
        {
            var url = Url(keep);
            if (url.Length <= MaxUrlLength) return url;
        }
        return Url(0);
    }

    [GeneratedRegex(@"\b[A-Z]{2}[0-9A-F]{8}\b")]
    private static partial Regex FipSerial();
}
