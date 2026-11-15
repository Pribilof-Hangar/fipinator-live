// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Xunit;
using System.Buffers.Binary;
using Fipinator.Sim;

namespace Fipinator.Core.Tests;

public class PmdgNg3Tests
{
    private static byte[] Block()
    {
        var block = new byte[PmdgNg3.BlockSize];
        BinaryPrimitives.WriteSingleLittleEndian(block.AsSpan(120), 8630f);   // FUEL_QtyLeft
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(416), 111);     // MCP_Course 1
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(418), 222);     // MCP_Course 2
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(428), 292);     // MCP_Heading
        BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(432), -1500);    // MCP_VertSpeed
        block[487] = 5;                                                        // MAIN_AutobrakeSelector: MAX
        block[491] = 1;                                                        // MAIN_annunLE_FLAPS_EXT
        BinaryPrimitives.WriteSingleLittleEndian(block.AsSpan(492), 5f);      // MAIN_TEFlapsNeedle left
        BinaryPrimitives.WriteSingleLittleEndian(block.AsSpan(496), 4.5f);    // MAIN_TEFlapsNeedle right
        return block;
    }

    [Theory]
    [InlineData("PMDG:FUEL_QtyLeft", 8630)]
    [InlineData("PMDG:MCP_Course", 111)]
    [InlineData("PMDG:MCP_Course:2", 222)]
    [InlineData("PMDG:MCP_Heading", 292)]
    [InlineData("PMDG:MCP_VertSpeed", -1500)]
    [InlineData("PMDG:MAIN_AutobrakeSelector", 5)]
    [InlineData("PMDG:MAIN_annunLE_FLAPS_EXT", 1)]
    [InlineData("PMDG:MAIN_annunLE_FLAPS_TRANSIT", 0)]
    [InlineData("PMDG:MAIN_TEFlapsNeedle:1", 5)]
    [InlineData("PMDG:MAIN_TEFlapsNeedle:2", 4.5)]
    public void Reads_fields_at_their_places(string name, double expected) =>
        Assert.Equal(expected, PmdgNg3.Read(Block(), name));

    [Theory]
    [InlineData("PMDG:NO_SUCH_FIELD")]
    [InlineData("PMDG:MCP_Course:3")]
    [InlineData("PMDG:MCP_Heading:x")]
    [InlineData("TURB ENG N1:1")]
    public void Unknown_names_read_nothing(string name)
    {
        Assert.Null(PmdgNg3.Read(Block(), name));
        Assert.False(PmdgNg3.IsKnown(name));
    }

    [Fact]
    public void A_short_block_reads_nothing() => Assert.Null(PmdgNg3.Read(new byte[100], "PMDG:MAIN_AutobrakeSelector"));
}
