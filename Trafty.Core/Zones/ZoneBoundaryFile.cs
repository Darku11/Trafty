using System.Globalization;

namespace Trafty.Core.Zones;

public readonly record struct ZoneBoundaryPoint(int X, int Y);

/// <summary>
/// Reads bound.csv, found inside a zone's csv/dat archive next to fixtures.csv. Not
/// row-per-record like fixtures.csv/nifs.csv — it's one flat, comma-separated list of
/// integers (line-wrapped for readability, wrapping is not meaningful) that pairs up into
/// (x, y) points tracing the zone's outer boundary polygon.
/// </summary>
public sealed class ZoneBoundaryFile
{
    public required IReadOnlyList<ZoneBoundaryPoint> Points { get; init; }

    public static ZoneBoundaryFile Parse(string text)
    {
        string[] tokens = text.Split(
            [',', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length % 2 != 0)
        {
            throw new ZoneCsvFormatException(
                $"bound.csv has {tokens.Length} value(s), which is odd — expected pairs of (x, y).");
        }

        var points = new List<ZoneBoundaryPoint>(tokens.Length / 2);

        for (int i = 0; i < tokens.Length; i += 2)
        {
            if (!int.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(tokens[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
            {
                throw new ZoneCsvFormatException($"bound.csv: \"{tokens[i]}, {tokens[i + 1]}\" is not a valid (x, y) pair.");
            }

            points.Add(new ZoneBoundaryPoint(x, y));
        }

        return new ZoneBoundaryFile { Points = points };
    }

    public static ZoneBoundaryFile Load(string path) => Parse(File.ReadAllText(path));
}
