// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fipinator.Fip;

/// <summary>FIP soft buttons and knobs, as bits in DirectOutput's button mask (DirectOutput.h).</summary>
[Flags]
public enum FipButtons : uint
{
    None = 0,
    RightKnobClockwise = 0x002,
    RightKnobAnticlockwise = 0x004,
    LeftKnobAnticlockwise = 0x008,
    LeftKnobClockwise = 0x010,
    Button1 = 0x020,
    Button2 = 0x040,
    Button3 = 0x080,
    Button4 = 0x100,
    Button5 = 0x200,
    Button6 = 0x400,
}

/// <summary>
/// One FIP. FIPinator shows its picture on a "page" it adds to the FIP; the user can flip to
/// other programs' pages with the FIP's page buttons, and the picture only shows while our page
/// is the active one.
/// </summary>
public sealed unsafe class FipDevice
{
    public const int Page = 1;

    private readonly DirectOutput _owner;
    private GCHandle _self;
    private bool _pageAdded;
    private volatile bool _removed;
    private volatile bool _pageActive;
    private int _pageCount;
    private volatile int _activePage;

    /// <summary>
    /// Our page now showing (1, 2, 3...), or 0 when none of ours is (another program's page, or
    /// the FIP's own screen). With several pages, the FIP's page up/down buttons step through them.
    /// </summary>
    public int ActivePage => _activePage;

    /// <summary>How many pages FIPinator has added to this FIP.</summary>
    public int PageCount => _pageCount;

    /// <summary>One of our pages was shown (true) or hidden (false). Raised on a DirectOutput thread.</summary>
    public event Action<FipDevice, int, bool>? PageChanged;

    internal FipDevice(DirectOutput owner, nint handle, string serial)
    {
        _owner = owner;
        Handle = handle;
        SerialNumber = serial;
    }

    internal nint Handle { get; }

    /// <summary>Serial number reported by the FIP; may be empty.</summary>
    public string SerialNumber { get; }

    /// <summary>True once the FIP has been unplugged.</summary>
    public bool IsRemoved => _removed;

    /// <summary>Frames DirectOutput refused with an unexpected error and that were skipped.</summary>
    public int SkippedFrames { get; private set; }

    /// <summary>True while FIPinator's page is the one shown on this FIP.</summary>
    public bool IsPageActive => _pageActive;

    /// <summary>Buttons or knobs changed. Only sent while our page is active. Raised on a DirectOutput thread.</summary>
    public event Action<FipDevice, FipButtons>? ButtonsChanged;

    /// <summary>Our page became visible (true) or hidden (false). Raised on a DirectOutput thread.</summary>
    public event Action<FipDevice, bool>? PageActiveChanged;

    internal void RegisterCallbacks()
    {
        _self = GCHandle.Alloc(this);
        var ctx = GCHandle.ToIntPtr(_self);
        DirectOutput.Check(_owner.RegisterPageCallback(Handle, &OnPage, ctx), "DirectOutput_RegisterPageCallback");
        DirectOutput.Check(_owner.RegisterSoftButtonCallback(Handle, &OnButtons, ctx), "DirectOutput_RegisterSoftButtonCallback");
    }

    /// <summary>Adds FIPinator's page to this FIP and makes it the one shown.</summary>
    public void Open() => OpenPages(1);

    /// <summary>
    /// Adds <paramref name="count"/> pages (1..count) to this FIP and shows page 1. The FIP's page
    /// up/down buttons then step through them; <see cref="PageChanged"/> says which one is up.
    /// Pages are added one after another (DirectOutput fails when they're added at the same time).
    /// </summary>
    public void OpenPages(int count, int startPage = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ThrowIfRemoved();
        if (_pageAdded) return;
        if (startPage < 1 || startPage > count) startPage = 1;
        // Tested 2026-11-09 (DirectOutput 8.0.132, 7 FIPs): each page added becomes the one shown,
        // even without FLAG_SET_AS_ACTIVE. So add the start page last, and it ends up showing.
        _pageCount = count;
        var order = Enumerable.Range(1, count).Where(p => p != startPage).Reverse().Append(startPage);
        foreach (var page in order)
        {
            int hr;
            fixed (char* name = $"FIPinator Live ({page})") hr = _owner.AddPage(Handle, (uint)page, name, page == startPage ? 1u /* FLAG_SET_AS_ACTIVE */ : 0u);
            DirectOutput.Check(hr, "DirectOutput_AddPage");
        }
        _pageAdded = true;
        _pageActive = true; // DirectOutput doesn't send a page callback for FLAG_SET_AS_ACTIVE.
        _activePage = startPage;
    }

    /// <summary>
    /// Shows a picture. <paramref name="image"/> must be in the FIP's layout; see Fipinator.Core.FipImage.
    /// Returns false if the frame wasn't shown: our page isn't the one showing right now, or
    /// DirectOutput refused this one frame (see <see cref="SkippedFrames"/>); try again next frame.
    /// </summary>
    public bool Show(ReadOnlySpan<byte> image) => Show(Page, image);

    /// <summary>Shows a picture on one of our pages; see <see cref="Show(ReadOnlySpan{byte})"/>.</summary>
    public bool Show(int page, ReadOnlySpan<byte> image)
    {
        ThrowIfRemoved();
        if (!_pageAdded) throw new InvalidOperationException("Call Open() before Show().");
        if (image.Length != 320 * 240 * 3) throw new ArgumentException("FIP images must be 230400 bytes.", nameof(image));

        int hr;
        fixed (byte* p = image) hr = _owner.SetImage(Handle, (uint)page, 0, (uint)image.Length, p);
        if (hr == DirectOutput.E_PAGENOTACTIVE)
        {
            if (page == _activePage) { _pageActive = false; _activePage = 0; }
            return false;
        }
        if (hr == DirectOutput.E_UNEXPECTED)
        {
            // Seen 2026-11-07 with 7 FIPs updating at once: an occasional frame fails with
            // E_UNEXPECTED and the next one goes through. Skip the frame instead of giving up.
            SkippedFrames++;
            return false;
        }
        if (hr == DirectOutput.E_HANDLE) MarkRemoved();
        ThrowIfRemoved();
        DirectOutput.Check(hr, "DirectOutput_SetImage");
        return true;
    }

    /// <summary>Turns the light behind soft button 1–6 on or off. Returns false if our page isn't shown.</summary>
    public bool SetButtonLight(int button, bool on)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(button, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(button, 6);
        ThrowIfRemoved();
        var hr = _owner.SetLed(Handle, (uint)Math.Max(1, _activePage), (uint)(button - 1), on ? 1u : 0u);
        if (hr == DirectOutput.E_PAGENOTACTIVE) return false;
        DirectOutput.Check(hr, "DirectOutput_SetLed");
        return true;
    }

    /// <summary>Removes our page and stops callbacks. Safe to call more than once.</summary>
    internal void Close()
    {
        if (!_removed)
        {
            _owner.RegisterPageCallback(Handle, null, 0);
            _owner.RegisterSoftButtonCallback(Handle, null, 0);
            if (_pageAdded) for (var page = 1; page <= _pageCount; page++) _owner.RemovePage(Handle, (uint)page);
        }
        _pageAdded = false;
        _pageCount = 0;
        _activePage = 0;
        if (_self.IsAllocated) _self.Free();
    }

    internal void MarkRemoved()
    {
        _removed = true;
        _pageActive = false;
    }

    private void ThrowIfRemoved()
    {
        if (_removed) throw new FipException("The FIP was unplugged. Plug it back in and try again.");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnPage(nint handle, uint page, byte active, nint context)
    {
        try
        {
            if (GCHandle.FromIntPtr(context).Target is not FipDevice self || page < 1 || page > self._pageCount) return;
            var on = active != 0;
            var wasShowing = self._pageActive;
            if (on) self._activePage = (int)page;
            else if (self._activePage == (int)page) self._activePage = 0;
            self._pageActive = self._activePage != 0;
            self.PageChanged?.Invoke(self, (int)page, on);
            if (self._pageActive != wasShowing) self.PageActiveChanged?.Invoke(self, self._pageActive);
        }
        catch { /* never let an exception cross into native code */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnButtons(nint handle, uint buttons, nint context)
    {
        try
        {
            if (GCHandle.FromIntPtr(context).Target is FipDevice self) self.ButtonsChanged?.Invoke(self, (FipButtons)buttons);
        }
        catch { /* never let an exception cross into native code */ }
    }
}
