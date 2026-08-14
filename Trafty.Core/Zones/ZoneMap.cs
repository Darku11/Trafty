using Trafty.Core.Archives;

namespace Trafty.Core.Zones;

/// <summary>
/// Aggregates the three CSV files that together describe a zone's placed objects and
/// outline: fixtures.csv (object instances), nifs.csv (id -> model filename), and
/// bound.csv (outer boundary polygon). All three live side by side inside a zone's csv/dat
/// archive (e.g. csv003.mpk or dat003.mpk for zone003) — dat003.mpk additionally carries
/// terrain PCX images, but the three CSVs are byte-identical between the two, so either
/// archive works as the source.
/// </summary>
public sealed class ZoneMap
{
    public required FixtureCsvFile Fixtures { get; init; }
    public required NifCsvFile Nifs { get; init; }
    public required ZoneBoundaryFile Boundary { get; init; }

    /// <summary>Looks up the model filename for a fixture's NIF id, or null if unresolved.</summary>
    public string? ResolveNifFileName(int nifId) =>
        Nifs.Entries.FirstOrDefault(e => e.NifId == nifId)?.FileName;

    public static ZoneMap LoadFromArchive(MpkArchive archive)
    {
        FixtureCsvFile fixtures = LoadEntry(archive, "fixtures.csv", FixtureCsvFile.Parse);
        NifCsvFile nifs = LoadEntry(archive, "nifs.csv", NifCsvFile.Parse);
        ZoneBoundaryFile boundary = LoadEntry(archive, "bound.csv", ZoneBoundaryFile.Parse);

        return new ZoneMap { Fixtures = fixtures, Nifs = nifs, Boundary = boundary };
    }

    public static ZoneMap Load(string archivePath)
    {
        using MpkArchive archive = MpkArchive.Open(archivePath);
        return LoadFromArchive(archive);
    }

    private static T LoadEntry<T>(MpkArchive archive, string entryName, Func<string, T> parse)
    {
        MpkEntry? entry = archive[entryName];

        if (entry is null)
        {
            throw new ZoneCsvFormatException($"Archive {archive.ArchiveName} has no \"{entryName}\" entry.");
        }

        byte[] bytes = archive.Extract(entry);
        string text = System.Text.Encoding.Latin1.GetString(bytes);

        return parse(text);
    }
}
