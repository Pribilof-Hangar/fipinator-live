// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Buffers.Binary;

namespace Fipinator.Sim;

/// <summary>
/// The PMDG 737 (MSFS 2024, "NG3") cockpit data block: PMDG sends it through SimConnect as client data named
/// "PMDG_NG3_Data" when the PMDG's options file (737_Options.ini) has <c>[SDK] EnableDataBroadcast=1</c>. Gauges read
/// a field with a value name like <c>PMDG:MAIN_AutobrakeSelector</c> or, for a pair, <c>PMDG:MAIN_TEFlapsNeedle:2</c>
/// (1 = left / captain, 2 = right / first officer).
/// The field names, types and positions are facts from the SDK header PMDG ships with the aircraft
/// (Documentation\SDK\PMDG_NG3_SDK.h; positions worked out from its field order with normal alignment, block 916
/// bytes). Only the fields FIPinator uses are listed; more can be added from the header the same way.
/// </summary>
public static class PmdgNg3
{
    /// <summary>The value-name prefix gauges use for PMDG fields.</summary>
    public const string Prefix = "PMDG:";

    /// <summary>SimConnect client data name, as published in PMDG's SDK.</summary>
    public const string ClientDataName = "PMDG_NG3_Data";

    /// <summary>Size of the whole block in bytes.</summary>
    public const int BlockSize = 916;

    private enum Kind { Bool, Byte, Int16, UInt16, Float }

    private sealed record Field(Kind Kind, int Offset, int Count = 1);

    private static readonly Dictionary<string, Field> Fields = new(StringComparer.Ordinal)
    {
        ["FUEL_QtyCenter"] = new(Kind.Float, 116),                // pounds
        ["FUEL_QtyLeft"] = new(Kind.Float, 120),                  // pounds
        ["FUEL_QtyRight"] = new(Kind.Float, 124),                 // pounds
        ["MCP_Course"] = new(Kind.UInt16, 416, 2),                // degrees: 1 captain, 2 first officer
        ["MCP_IASMach"] = new(Kind.Float, 420),                   // knots, or Mach when below 10
        ["MCP_IASBlank"] = new(Kind.Bool, 424),
        ["MCP_Heading"] = new(Kind.UInt16, 428),                  // degrees
        ["MCP_Altitude"] = new(Kind.UInt16, 430),                 // feet
        ["MCP_VertSpeed"] = new(Kind.Int16, 432),                 // feet per minute
        ["MCP_VertSpeedBlank"] = new(Kind.Bool, 434),
        ["MAIN_annunSPEEDBRAKE_ARMED"] = new(Kind.Bool, 477),
        ["MAIN_N1SetSelector"] = new(Kind.Byte, 484),             // 0: 2, 1: 1, 2: AUTO, 3: BOTH
        ["MAIN_FuelFlowSelector"] = new(Kind.Byte, 486),          // 0: RESET, 1: RATE, 2: USED
        ["MAIN_AutobrakeSelector"] = new(Kind.Byte, 487),         // 0: RTO, 1: OFF, 2: 1, 3: 2, 4: 3, 5: MAX
        ["MAIN_annunANTI_SKID_INOP"] = new(Kind.Bool, 488),
        ["MAIN_annunAUTO_BRAKE_DISARM"] = new(Kind.Bool, 489),
        ["MAIN_annunLE_FLAPS_TRANSIT"] = new(Kind.Bool, 490),
        ["MAIN_annunLE_FLAPS_EXT"] = new(Kind.Bool, 491),
        ["MAIN_TEFlapsNeedle"] = new(Kind.Float, 492, 2),         // flap position as on the dial (0-40): 1 left, 2 right
    };

    /// <summary>True for a value name that reads the PMDG block.</summary>
    public static bool IsPmdg(string name) => name.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>True when <paramref name="name"/> (with the PMDG: prefix) is a field FIPinator knows.</summary>
    public static bool IsKnown(string name) => Parse(name) is not null;

    private static (Field Field, int Index)? Parse(string name)
    {
        if (!IsPmdg(name)) return null;
        var rest = name[Prefix.Length..];
        var index = 1;
        var colon = rest.IndexOf(':');
        if (colon >= 0)
        {
            if (!int.TryParse(rest[(colon + 1)..], out index)) return null;
            rest = rest[..colon];
        }
        if (!Fields.TryGetValue(rest, out var field) || index < 1 || index > field.Count) return null;
        return (field, index);
    }

    /// <summary>Reads a field from a received block; null if the name is unknown or the block too short.</summary>
    public static double? Read(ReadOnlySpan<byte> block, string name)
    {
        if (Parse(name) is not var (field, index)) return null;
        var size = field.Kind switch { Kind.Bool or Kind.Byte => 1, Kind.Int16 or Kind.UInt16 => 2, _ => 4 };
        var at = field.Offset + (index - 1) * size;
        if (block.Length < at + size) return null;
        var bytes = block.Slice(at, size);
        return field.Kind switch
        {
            Kind.Bool => bytes[0] != 0 ? 1 : 0,
            Kind.Byte => bytes[0],
            Kind.Int16 => BinaryPrimitives.ReadInt16LittleEndian(bytes),
            Kind.UInt16 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
            _ => BinaryPrimitives.ReadSingleLittleEndian(bytes),
        };
    }
}
