// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Runtime.InteropServices;
using System.Text;

namespace Fipinator.Sim;

/// <summary>
/// A value to read from the sim: a SimConnect variable name (or L: name) and its unit, or a field of the PMDG 737's
/// data block ("PMDG:MAIN_AutobrakeSelector"; see <see cref="PmdgNg3"/>).
/// </summary>
public sealed record SimVariable(string Name, string Unit);

public enum SimState
{
    /// <summary>Trying to reach the sim; it isn't running, or is still starting.</summary>
    Waiting,
    /// <summary>Connected and receiving values.</summary>
    Connected,
    /// <summary>Can't work at all (for example SimConnect is missing). Won't retry.</summary>
    Failed,
}

/// <summary>
/// Keeps a connection to MSFS 2024 through SimConnect and holds the latest values.
/// Written from Microsoft's SimConnect header (SimConnect.h, MSFS 2024 SDK). It connects on a
/// background thread, retries every few seconds while the sim isn't running, and reconnects
/// after the sim quits. It only reads; it never changes anything in the sim.
/// </summary>
public sealed unsafe class SimConnection : IDisposable
{
    private const int S_OK = 0;
    private const uint RecvException = 1, RecvOpen = 2, RecvQuit = 3, RecvSimObjectData = 8, RecvClientData = 16;
    private const uint ExceptionNameUnrecognized = 7;
    private const uint DataTypeFloat64 = 4;
    private const uint PeriodSimFrame = 3;
    private const uint RequestFlagChanged = 1;
    private const uint UserAircraft = 0;
    private const uint DefinitionId = 1, RequestId = 1;
    // The PMDG 737 data block (client data): its own ID, definition and request.
    private const uint PmdgClientDataId = 1, PmdgDefinitionId = 2, PmdgRequestId = 2;
    private const uint ClientDataPeriodOnSet = 3, ClientDataFlagChanged = 1;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly SimVariable[] _variables;
    private readonly string _appName;
    private readonly double[] _values;
    private readonly bool[] _received;
    private readonly int[] _simIndexes;  // positions in _variables of the plain sim values, in request order
    private readonly int[] _pmdgIndexes; // positions in _variables of PMDG data block fields
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _stop = new();
    private Thread? _thread;
    private nint _library;

    private delegate* unmanaged<nint*, byte*, nint, uint, nint, uint, int> _open;
    private delegate* unmanaged<nint, int> _close;
    private delegate* unmanaged<nint, uint, byte*, byte*, uint, float, uint, int> _addToDataDefinition;
    private delegate* unmanaged<nint, uint, uint, uint, uint, uint, uint, uint, uint, int> _requestDataOnSimObject;
    private delegate* unmanaged<nint, byte**, uint*, int> _getNextDispatch;
    private delegate* unmanaged<nint, uint*, int> _getLastSentPacketId;
    private delegate* unmanaged<nint, uint, byte*, int> _mapClientEventToSimEvent;
    private delegate* unmanaged<nint, uint, uint, uint, uint, uint, int> _transmitClientEvent;
    private delegate* unmanaged<nint, byte*, uint, int> _mapClientDataNameToId;
    private delegate* unmanaged<nint, uint, uint, uint, float, uint, int> _addToClientDataDefinition;
    private delegate* unmanaged<nint, uint, uint, uint, uint, uint, uint, uint, uint, int> _requestClientData;
    private delegate* unmanaged<nint, uint, uint, uint, uint, uint, void*, int> _setDataOnSimObject;

    // Knobs that change a plane's own value (L:): clicks wait here; each value gets its own definition, is read once,
    // and written back with the clicks added.
    private const uint PeriodOnce = 1, ValueDefinitionBase = 100, ValueRequestBase = 100;
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Name, double Delta)> _adjustments = new();
    private const uint GroupPriorityHighest = 1;
    private const uint EventFlagGroupIdIsPriority = 0x10;
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Name, uint Data)> _outgoing = new();
    private readonly AutoResetEvent _wake = new(false);

    public SimConnection(IEnumerable<SimVariable> variables, string appName = "FIPinator 3000")
    {
        _variables = [.. variables];
        _appName = appName;
        _values = new double[_variables.Length];
        _received = new bool[_variables.Length];
        _simIndexes = [.. Enumerable.Range(0, _variables.Length).Where(i => !PmdgNg3.IsPmdg(_variables[i].Name))];
        _pmdgIndexes = [.. Enumerable.Range(0, _variables.Length).Where(i => PmdgNg3.IsPmdg(_variables[i].Name))];
    }

    // Timing: how long the sim went without sending values (a sim stutter looks like this on the FIPs).
    private readonly System.Diagnostics.Stopwatch _sinceData = new();
    private int _pauses;
    private double _longestPauseMs;

    /// <summary>
    /// Pauses of more than 150 ms between batches of sim values since the last call, and the longest one, for the log.
    /// The values are asked for "when they change", so a pause can also just mean nothing changed (parked).
    /// </summary>
    public (int Count, double LongestMs) TakePauses()
    {
        lock (_lock)
        {
            var result = (_pauses, _longestPauseMs);
            (_pauses, _longestPauseMs) = (0, 0);
            return result;
        }
    }

    /// <summary>True once a PMDG 737 data block has arrived on this connection (the PMDG is loaded and sharing).</summary>
    public bool PmdgDataReceived { get; private set; }

    public IReadOnlyList<SimVariable> Variables => _variables;

    public SimState State { get; private set; } = SimState.Waiting;

    /// <summary>Name and version the sim reports about itself, for logs. Empty until connected.</summary>
    public string SimName { get; private set; } = "";

    /// <summary>Plain-language description of the current state, for the user.</summary>
    public string StatusMessage { get; private set; } = "Not started.";

    /// <summary>Counts every batch of values received; lets callers tell whether anything changed.</summary>
    public long Version { get; private set; }

    /// <summary>State or message changed. Raised on the connection's background thread.</summary>
    public event Action<SimState, string>? StatusChanged;

    /// <summary>A problem worth logging that doesn't stop the connection (for example an unknown variable name).</summary>
    public event Action<string>? Warning;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "SimConnect" };
        _thread.Start();
    }

    /// <summary>
    /// Sends a sim event (for example KOHLSMAN_INC) to the user's aircraft. Queued and sent on the
    /// connection's thread; dropped if the sim isn't connected, so old clicks don't pile up.
    /// </summary>
    public void SendEvent(string name, uint data = 0)
    {
        if (State != SimState.Connected) return;
        _outgoing.Enqueue((name, data));
        _wake.Set();
    }

    /// <summary>
    /// Adds <paramref name="delta"/> to a plane's own value (an L: name), for knobs that are values rather than sim
    /// events. Queued like <see cref="SendEvent"/>; dropped if the sim isn't connected.
    /// </summary>
    public void AddToValue(string name, double delta)
    {
        if (State != SimState.Connected) return;
        _adjustments.Enqueue((name, delta));
        _wake.Set();
    }

    /// <summary>Latest value of each variable, in the order given; null for any not received yet.</summary>
    public double?[] GetValues()
    {
        lock (_lock)
            return [.. _values.Select((v, i) => _received[i] ? v : (double?)null)];
    }

    /// <summary>
    /// Where SimConnect.dll is. For now FIPinator uses the copy in the MSFS SDK on this PC, found
    /// through the folders the SDK installer records; whether FIPinator may ship SimConnect.dll
    /// itself is still an open licensing question.
    /// </summary>
    public static string? FindLibrary()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "SimConnect.dll");
        if (File.Exists(beside)) return beside;
        foreach (var variable in new[] { "MSFS2024_SDK", "MSFS_SDK" })
        {
            var sdk = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(sdk)) continue;
            var path = Path.Combine(sdk, "SimConnect SDK", "lib", "SimConnect.dll");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private void Run()
    {
        if (!TryLoad()) return;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                RunOneConnection();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Warning?.Invoke($"SimConnect connection error: {ex.Message}");
            }
            if (_stop.IsCancellationRequested) break;
            _stop.Token.WaitHandle.WaitOne(RetryDelay);
        }
    }

    private bool TryLoad()
    {
        var path = FindLibrary();
        if (path is null)
        {
            SetStatus(SimState.Failed,
                "SimConnect isn't available. FIPinator currently needs the MSFS 2024 SDK installed " +
                "(it provides SimConnect.dll).");
            return false;
        }
        try
        {
            _library = NativeLibrary.Load(path);
            _open = (delegate* unmanaged<nint*, byte*, nint, uint, nint, uint, int>)NativeLibrary.GetExport(_library, "SimConnect_Open");
            _close = (delegate* unmanaged<nint, int>)NativeLibrary.GetExport(_library, "SimConnect_Close");
            _addToDataDefinition = (delegate* unmanaged<nint, uint, byte*, byte*, uint, float, uint, int>)NativeLibrary.GetExport(_library, "SimConnect_AddToDataDefinition");
            _requestDataOnSimObject = (delegate* unmanaged<nint, uint, uint, uint, uint, uint, uint, uint, uint, int>)NativeLibrary.GetExport(_library, "SimConnect_RequestDataOnSimObject");
            _getNextDispatch = (delegate* unmanaged<nint, byte**, uint*, int>)NativeLibrary.GetExport(_library, "SimConnect_GetNextDispatch");
            _getLastSentPacketId = (delegate* unmanaged<nint, uint*, int>)NativeLibrary.GetExport(_library, "SimConnect_GetLastSentPacketID");
            _mapClientEventToSimEvent = (delegate* unmanaged<nint, uint, byte*, int>)NativeLibrary.GetExport(_library, "SimConnect_MapClientEventToSimEvent");
            _transmitClientEvent = (delegate* unmanaged<nint, uint, uint, uint, uint, uint, int>)NativeLibrary.GetExport(_library, "SimConnect_TransmitClientEvent");
            _mapClientDataNameToId = (delegate* unmanaged<nint, byte*, uint, int>)NativeLibrary.GetExport(_library, "SimConnect_MapClientDataNameToID");
            _addToClientDataDefinition = (delegate* unmanaged<nint, uint, uint, uint, float, uint, int>)NativeLibrary.GetExport(_library, "SimConnect_AddToClientDataDefinition");
            _requestClientData = (delegate* unmanaged<nint, uint, uint, uint, uint, uint, uint, uint, uint, int>)NativeLibrary.GetExport(_library, "SimConnect_RequestClientData");
            _setDataOnSimObject = (delegate* unmanaged<nint, uint, uint, uint, uint, uint, void*, int>)NativeLibrary.GetExport(_library, "SimConnect_SetDataOnSimObject");
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            SetStatus(SimState.Failed, $"SimConnect couldn't be loaded from {path}. Reinstalling the MSFS 2024 SDK may fix this. ({ex.Message})");
            return false;
        }
    }

    private void RunOneConnection()
    {
        using var dataReady = new AutoResetEvent(false);
        nint handle;
        int hr;
        fixed (byte* name = Ascii(_appName))
            hr = _open(&handle, name, 0, 0, dataReady.SafeWaitHandle.DangerousGetHandle(), 0);
        if (hr != S_OK)
        {
            SetStatus(SimState.Waiting, "Waiting for MSFS 2024. Start the sim; FIPinator connects by itself.");
            return;
        }

        try
        {
            // Remember which request defined which variable, so an error can name it.
            var sendIds = new Dictionary<uint, SimVariable>();
            foreach (var variable in _simIndexes.Select(i => _variables[i]))
            {
                fixed (byte* datum = Ascii(variable.Name))
                fixed (byte* unit = Ascii(variable.Unit))
                    Check(_addToDataDefinition(handle, DefinitionId, datum, unit, DataTypeFloat64, 0, uint.MaxValue), "AddToDataDefinition");
                uint sendId;
                if (_getLastSentPacketId(handle, &sendId) == S_OK) sendIds[sendId] = variable;
            }
            if (_simIndexes.Length > 0)
                Check(_requestDataOnSimObject(handle, RequestId, DefinitionId, UserAircraft, PeriodSimFrame, RequestFlagChanged, 0, 0, 0), "RequestDataOnSimObject");

            // PMDG 737 fields: ask for PMDG's whole data block; it arrives whenever PMDG updates it (only while the
            // PMDG is loaded with data sharing switched on in its options file).
            PmdgDataReceived = false;
            if (_pmdgIndexes.Length > 0)
            {
                foreach (var unknown in _pmdgIndexes.Select(i => _variables[i].Name).Where(n => !PmdgNg3.IsKnown(n)))
                    Warning?.Invoke($"'{unknown}' isn't a 737-800 add-on value FIPinator knows (PMDG: values). Check the spelling in the gauge file.");
                fixed (byte* clientName = Ascii(PmdgNg3.ClientDataName))
                    Check(_mapClientDataNameToId(handle, clientName, PmdgClientDataId), "MapClientDataNameToID");
                Check(_addToClientDataDefinition(handle, PmdgDefinitionId, 0, PmdgNg3.BlockSize, 0, uint.MaxValue), "AddToClientDataDefinition");
                Check(_requestClientData(handle, PmdgClientDataId, PmdgRequestId, PmdgDefinitionId, ClientDataPeriodOnSet, ClientDataFlagChanged, 0, 0, 0), "RequestClientData");
            }

            // Sim events get client IDs the first time they're used on this connection.
            var eventIds = new Dictionary<string, uint>(StringComparer.Ordinal);
            while (_outgoing.TryDequeue(out _)) { } // clicks from before this connection are stale
            while (_adjustments.TryDequeue(out _)) { }
            var valueIds = new Dictionary<string, uint>(StringComparer.Ordinal); // L: name -> its definition/request id
            var pending = new Dictionary<uint, (string Name, double Delta)>();    // id -> clicks waiting for the value
            // The value as we last wrote it, and when: some planes count each change as one knob click, so every click
            // is written on its own; we re-read it after a pause in case the cockpit moved it meanwhile.
            var known = new Dictionary<uint, (double Value, DateTime At)>();

            while (!_stop.IsCancellationRequested)
            {
                WaitHandle.WaitAny([dataReady, _wake, _stop.Token.WaitHandle], 1000);
                while (_outgoing.TryDequeue(out var outgoing))
                {
                    if (!eventIds.TryGetValue(outgoing.Name, out var eventId))
                    {
                        eventId = (uint)eventIds.Count + 100;
                        int mapped;
                        fixed (byte* eventName = Ascii(outgoing.Name)) mapped = _mapClientEventToSimEvent(handle, eventId, eventName);
                        if (mapped != S_OK) { Warning?.Invoke($"Couldn't set up sim event {outgoing.Name} (0x{mapped:X8})."); continue; }
                        uint sendId;
                        if (_getLastSentPacketId(handle, &sendId) == S_OK) sendIds[sendId] = new SimVariable(outgoing.Name, "event");
                        eventIds[outgoing.Name] = eventId;
                    }
                    var sent = _transmitClientEvent(handle, UserAircraft, eventId, outgoing.Data, GroupPriorityHighest, EventFlagGroupIdIsPriority);
                    if (sent != S_OK) Warning?.Invoke($"Couldn't send sim event {outgoing.Name} (0x{sent:X8}).");
                }
                while (_adjustments.TryDequeue(out var adjust))
                {
                    if (!valueIds.TryGetValue(adjust.Name, out var valueId))
                    {
                        valueId = (uint)valueIds.Count;
                        fixed (byte* datum = Ascii(adjust.Name))
                        fixed (byte* unit = Ascii("number"))
                            if (_addToDataDefinition(handle, ValueDefinitionBase + valueId, datum, unit, DataTypeFloat64, 0, uint.MaxValue) != S_OK)
                            {
                                Warning?.Invoke($"Couldn't set up {adjust.Name}.");
                                continue;
                            }
                        valueIds[adjust.Name] = valueId;
                    }
                    if (!pending.ContainsKey(valueId) && known.TryGetValue(valueId, out var last) && DateTime.UtcNow - last.At < TimeSpan.FromSeconds(1))
                    {
                        var next = last.Value + adjust.Delta;
                        if (_setDataOnSimObject(handle, ValueDefinitionBase + valueId, UserAircraft, 0, 0, sizeof(double), &next) != S_OK)
                            Warning?.Invoke($"Couldn't change {adjust.Name}.");
                        known[valueId] = (next, DateTime.UtcNow);
                        continue;
                    }
                    var waiting = pending.TryGetValue(valueId, out var already);
                    pending[valueId] = (adjust.Name, (waiting ? already.Delta : 0) + adjust.Delta);
                    if (!waiting) // ask for its current value; the answer adds the clicks and writes it back
                        _requestDataOnSimObject(handle, ValueRequestBase + valueId, ValueDefinitionBase + valueId, UserAircraft, PeriodOnce, 0, 0, 0, 0);
                }
                byte* data;
                uint size;
                while (_getNextDispatch(handle, &data, &size) == S_OK)
                {
                    if (size >= 48 && *(uint*)(data + 8) == RecvSimObjectData && *(uint*)(data + 12) is var rid && rid >= ValueRequestBase
                        && pending.Remove(rid - ValueRequestBase, out var click))
                    {
                        var value = *(double*)(data + 40) + click.Delta;
                        var written = _setDataOnSimObject(handle, ValueDefinitionBase + (rid - ValueRequestBase), UserAircraft, 0, 0, sizeof(double), &value);
                        if (written != S_OK) Warning?.Invoke($"Couldn't change {click.Name} (0x{written:X8}).");
                        known[rid - ValueRequestBase] = (value, DateTime.UtcNow);
                        continue;
                    }
                    if (!Handle(data, size, sendIds)) return; // sim quit
                }
            }
        }
        finally
        {
            _close(handle);
            lock (_lock) Array.Clear(_received);
        }
    }

    /// <summary>Handles one message from the sim. Returns false when the sim has quit.</summary>
    private bool Handle(byte* data, uint size, Dictionary<uint, SimVariable> sendIds)
    {
        if (size < 12) return true;
        var id = *(uint*)(data + 8);
        switch (id)
        {
            case RecvOpen:
                var app = Encoding.ASCII.GetString(data + 12, 256).TrimEnd('\0');
                var major = *(uint*)(data + 12 + 256);
                var minor = *(uint*)(data + 12 + 260);
                // MSFS 2024 reports an internal codename here ("SunRise"), so keep it out of the
                // user-facing text; it's in SimName for logs.
                SimName = $"{app} {major}.{minor}";
                SetStatus(SimState.Connected, "Connected to the flight simulator.");
                break;

            case RecvQuit:
                SetStatus(SimState.Waiting, "MSFS 2024 closed. Waiting for it to start again.");
                return false;

            case RecvException when size >= 24:
                var exception = *(uint*)(data + 12);
                var sendId = *(uint*)(data + 16);
                var what = sendIds.TryGetValue(sendId, out var variable)
                    ? variable.Unit == "event" ? $" for event '{variable.Name}'" : $" for '{variable.Name}' ({variable.Unit})"
                    : "";
                Warning?.Invoke(exception == ExceptionNameUnrecognized
                    ? $"The sim doesn't recognize the name{what}. Check the spelling in the gauge file."
                    : $"The sim reported problem {exception}{what}.");
                break;

            case RecvSimObjectData when size >= 40:
                var requestId = *(uint*)(data + 12);
                var count = *(uint*)(data + 36);
                if (requestId != RequestId || count != _simIndexes.Length || size < 40 + count * 8) break;
                lock (_lock)
                {
                    for (var i = 0; i < count; i++)
                    {
                        _values[_simIndexes[i]] = *(double*)(data + 40 + i * 8);
                        _received[_simIndexes[i]] = true;
                    }
                    Version++;
                    var gap = _sinceData.Elapsed.TotalMilliseconds;
                    if (_sinceData.IsRunning && gap > 150) { _pauses++; _longestPauseMs = Math.Max(_longestPauseMs, gap); }
                    _sinceData.Restart();
                }
                break;

            case RecvClientData when size >= 40 + PmdgNg3.BlockSize && *(uint*)(data + 12) == PmdgRequestId:
                var block = new ReadOnlySpan<byte>(data + 40, PmdgNg3.BlockSize);
                lock (_lock)
                {
                    foreach (var i in _pmdgIndexes)
                    {
                        if (PmdgNg3.Read(block, _variables[i].Name) is not { } value) continue;
                        _values[i] = value;
                        _received[i] = true;
                    }
                    PmdgDataReceived = true;
                    Version++;
                }
                break;
        }
        return true;
    }

    private static void Check(int hr, string call)
    {
        if (hr != S_OK) throw new InvalidOperationException($"SimConnect_{call} returned 0x{hr:X8}");
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text + '\0');

    private void SetStatus(SimState state, string message)
    {
        if (State == state && StatusMessage == message) return;
        State = state;
        StatusMessage = message;
        StatusChanged?.Invoke(state, message);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(3));
        if (_thread is { IsAlive: true }) return; // stuck in a SimConnect call; the process exit cleans up
        if (_library != 0) NativeLibrary.Free(_library);
        _stop.Dispose();
        _wake.Dispose();
    }
}
