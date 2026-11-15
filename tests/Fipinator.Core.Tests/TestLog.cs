// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Runtime.CompilerServices;
using Fipinator.Core;

namespace Fipinator.Core.Tests;

/// <summary>
/// Runs before any test: the tests' log lines (made-up panels like "AAAA1111", temp folders) go to a
/// throwaway folder in %TEMP%, never into the user's real FIPinator log.
/// </summary>
internal static class TestLog
{
    [ModuleInitializer]
    internal static void Redirect() =>
        Log.UseFolder(Path.Combine(Path.GetTempPath(), "fipinator-test-logs"));
}
