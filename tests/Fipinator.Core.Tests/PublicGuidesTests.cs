// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Text.RegularExpressions;
using Xunit;

namespace Fipinator.Core.Tests;

/// <summary>
/// The guides people download (installed with FIPinator 3000, on GitHub) stay general: no personal details, no
/// notes about one person's setup, nothing that only works with one AI's private folders.
/// </summary>
public class PublicGuidesTests
{
    private static string Guides => Path.Combine(AppContext.BaseDirectory, "repo", "docs", "guides");

    [Theory]
    [InlineData("gauge-making-guide.md")]
    [InlineData("making-gauges-with-ai.md")]
    [InlineData("spad-and-live.md")]
    public void Public_guide_has_nothing_personal_or_private(string file)
    {
        var text = File.ReadAllText(Path.Combine(Guides, file));
        string[] banned = ["owner", "scratchpad", "CLAUDE.md", ".claude", "OneDrive", @"C:\Users", @"D:\", "spad-gauges-git", "private"];
        foreach (var word in banned)
            Assert.False(text.Contains(word, StringComparison.OrdinalIgnoreCase), $"{file} mentions \"{word}\"");
        Assert.DoesNotMatch(new Regex(@"\bMZ[0-9A-F]{8}\b"), text); // a FIP serial number
        Assert.Contains("FREE and open source", text);
    }
}
