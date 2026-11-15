// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using Fipinator.Core;
using Fipinator.Sim;

namespace Fipinator.Panel;

/// <summary>
/// Read-only diagnostics: writes a log line whenever an electrical or avionics switch changes in
/// the sim (avionics master, battery, generators, NAV/COM radio power, parking brake), so that
/// "the radios went off" can be traced afterwards. It never changes anything in the sim.
/// Uses its own SimConnect connections, split into groups, so an unknown variable name can't
/// disturb the gauges or the other groups.
/// </summary>
internal sealed class SwitchRecorder : IDisposable
{
    private sealed record Watch(string Label, SimVariable Variable, double Step);

    // Each group is its own connection. Names from the MSFS SimConnect variable list.
    private static readonly Watch[][] Groups =
    [
        [
            new("Avionics master", new("AVIONICS MASTER SWITCH", "bool"), 0.5),
            new("Battery master", new("ELECTRICAL MASTER BATTERY", "bool"), 0.5),
            new("Parking brake", new("BRAKE PARKING POSITION", "bool"), 0.5),
            new("Main bus volts", new("ELECTRICAL MAIN BUS VOLTAGE", "volts"), 3),
        ],
        [
            new("Generator 1", new("GENERAL ENG GENERATOR SWITCH:1", "bool"), 0.5),
            new("Generator 2", new("GENERAL ENG GENERATOR SWITCH:2", "bool"), 0.5),
            new("Generator 3", new("GENERAL ENG GENERATOR SWITCH:3", "bool"), 0.5),
            new("Generator 4", new("GENERAL ENG GENERATOR SWITCH:4", "bool"), 0.5),
        ],
        [
            new("NAV/COM 1 power", new("CIRCUIT NAVCOM1 ON", "bool"), 0.5),
            new("NAV/COM 2 power", new("CIRCUIT NAVCOM2 ON", "bool"), 0.5),
        ],
    ];

    private readonly List<(Watch[] Watches, SimConnection Sim, double?[] Last)> _groups = [];
    private readonly Thread _thread;
    private readonly CancellationTokenSource _stop = new();

    public SwitchRecorder()
    {
        foreach (var group in Groups)
        {
            var sim = new SimConnection(group.Select(w => w.Variable), "FIPinator 3000 recorder");
            sim.Warning += message => Log.Warn($"Switch recorder: {message}");
            sim.Start();
            _groups.Add((group, sim, new double?[group.Length]));
        }
        _thread = new Thread(Run) { IsBackground = true, Name = "Switch recorder" };
        _thread.Start();
    }

    private void Run()
    {
        while (!_stop.Token.WaitHandle.WaitOne(500))
        {
            foreach (var (watches, sim, last) in _groups)
            {
                if (sim.State != SimState.Connected)
                {
                    Array.Clear(last); // log the full state again after reconnecting
                    continue;
                }
                var values = sim.GetValues();
                for (var i = 0; i < watches.Length; i++)
                {
                    if (values[i] is not { } now) continue;
                    if (last[i] is { } before && Math.Abs(now - before) < watches[i].Step) continue;
                    var text = watches[i].Variable.Unit == "bool"
                        ? (now >= 0.5 ? "ON" : "OFF")
                        : now.ToString("0.0", CultureInfo.InvariantCulture);
                    Log.Info(last[i] is null ? $"Switches: {watches[i].Label} is {text}" : $"Switches: {watches[i].Label} -> {text}");
                    last[i] = now;
                }
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread.Join(TimeSpan.FromSeconds(2));
        foreach (var (_, sim, _) in _groups) sim.Dispose();
        _stop.Dispose();
    }
}
