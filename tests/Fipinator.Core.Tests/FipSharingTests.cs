// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Xunit;

namespace Fipinator.Core.Tests;

public class FipSharingTests
{
    [Theory]
    [InlineData("D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCLCSWLOCRRC;;;IU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)",
                "D:(A;;RPWPLC;;;IU)(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCLCSWLOCRRC;;;IU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)")]
    [InlineData("O:SYD:AI(A;;CCLCSWLOCRRC;;;IU)", "O:SYD:AI(A;;RPWPLC;;;IU)(A;;CCLCSWLOCRRC;;;IU)")]
    [InlineData("  D:(A;;RPWPLC;;;IU)(A;;CC;;;SY)\r\n", "D:(A;;RPWPLC;;;IU)(A;;CC;;;SY)")] // already there: unchanged
    public void Adds_start_and_stop_for_the_signed_in_user_once(string before, string after) =>
        Assert.Equal(after, FipSharing.AddStartStop(before));

    [Fact]
    public void Text_without_a_permission_list_is_refused() =>
        Assert.Throws<FormatException>(() => FipSharing.AddStartStop("O:SY"));

    [Theory]
    [InlineData("        START_TYPE         : 2   AUTO_START", "auto")]
    [InlineData("        START_TYPE         : 2   AUTO_START  (DELAYED)", "delayed-auto")]
    [InlineData("        START_TYPE         : 3   DEMAND_START", "demand")]
    [InlineData("        START_TYPE         : 4   DISABLED", "disabled")]
    [InlineData("nothing useful", null)]
    public void Reads_the_start_type(string line, string? expected) =>
        Assert.Equal(expected, FipSharing.StartType($"[SC] QueryServiceConfig SUCCESS\r\n\r\nSERVICE_NAME: SaiDOutput\r\n{line}\r\n"));
}
