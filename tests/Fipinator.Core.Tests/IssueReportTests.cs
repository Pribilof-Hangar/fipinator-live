// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class IssueReportTests
{
    [Fact]
    public void Hides_fip_serials_and_the_profile_folder()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var text = IssueReport.Hide($"FIP SN12345ABC: slow send; file {profile}\\Documents\\panel.json");
        Assert.DoesNotContain("SN12345ABC", text);
        Assert.Contains("...5ABC", text);
        Assert.DoesNotContain(profile, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GitHub_page_is_prefilled_short_and_has_no_serials()
    {
        var url = IssueReport.GitHubIssueUrl("FIPinator Live", ["FIPs connected: 7", "  FIP SN0A1B2C3D: DC-6 Airspeed - Showing"]);
        Assert.StartsWith(IssueReport.ProjectPage + "/issues/new?title=", url);
        Assert.True(url.Length <= 7000, $"link is {url.Length} characters");
        var body = Uri.UnescapeDataString(url);
        Assert.Contains("What happened?", body);
        Assert.DoesNotContain("SN0A1B2C3D", body);
    }
}
