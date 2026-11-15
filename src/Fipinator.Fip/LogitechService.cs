// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Runtime.InteropServices;

namespace Fipinator.Fip;

/// <summary>
/// Starts and stops Logitech's FIP service (DirectOutput) for "Share the FIPs with SPAD.neXt". Works without
/// a Windows prompt only after the one-time setup gave the signed-in user permission (see FipSharing);
/// <see cref="CanSwitch"/> says whether it has.
/// </summary>
public static class LogitechService
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004, ServiceStart = 0x0010, ServiceStop = 0x0020;
    private const uint Running = 4, Stopped = 1;

    /// <summary>The signed-in user may start and stop the service (the one-time setup was done).</summary>
    public static bool CanSwitch() => WithService(ServiceStart | ServiceStop | ServiceQueryStatus, _ => true) ?? false;

    /// <summary>Starts the service and waits until it runs (up to <paramref name="timeout"/>). True if it runs.</summary>
    public static bool Start(TimeSpan timeout) => WithService(ServiceStart | ServiceQueryStatus, service =>
    {
        if (State(service) == Running) return true;
        StartServiceW(service, 0, 0); // fails harmlessly if it's already starting
        return WaitFor(service, Running, timeout);
    }) ?? false;

    /// <summary>Stops the service and waits until it has stopped (up to <paramref name="timeout"/>). True if stopped.</summary>
    public static unsafe bool Stop(TimeSpan timeout) => WithService(ServiceStop | ServiceQueryStatus, service =>
    {
        if (State(service) == Stopped) return true;
        var status = stackalloc uint[7];
        ControlService(service, 1 /* SERVICE_CONTROL_STOP */, status);
        return WaitFor(service, Stopped, timeout);
    }) ?? false;

    private static bool WaitFor(nint service, uint state, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (State(service) == state) return true;
            Thread.Sleep(200);
        }
        return State(service) == state;
    }

    private static unsafe uint State(nint service)
    {
        var status = stackalloc uint[7]; // SERVICE_STATUS
        return QueryServiceStatus(service, status) != 0 ? status[1] : 0;
    }

    /// <summary>Runs <paramref name="action"/> with the service opened for <paramref name="access"/>; null if it can't be opened.</summary>
    private static bool? WithService(uint access, Func<nint, bool> action)
    {
        var manager = OpenSCManagerW(null, null, ScManagerConnect);
        if (manager == 0) return null;
        try
        {
            var service = OpenServiceW(manager, DirectOutput.ServiceName, access);
            if (service == 0) return null; // not installed, or no permission
            try { return action(service); }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint OpenSCManagerW(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint OpenServiceW(nint manager, string name, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StartServiceW(nint service, uint argCount, nint args);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern unsafe int ControlService(nint service, uint control, uint* status);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern unsafe int QueryServiceStatus(nint service, uint* status);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern int CloseServiceHandle(nint handle);
}
