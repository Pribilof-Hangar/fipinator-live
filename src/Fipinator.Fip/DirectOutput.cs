// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Fipinator.Fip;

/// <summary>
/// Connection to Logitech's DirectOutput, which drives the FIP screens.
/// Written from Logitech's DirectOutput SDK documentation (DirectOutput.htm, DirectOutput.h),
/// which is installed with the Logitech FIP software. DirectOutput.dll is loaded from where
/// Logitech's installer put it; FIPinator never ships it.
/// </summary>
public sealed unsafe class DirectOutput : IDisposable
{
    public const string DownloadPage = "https://support.logi.com/hc/en-us/articles/360025131294-Flight-Instrument-Panel-Drivers";
    public const string ServiceName = "SaiDOutput";

    // From DirectOutput.h.
    private static readonly Guid DeviceTypeFip = new("3E083CD8-6A37-4A58-80A8-3D6A2C07513E");
    internal const int S_OK = 0;
    internal const int E_HANDLE = unchecked((int)0x80070006);
    internal const int E_INVALIDARG = unchecked((int)0x80070057);
    internal const int E_UNEXPECTED = unchecked((int)0x8000FFFF);
    internal const int E_PAGENOTACTIVE = unchecked((int)0xFF040001);
    internal const int E_BUFFERTOOSMALL = unchecked((int)0xFF04006F);

    private readonly nint _library;
    private readonly GCHandle _self;
    private bool _initialized;
    private readonly Lock _lock = new();
    private readonly List<FipDevice> _devices = [];

    internal readonly delegate* unmanaged[Stdcall]<char*, int> Initialize;
    internal readonly delegate* unmanaged[Stdcall]<int> Deinitialize;
    internal readonly delegate* unmanaged[Stdcall]<delegate* unmanaged[Stdcall]<nint, byte, nint, void>, nint, int> RegisterDeviceCallback;
    internal readonly delegate* unmanaged[Stdcall]<delegate* unmanaged[Stdcall]<nint, nint, void>, nint, int> Enumerate;
    internal readonly delegate* unmanaged[Stdcall]<nint, Guid*, int> GetDeviceType;
    internal readonly delegate* unmanaged[Stdcall]<nint, char*, uint, int> GetSerialNumber;
    internal readonly delegate* unmanaged[Stdcall]<nint, delegate* unmanaged[Stdcall]<nint, uint, byte, nint, void>, nint, int> RegisterPageCallback;
    internal readonly delegate* unmanaged[Stdcall]<nint, delegate* unmanaged[Stdcall]<nint, uint, nint, void>, nint, int> RegisterSoftButtonCallback;
    internal readonly delegate* unmanaged[Stdcall]<nint, uint, char*, uint, int> AddPage;
    internal readonly delegate* unmanaged[Stdcall]<nint, uint, int> RemovePage;
    internal readonly delegate* unmanaged[Stdcall]<nint, uint, uint, uint, int> SetLed;
    internal readonly delegate* unmanaged[Stdcall]<nint, uint, uint, uint, void*, int> SetImage;

    /// <summary>A FIP was plugged in. Raised on a DirectOutput thread.</summary>
    public event Action<FipDevice>? DeviceAdded;

    /// <summary>A FIP was unplugged; don't use it any more. Raised on a DirectOutput thread.</summary>
    public event Action<FipDevice>? DeviceRemoved;

    /// <summary>Loads DirectOutput and connects to the Logitech DirectOutput service.</summary>
    /// <exception cref="FipException">The Logitech software is missing or its service isn't running.</exception>
    public DirectOutput(string appName = "FIPinator 3000")
    {
        var path = FindLibrary() ?? throw new FipException(
            "The Logitech FIP software (DirectOutput) isn't installed. Install it from Logitech's " +
            $"Flight Instrument Panel driver page, then try again: {DownloadPage}");

        try { _library = NativeLibrary.Load(path); }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            throw new FipException(
                "The Logitech FIP software is installed but couldn't be loaded. Reinstalling it from Logitech's " +
                $"Flight Instrument Panel driver page usually fixes this: {DownloadPage}", $"{path}: {ex.Message}");
        }

        try
        {
            Initialize = (delegate* unmanaged[Stdcall]<char*, int>)Export("DirectOutput_Initialize");
            Deinitialize = (delegate* unmanaged[Stdcall]<int>)Export("DirectOutput_Deinitialize");
            RegisterDeviceCallback = (delegate* unmanaged[Stdcall]<delegate* unmanaged[Stdcall]<nint, byte, nint, void>, nint, int>)Export("DirectOutput_RegisterDeviceCallback");
            Enumerate = (delegate* unmanaged[Stdcall]<delegate* unmanaged[Stdcall]<nint, nint, void>, nint, int>)Export("DirectOutput_Enumerate");
            GetDeviceType = (delegate* unmanaged[Stdcall]<nint, Guid*, int>)Export("DirectOutput_GetDeviceType");
            GetSerialNumber = (delegate* unmanaged[Stdcall]<nint, char*, uint, int>)Export("DirectOutput_GetSerialNumber");
            RegisterPageCallback = (delegate* unmanaged[Stdcall]<nint, delegate* unmanaged[Stdcall]<nint, uint, byte, nint, void>, nint, int>)Export("DirectOutput_RegisterPageCallback");
            RegisterSoftButtonCallback = (delegate* unmanaged[Stdcall]<nint, delegate* unmanaged[Stdcall]<nint, uint, nint, void>, nint, int>)Export("DirectOutput_RegisterSoftButtonCallback");
            AddPage = (delegate* unmanaged[Stdcall]<nint, uint, char*, uint, int>)Export("DirectOutput_AddPage");
            RemovePage = (delegate* unmanaged[Stdcall]<nint, uint, int>)Export("DirectOutput_RemovePage");
            SetLed = (delegate* unmanaged[Stdcall]<nint, uint, uint, uint, int>)Export("DirectOutput_SetLed");
            SetImage = (delegate* unmanaged[Stdcall]<nint, uint, uint, uint, void*, int>)Export("DirectOutput_SetImage");

            // Tested 2026-11-07 with DirectOutput 8.0.132: when the service is stopped, Initialize
            // waits indefinitely instead of returning E_HANDLE as documented. So check the service
            // first, and don't wait forever on Initialize either.
            if (!IsServiceRunning()) throw ServiceNotRunning("service status check");

            var hr = InitializeWithTimeout(appName, TimeSpan.FromSeconds(10));
            if (hr == E_HANDLE) throw ServiceNotRunning($"DirectOutput_Initialize returned {Hex(hr)}");
            Check(hr, "DirectOutput_Initialize");
            _initialized = true;

            _self = GCHandle.Alloc(this);
            Check(RegisterDeviceCallback(&OnDeviceChange, GCHandle.ToIntPtr(_self)), "DirectOutput_RegisterDeviceCallback");

            var found = new List<nint>();
            var foundHandle = GCHandle.Alloc(found);
            try { Check(Enumerate(&OnEnumerate, GCHandle.ToIntPtr(foundHandle)), "DirectOutput_Enumerate"); }
            finally { foundHandle.Free(); }

            foreach (var handle in found)
            {
                if (TryCreateDevice(handle) is not { } device) continue;
                lock (_lock)
                {
                    // The device callback may have added it already.
                    if (_devices.All(d => d.Handle != handle)) _devices.Add(device);
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>FIPs connected right now, in the order DirectOutput reported them.</summary>
    public IReadOnlyList<FipDevice> Devices
    {
        get { lock (_lock) return [.. _devices]; }
    }

    /// <summary>Full path of DirectOutput.dll as recorded by Logitech's installer, or null if not installed.</summary>
    public static string? FindLibrary()
    {
        foreach (var (key, value) in new[]
        {
            (@"SOFTWARE\Logitech\DirectOutput", "DirectOutput"),
            (@"SOFTWARE\Saitek\DirectOutput", "DirectOutput_Saitek"),
        })
        {
            try
            {
                using var reg = Registry.LocalMachine.OpenSubKey(key);
                if (reg?.GetValue(value) is string path && File.Exists(path)) return path;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Try the next place.
            }
        }
        return null;
    }

    private int InitializeWithTimeout(string appName, TimeSpan timeout)
    {
        var init = Initialize;
        var call = Task.Factory.StartNew(() =>
        {
            fixed (char* name = appName) return init(name);
        }, TaskCreationOptions.LongRunning);
        if (call.Wait(timeout)) return call.Result;

        throw new FipException(
            "The Logitech DirectOutput service didn't respond. Close SPAD.neXt (or any other FIP program), " +
            "restart the service, and try again.",
            $"DirectOutput_Initialize didn't return within {timeout.TotalSeconds:0} s");
    }

    private static FipException ServiceNotRunning(string details) => new(
        "The Logitech DirectOutput service isn't running, so FIPinator can't reach the FIPs. " +
        "Close SPAD.neXt (or any other FIP program), start the service, and try again.",
        details);

    /// <summary>True if Logitech's DirectOutput service is running; true as well if its state can't be read.</summary>
    public static bool IsServiceRunning()
    {
        var manager = OpenSCManagerW(null, null, 0x0001 /* SC_MANAGER_CONNECT */);
        if (manager == 0) return true; // can't tell; let Initialize decide
        try
        {
            var service = OpenServiceW(manager, ServiceName, 0x0004 /* SERVICE_QUERY_STATUS */);
            if (service == 0) return false; // not installed
            try
            {
                var status = stackalloc uint[7]; // SERVICE_STATUS
                if (QueryServiceStatus(service, status) == 0) return true;
                return status[1] == 4; // dwCurrentState == SERVICE_RUNNING
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint OpenSCManagerW(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint OpenServiceW(nint manager, string name, uint access);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern int QueryServiceStatus(nint service, uint* status);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern int CloseServiceHandle(nint handle);

    private FipDevice? TryCreateDevice(nint handle)
    {
        Guid type;
        if (GetDeviceType(handle, &type) != S_OK || type != DeviceTypeFip) return null; // e.g. an X52 Pro

        var buffer = stackalloc char[17];
        new Span<char>(buffer, 17).Clear();
        var serial = GetSerialNumber(handle, buffer, 16) == S_OK ? new string(buffer).Trim() : "";
        var device = new FipDevice(this, handle, serial);
        device.RegisterCallbacks();
        return device;
    }

    private nint Export(string name)
    {
        try { return NativeLibrary.GetExport(_library, name); }
        catch (EntryPointNotFoundException)
        {
            throw new FipException(
                "The installed Logitech FIP software is a version FIPinator doesn't recognize. Reinstalling it from " +
                $"Logitech's Flight Instrument Panel driver page may help: {DownloadPage}", $"Missing export {name}");
        }
    }

    internal static void Check(int hr, string call)
    {
        if (hr != S_OK)
            throw new FipException("The FIP didn't accept a command. Try unplugging and replugging it.", $"{call} returned {Hex(hr)}");
    }

    internal static string Hex(int hr) => $"0x{hr:X8}";

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnEnumerate(nint device, nint context)
    {
        try { ((List<nint>)GCHandle.FromIntPtr(context).Target!).Add(device); }
        catch { /* never let an exception cross into native code */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnDeviceChange(nint handle, byte added, nint context)
    {
        try
        {
            if (GCHandle.FromIntPtr(context).Target is not DirectOutput self) return;
            if (added != 0)
            {
                lock (self._lock)
                {
                    if (self._devices.Any(d => d.Handle == handle)) return;
                }
                if (self.TryCreateDevice(handle) is not { } device) return;
                lock (self._lock)
                {
                    if (self._devices.Any(d => d.Handle == handle)) return;
                    self._devices.Add(device);
                }
                self.DeviceAdded?.Invoke(device);
            }
            else
            {
                FipDevice? device;
                lock (self._lock)
                {
                    device = self._devices.FirstOrDefault(d => d.Handle == handle);
                    if (device is not null) self._devices.Remove(device);
                }
                if (device is not null)
                {
                    device.MarkRemoved();
                    self.DeviceRemoved?.Invoke(device);
                }
            }
        }
        catch { /* never let an exception cross into native code */ }
    }

    public void Dispose()
    {
        if (_initialized)
        {
            foreach (var device in Devices) device.Close();
            RegisterDeviceCallback(null, 0);
            Deinitialize();
            _initialized = false;
        }
        if (_self.IsAllocated) _self.Free();
        if (_library != 0) NativeLibrary.Free(_library);
    }
}
