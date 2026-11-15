// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;

namespace Fipinator.Core;

/// <summary>
/// Simple diagnostic log: one file per day in %LOCALAPPDATA%\FIPinator 3000\logs, kept for 7 days.
/// Holds technical details (error codes, timings) so the screen can stay plain. No personal data.
/// Logging never throws: if the file can't be written, the message is just not saved.
/// </summary>
public static class Log
{
    private static readonly Lock Gate = new();

    public static string Folder { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FIPinator 3000", "logs");

    /// <summary>
    /// Sends the log somewhere else. Only for automated tests, so their made-up panels and serials
    /// never land in the user's real log (or a problem report). The apps never call this.
    /// </summary>
    public static void UseFolder(string folder)
    {
        lock (Gate) Folder = folder;
    }

    public static string CurrentFile => Path.Combine(Folder, $"fipinator-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {level,-5} {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(CurrentFile, line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Deletes log files older than 7 days.</summary>
    public static void Tidy()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var file in Directory.GetFiles(Folder, "fipinator-*.log"))
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-7)) File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
