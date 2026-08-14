using System.Globalization;

namespace Trafty.Core.Zones;

/// <summary>
/// One placed object in a zone: a fixtures.csv row. World coordinates (X, Y, Z) are in the
/// same unit the client uses for zone-local positions (matches the coordinate range seen in
/// zonejump.csv exit points for the same zone).
/// </summary>
public sealed class FixtureCsvEntry
{
    public required int Id { get; init; }
    public required int NifId { get; init; }
    public required string TextualName { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Z { get; init; }
    public required double Heading { get; init; }
    public required int Scale { get; init; }
}

/// <summary>
/// Reads fixtures.csv, found inside a zone's csv/dat archive (e.g. csv003.mpk, dat003.mpk).
/// Plain comma-separated text. Each row places one instance of a model (referenced by NIF id,
/// resolved via nifs.csv) at a world position within the zone — this is the data a zone map
/// view needs to plot objects.
///
/// Layout: two header rows, then one row per fixture instance:
///   ID,NIF #,Textual Name,X,Y,Z,A,Scale,Collide,Radius,Animate,Ground,Flip,Cave,Unique ID,
///   3D Angle,3D Axis X,3D Axis Y,3D Axis Z
///
/// Only position, heading, scale, and the name/id fields are kept — collision radius,
/// animation flags, and the 3D-axis rotation fields configure in-game behavior this asset
/// tool does not need.
/// </summary>
public sealed class FixtureCsvFile
{
    private const int HeaderRowCount = 2;

    public required IReadOnlyList<FixtureCsvEntry> Entries { get; init; }

    public static FixtureCsvFile Parse(string text)
    {
        string[] lines = text.Split('\n');

        if (lines.Length <= HeaderRowCount)
        {
            throw new ZoneCsvFormatException("fixtures.csv has no data rows past the header.");
        }

        List<FixtureCsvEntry> entries = new();

        for (int i = HeaderRowCount; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r', '\n');

            if (line.Length == 0)
            {
                continue;
            }

            string[] fields = line.Split(',');

            if (fields.Length < 8)
            {
                throw new ZoneCsvFormatException(
                    $"fixtures.csv line {i + 1} has {fields.Length} field(s), expected at least 8 (id..scale).");
            }

            entries.Add(new FixtureCsvEntry
            {
                Id = ParseInt(fields[0], i, "id"),
                NifId = ParseInt(fields[1], i, "NIF #"),
                TextualName = fields[2],
                X = ParseDouble(fields[3], i, "X"),
                Y = ParseDouble(fields[4], i, "Y"),
                Z = ParseDouble(fields[5], i, "Z"),
                Heading = ParseDouble(fields[6], i, "A"),
                Scale = ParseInt(fields[7], i, "Scale"),
            });
        }

        return new FixtureCsvFile { Entries = entries };
    }

    public static FixtureCsvFile Load(string path) => Parse(File.ReadAllText(path));

    private static int ParseInt(string field, int lineIndex, string fieldName)
    {
        if (!int.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            throw new ZoneCsvFormatException($"fixtures.csv line {lineIndex + 1}: \"{field}\" is not a valid {fieldName}.");
        }

        return value;
    }

    private static double ParseDouble(string field, int lineIndex, string fieldName)
    {
        if (!double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw new ZoneCsvFormatException($"fixtures.csv line {lineIndex + 1}: \"{field}\" is not a valid {fieldName}.");
        }

        return value;
    }
}
