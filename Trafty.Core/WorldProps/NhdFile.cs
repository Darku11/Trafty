using System.Buffers.Binary;
using System.Text;

namespace Trafty.Core.WorldProps;

/// <summary>
/// Reads .nhd files. Reverse-engineered from five real samples spanning tiny to large
/// objects (3rdruinedpiece.nhd, aecentbarricade.nhd, aegcliffpiece2.nhd, and both
/// 1struinedtemple variants) — the grid-size formula below matched the remaining byte
/// count exactly on all five, with zero slack.
///
/// Layout:
///   0x00  char[3]   "NHD"
///   0x03  byte      version (1 in every sample)
///   0x04  byte      unknown, 0 in every sample
///   0x05  uint16    length of the model file name that follows
///   0x07  char[N]   model file name, e.g. "1struinedtemple.nif" — the matching .npk
///                    archive holds exactly this file
///   ...   uint16    unknown, 0x0040 (64) in every sample
///   ...   int32 x4  bounding box: minX, maxX, minY, maxY (grid-cell units)
///   ...   int16[]   a (maxX-minX) by (maxY-minY) grid, row-major
///
/// Grid value hypothesis (evidence-based, not confirmed): across the five samples, the
/// dominant value is consistently -2500 (a "no geometry here" sentinel covering 42-64% of
/// cells depending on how irregular the object's silhouette is against its rectangular
/// bounding box), and the non-sentinel maximum scales with the object's physical size —
/// 85 for a small ruin fragment, 388 for a mid-size barricade, 6862 for a large cliff
/// piece. That pattern fits a per-cell heightfield (geometry height above a baseline, in
/// an unconfirmed sub-unit) better than a flat collision mask, but this parser exposes
/// the raw values rather than asserting that interpretation as fact.
/// </summary>
public sealed class NhdFile
{
    private const int MinimumHeaderSize = 7;
    private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes("NHD");

    public byte Version { get; init; }
    public byte Unknown4 { get; init; }

    /// <summary>Name of the referenced model, e.g. "1struinedtemple.nif".</summary>
    public required string ModelName { get; init; }

    public int MinX { get; init; }
    public int MaxX { get; init; }
    public int MinY { get; init; }
    public int MaxY { get; init; }

    /// <summary>Grid width: <see cref="MaxX"/> - <see cref="MinX"/>.</summary>
    public int GridWidth => MaxX - MinX;

    /// <summary>Grid height: <see cref="MaxY"/> - <see cref="MinY"/>.</summary>
    public int GridHeight => MaxY - MinY;

    /// <summary>
    /// Raw per-cell values, row-major, <see cref="GridWidth"/> * <see cref="GridHeight"/>
    /// entries. Meaning not yet confirmed — see the type-level remarks.
    /// </summary>
    public required short[] Grid { get; init; }

    public short GridValueAt(int x, int y)
    {
        if ((uint)x >= (uint)GridWidth || (uint)y >= (uint)GridHeight)
        {
            throw new ArgumentOutOfRangeException(
                x >= GridWidth ? nameof(x) : nameof(y), "Coordinate is outside the grid.");
        }

        return Grid[y * GridWidth + x];
    }

    public static NhdFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < MinimumHeaderSize)
        {
            throw new WorldPropFormatException(
                $"File is too small to contain an NHD header ({data.Length} bytes).");
        }

        if (!data[..3].SequenceEqual(MagicBytes))
        {
            throw new WorldPropFormatException("Missing NHD signature.");
        }

        byte version = data[3];
        byte unknown4 = data[4];
        ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(data[5..]);

        int nameOffset = 7;

        if (data.Length < nameOffset + nameLength + 2 + 16)
        {
            throw new WorldPropFormatException("File is truncated before the bounding box fields.");
        }

        string modelName = Encoding.Latin1.GetString(data.Slice(nameOffset, nameLength));

        int fieldsOffset = nameOffset + nameLength + 2; // skip name, then the unknown uint16
        int minX = BinaryPrimitives.ReadInt32LittleEndian(data[fieldsOffset..]);
        int maxX = BinaryPrimitives.ReadInt32LittleEndian(data[(fieldsOffset + 4)..]);
        int minY = BinaryPrimitives.ReadInt32LittleEndian(data[(fieldsOffset + 8)..]);
        int maxY = BinaryPrimitives.ReadInt32LittleEndian(data[(fieldsOffset + 12)..]);

        int gridWidth = maxX - minX;
        int gridHeight = maxY - minY;

        if (gridWidth < 0 || gridHeight < 0)
        {
            throw new WorldPropFormatException(
                $"Bounding box is inverted (minX={minX}, maxX={maxX}, minY={minY}, maxY={maxY}).");
        }

        int gridOffset = fieldsOffset + 16;
        int expectedGridBytes = gridWidth * gridHeight * 2;

        if (data.Length - gridOffset != expectedGridBytes)
        {
            throw new WorldPropFormatException(
                $"Grid size mismatch: bounding box implies {expectedGridBytes} bytes " +
                $"({gridWidth}x{gridHeight} int16 cells), but {data.Length - gridOffset} remain in the file.");
        }

        short[] grid = new short[gridWidth * gridHeight];

        for (int i = 0; i < grid.Length; i++)
        {
            grid[i] = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(gridOffset + i * 2, 2));
        }

        return new NhdFile
        {
            Version = version,
            Unknown4 = unknown4,
            ModelName = modelName,
            MinX = minX,
            MaxX = maxX,
            MinY = minY,
            MaxY = maxY,
            Grid = grid,
        };
    }

    public static NhdFile Load(string path) => Parse(File.ReadAllBytes(path));
}
