// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
namespace Fipinator.Fip;

/// <summary>
/// A FIP problem with a plain-language message for the user (what's wrong and what to do).
/// <see cref="Details"/> holds the technical part for logs.
/// </summary>
public sealed class FipException(string message, string? details = null) : Exception(message)
{
    public string? Details { get; } = details;
}
