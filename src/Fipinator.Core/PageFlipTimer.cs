// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
namespace Fipinator.Core;

/// <summary>
/// Auto-flip for a FIP with several gauges: says when to show the next one, every
/// <see cref="Seconds"/> seconds. Turning the knob pauses it, so a gauge the pilot picked stays up;
/// it carries on by itself after <see cref="PauseAfterKnob"/> without knob turns.
/// Safe to use from the knob event thread and the drawing thread at once.
/// </summary>
public sealed class PageFlipTimer
{
    /// <summary>The choices offered in Set up FIPs (0 = off).</summary>
    public static readonly int[] Choices = [0, 3, 5, 10, 20, 30];

    /// <summary>Shortest and longest time between flips; anything else in a panel file is brought into range.</summary>
    public const int MinSeconds = 3, MaxSeconds = 600;

    public static readonly TimeSpan PauseAfterKnob = TimeSpan.FromSeconds(30);

    private long _nextTicks;

    /// <summary>Seconds between flips, or 0 when auto-flip is off.</summary>
    public int Seconds { get; }

    public bool On => Seconds > 0;

    public PageFlipTimer(int seconds, DateTime now)
    {
        Seconds = seconds <= 0 ? 0 : Math.Clamp(seconds, MinSeconds, MaxSeconds);
        _nextTicks = now.AddSeconds(Seconds).Ticks;
    }

    /// <summary>True when it's time to show the next gauge; the next flip is then counted from <paramref name="now"/>.</summary>
    public bool Due(DateTime now)
    {
        if (!On) return false;
        var next = Interlocked.Read(ref _nextTicks);
        if (now.Ticks < next) return false;
        Interlocked.Exchange(ref _nextTicks, now.AddSeconds(Seconds).Ticks);
        return true;
    }

    /// <summary>The pilot turned the knob: hold the gauge for <see cref="PauseAfterKnob"/>, then carry on.</summary>
    public void KnobTurned(DateTime now) => Interlocked.Exchange(ref _nextTicks, (now + PauseAfterKnob).Ticks);

    /// <summary>Plain words for a setting: "off", "every 5 seconds".</summary>
    public static string Describe(int seconds) => seconds <= 0 ? "off" : $"every {Math.Clamp(seconds, MinSeconds, MaxSeconds)} seconds";
}
