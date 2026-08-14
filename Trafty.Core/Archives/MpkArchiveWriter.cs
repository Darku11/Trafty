using Trafty.Core.Compression;
using Trafty.Core.Hashing;

namespace Trafty.Core.Archives;

/// <summary>
/// A single file to place into an archive being written.
/// </summary>
public sealed class MpkPendingEntry
{
    public required string Name { get; init; }
    public required byte[] UncompressedData { get; init; }

    /// <summary>Source path to record. Purely informational; defaults to the name.</summary>
    public string SourcePath { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Builds MPAK archives. Two ways in: start from an open <see cref="MpkArchive"/> and
/// replace or add a handful of entries while carrying the rest over untouched, or build
/// one from a flat list of files.
/// </summary>
public static class MpkArchiveWriter
{
    /// <summary>
    /// Writes a new archive to <paramref name="outputPath"/> that is identical to
    /// <paramref name="source"/> except for the entries listed in
    /// <paramref name="replacements"/>. Entries whose name matches a replacement are
    /// overwritten in place (same position in the directory); names that do not already
    /// exist are appended.
    /// </summary>
    public static void WriteReplacing(MpkArchive source, IEnumerable<MpkPendingEntry> replacements, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(replacements);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var replacementsByName = replacements.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        var entries = new List<MpkPendingEntry>();

        foreach (MpkEntry existing in source.Entries)
        {
            if (replacementsByName.Remove(existing.Name, out MpkPendingEntry? replacement))
            {
                entries.Add(replacement);
            }
            else
            {
                entries.Add(new MpkPendingEntry
                {
                    Name = existing.Name,
                    SourcePath = existing.SourcePath,
                    Timestamp = existing.Timestamp,
                    UncompressedData = source.Extract(existing, verifyChecksum: false),
                });
            }
        }

        // Anything left in the dictionary is a genuinely new file.
        entries.AddRange(replacementsByName.Values);

        Write(entries, Path.GetFileName(outputPath), outputPath);
    }

    /// <summary>
    /// Writes a brand new archive from scratch.
    /// </summary>
    public static void Write(IReadOnlyList<MpkPendingEntry> entries, string archiveName, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveName);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        byte[] nameCompressed = ZlibCodec.Compress(System.Text.Encoding.Latin1.GetBytes(archiveName));

        byte[] dataRegion;
        byte[] directoryRaw = BuildDirectoryAndData(entries, out dataRegion);
        byte[] directoryCompressed = ZlibCodec.Compress(directoryRaw);
        uint directoryCrc = Crc32.Compute(directoryCompressed);

        var header = new MpkHeader
        {
            Version = MpkHeader.KnownVersion,
            DirectoryCrc32 = directoryCrc,
            DirectoryCompressedSize = (uint)directoryCompressed.Length,
            NameCompressedSize = (uint)nameCompressed.Length,
            FileCount = (uint)entries.Count,
        };

        Span<byte> headerBytes = stackalloc byte[MpkHeader.Size];
        header.WriteTo(headerBytes);

        string? directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        output.Write(headerBytes);
        output.Write(nameCompressed);
        output.Write(directoryCompressed);
        output.Write(dataRegion);
    }

    private static byte[] BuildDirectoryAndData(IReadOnlyList<MpkPendingEntry> entries, out byte[] dataRegion)
    {
        byte[] directory = new byte[entries.Count * MpkEntry.DirectoryRecordSize];

        using var data = new MemoryStream();
        uint uncompressedCursor = 0;

        for (int i = 0; i < entries.Count; i++)
        {
            MpkPendingEntry pending = entries[i];

            byte[] compressed = ZlibCodec.Compress(pending.UncompressedData);
            uint crc = Crc32.Compute(compressed);
            uint compressedOffset = (uint)data.Position;

            data.Write(compressed);

            var entry = new MpkEntry
            {
                Index = i,
                Name = pending.Name,
                SourcePath = string.IsNullOrEmpty(pending.SourcePath) ? pending.Name : pending.SourcePath,
                Timestamp = pending.Timestamp,
                Flags = MpkEntry.DefaultFlags,
                UncompressedOffset = uncompressedCursor,
                UncompressedSize = (uint)pending.UncompressedData.Length,
                CompressedOffset = compressedOffset,
                CompressedSize = (uint)compressed.Length,
                Crc32 = crc,
            };

            entry.WriteTo(directory.AsSpan(i * MpkEntry.DirectoryRecordSize, MpkEntry.DirectoryRecordSize));

            uncompressedCursor += (uint)pending.UncompressedData.Length;
        }

        dataRegion = data.ToArray();

        return directory;
    }
}
