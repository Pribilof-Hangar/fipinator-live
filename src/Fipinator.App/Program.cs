// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;

namespace Fipinator.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // The administrator copy for "Share the FIPs with SPAD.neXt" setup/undo: change the service, then quit.
        if (args.Length == 2 && args[0] is FipSharingSetup.SetupArg or FipSharingSetup.UndoArg)
            return FipSharingSetup.RunElevatedStep(args[0], args[1]);
        Run();
        return 0;
    }

    private static void Run()
    {
        // Only one FIPinator at a time: two would fight over the FIPs.
        using var single = new Mutex(initiallyOwned: true, @"Local\FIPinator3000.App", out var first);
        if (!first)
        {
            MessageBox.Show("FIPinator Live is already running. Look for its window or its icon on the taskbar.",
                "FIPinator Live", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Any unexpected error in the window: log the details, show a plain message, keep running.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            Log.Error($"Unexpected error in the window: {e.Exception}");
            MessageBox.Show("Something unexpected went wrong. FIPinator Live keeps running; the details are in the log file (Open log folder).",
                "FIPinator Live", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error($"Unexpected error: {e.ExceptionObject}");

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
