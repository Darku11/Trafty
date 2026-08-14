using System.Globalization;

namespace Trafty.Core.Zones;

/// <summary>
/// Reads nifs.csv, found inside a zone's csv/dat archive (e.g. csv003.mpk, dat003.mpk) next
/// to fixtures.csv. Plain comma-separated text, not a binary format — no reverse engineering
/// needed, just header rows to skip. Maps a per-zone NIF id (referenced by fixtures.csv) to
/// the actual model filename, e.g. "409,Elm,elm1.nif,...".
///
/// Layout: two header rows, then one row per model:
///   NIF,Textual Name,Filename,Only,Shadow,Color,Animate,Collide,Ground,MinAngle,MaxAngle,
///   MinScale,MaxScale,Radius,LOD 1,LOD 2,LOD 3,LOD 4,Ref Height,Ref Width,Unique,Local,Terrain
///
/// Only the id, name, and filename are kept — the rest configures in-game rendering behavior
/// (LOD distances, collision, animation) that this asset tool has no use for.
/// </summary>
public sealed class NifCsvEntry
{
    public required int NifId { get; init; }
    public required string TextualName { get; init; }
    public required string FileName { get; init; }
}

public sealed class NifCsvFile
{
    private const int HeaderRowCount = 2;

    public required IReadOnlyList<NifCsvEntry> Entries { get; init; }

    public static NifCsvFile Parse(string text)
    {
        string[] lines = text.Split('\n');

        if (lines.Length <= HeaderRowCount)
        {
            throw new ZoneCsvFormatException("nifs.csv has no data rows past the header.");
        }

        List<NifCsvEntry> entries = new();

        for (int i = HeaderRowCount; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r', '\n');

            if (line.Length == 0)
            {
                continue;
            }

            string[] fields = line.Split(',');

            if (fields.Length < 3)
            {
                throw new ZoneCsvFormatException(
                    $"nifs.csv line {i + 1} has {fields.Length} field(s), expected at least 3 (id, name, filename).");
            }

            if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nifId))
            {
                throw new ZoneCsvFormatException($"nifs.csv line {i + 1}: \"{fields[0]}\" is not a valid NIF id.");
            }

            entries.Add(new NifCsvEntry
            {
                NifId = nifId,
                TextualName = fields[1],
                FileName = fields[2],
            });
        }

        return new NifCsvFile { Entries = entries };
    }

    public static NifCsvFile Load(string path) => Parse(File.ReadAllText(path));
}
