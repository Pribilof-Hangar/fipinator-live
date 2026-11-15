// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Diagnostics;
using System.Text.Json;
using Fipinator.Core;
using Fipinator.Fip;

namespace Fipinator.App;

/// <summary>
/// The one-time setup (and undo) for "Share the FIPs with SPAD.neXt". FIPinator Live runs itself a second
/// time with administrator rights (Windows asks once); that copy only changes the Logitech FIP service:
/// lets the signed-in user start and stop it, and sets it to start only when asked. Before changing
/// anything it saves how the service was set, so undo can put it back exactly.
/// </summary>
internal static class FipSharingSetup
{
    public const string SetupArg = "--share-fips-setup", UndoArg = "--share-fips-undo";

    private static string BackupFile => Path.Combine(PanelLibrary.ForCurrentUser().Folder, "fip-sharing-backup.json");

    /// <summary>Asks Windows for permission and runs the setup (or undo). Returns null if it worked, else a plain message.</summary>
    public static string? Run(bool setup)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                ArgumentList = { setup ? SetupArg : UndoArg, BackupFile },
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (p is null) return "Windows didn't start the setup. Try again.";
            if (!p.WaitForExit(30000)) return "The setup took too long. Try again; details are in the log.";
            if (p.ExitCode == 0) return null;
            Log.Warn($"FIP sharing {(setup ? "setup" : "undo")} failed with code {p.ExitCode}.");
            return "That didn't work. Nothing was changed. Details are in the log folder.";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "Windows' permission was declined, so nothing was changed."; // the user said No
        }
    }

    /// <summary>The administrator copy: does the change and quits. Exit code 0 = done.</summary>
    public static int RunElevatedStep(string step, string backupFile)
    {
        try
        {
            var service = DirectOutput.ServiceName;
            if (step == SetupArg)
            {
                // Keep the very first backup (the service as it was before FIPinator ever touched it).
                if (!File.Exists(backupFile))
                {
                    var sddl = Sc("sdshow", service).Trim();
                    var start = FipSharing.StartType(Sc("qc", service)) ?? "auto";
                    Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                    File.WriteAllText(backupFile, JsonSerializer.Serialize(new FipSharing.Backup(service, sddl, start, DateTime.Now), GaugeDefinition.JsonOptions));
                }
                var now = Sc("sdshow", service).Trim();
                Sc("sdset", service, FipSharing.AddStartStop(now));
                Sc("config", service, "start=", "demand");
                Log.Info("FIP sharing set up: the signed-in user may start/stop the Logitech service; it starts when asked.");
                return 0;
            }
            if (step == UndoArg)
            {
                if (!File.Exists(backupFile)) return 3;
                var backup = JsonSerializer.Deserialize<FipSharing.Backup>(File.ReadAllText(backupFile), GaugeDefinition.JsonOptions)!;
                Sc("sdset", backup.Service, backup.Sddl);
                Sc("config", backup.Service, "start=", backup.StartType);
                File.Delete(backupFile);
                Log.Info("FIP sharing undone: the Logitech service is back as it was.");
                return 0;
            }
            return 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or JsonException or System.ComponentModel.Win32Exception)
        {
            Log.Error($"FIP sharing {step}: {ex}");
            return 1;
        }
    }

    /// <summary>Runs Windows' own sc.exe and returns what it printed; throws if it reports a failure.</summary>
    private static string Sc(params string[] args)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"))
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        if (p.ExitCode != 0) throw new InvalidOperationException($"sc {string.Join(' ', args.Take(2))} failed ({p.ExitCode}): {output.Trim()}");
        return output;
    }

    /// <summary>Sharing is set up and still works (a Logitech update may have reset the service).</summary>
    public static bool IsSetUp => File.Exists(BackupFile) && LogitechService.CanSwitch();
}
