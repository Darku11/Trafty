using System.Collections.ObjectModel;
using System.Text;
using Trafty.Core.Compression;
using Trafty.Core.Hashing;

namespace Trafty.Core.Archives;

/// <summary>
/// Read access to an MPAK container as shipped with the Dark Age of Camelot client
/// (.mpk, .npk and the archives that use the same layout).
///
/// The archive is opened lazily: only the header, the archive name block and the
/// directory block are read up front. Payloads are pulled from the underlying stream on
/// demand, so browsing a multi hundred megabyte archive costs almost no memory.
/// </summary>
public sealed class MpkArchive : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly Dictionary<string, MpkEntry> _byName;
    private readonly object _readLock = new();

    private bool _disposed;

    private MpkArchive(Stream stream, bool leaveOpen, MpkHeader header, string archiveName, IReadOnlyList<MpkEntry> entries)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;

        Header = header;
        ArchiveName = archiveName;
        Entries = new ReadOnlyCollection<MpkEntry>(entries.ToList());

        _byName = new Dictionary<string, MpkEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (MpkEntry entry in Entries)
        {
            // Duplicate names are not expected but must not throw: the first occurrence
            // wins for lookups, while Entries keeps every record.
            _byName.TryAdd(entry.Name, entry);
        }
    }

    /// <summary>Parsed container header.</summary>
    public MpkHeader Header { get; }

    /// <summary>
    /// Archive name stored inside the container, for example "ter002.mpk". This is the
    /// name the client expects and it does not have to match the name on disk.
    /// </summary>
    public string ArchiveName { get; }

    /// <summary>All directory entries, in their original order.</summary>
    public ReadOnlyCollection<MpkEntry> Entries { get; }

    /// <summary>Looks an entry up by name, case insensitively.</summary>
    public MpkEntry? this[string name] =>
        _byName.TryGetValue(name, out MpkEntry? entry) ? entry : null;

    /// <summary>Opens an archive from disk for reading.</summary>
    public static MpkArchive Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.RandomAccess);

        try
        {
            return Open(stream, leaveOpen: false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens an archive from an existing seekable stream.
    /// </summary>
    /// <param name="stream">Stream positioned anywhere; it is seeked internally.</param>
    /// <param name="leaveOpen">Keep the stream open when the archive is disposed.</param>
    public static MpkArchive Open(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("The stream must be readable and seekable.", nameof(stream));
        }

        long length = stream.Length;

        stream.Position = 0;
        byte[] rawHeader;

        try
        {
            rawHeader = ReadExactly(stream, MpkHeader.Size);
        }
        catch (EndOfStreamException ex)
        {
            throw new MpkFormatException("File is too small to contain an MPAK header.", ex);
        }

        MpkHeader header = MpkHeader.Parse(rawHeader);
        header.ValidateAgainstFileLength(length);

        string archiveName = ReadArchiveName(stream, header);
        List<MpkEntry> entries = ReadDirectory(stream, header, length);

        return new MpkArchive(stream, leaveOpen, header, archiveName, entries);
    }

    private static string ReadArchiveName(Stream stream, MpkHeader header)
    {
        if (header.NameCompressedSize == 0)
        {
            return string.Empty;
        }

        stream.Position = header.NameOffset;
        byte[] compressed = ReadExactly(stream, (int)header.NameCompressedSize);

        try
        {
            return Encoding.Latin1.GetString(ZlibCodec.Decompress(compressed)).TrimEnd('\0');
        }
        catch (InvalidDataException ex)
        {
            throw new MpkFormatException("The archive name block is not a valid zlib stream.", ex);
        }
    }

    private static List<MpkEntry> ReadDirectory(Stream stream, MpkHeader header, long fileLength)
    {
        stream.Position = header.DirectoryOffset;
        byte[] compressed = ReadExactly(stream, (int)header.DirectoryCompressedSize);

        uint actualCrc = Crc32.Compute(compressed);

        if (actualCrc != header.DirectoryCrc32)
        {
            throw new MpkFormatException(
                $"Directory checksum mismatch: header declares 0x{header.DirectoryCrc32:X8}, " +
                $"the data hashes to 0x{actualCrc:X8}. The archive is damaged.");
        }

        byte[] directory;

        try
        {
            directory = ZlibCodec.Decompress(compressed, (int)header.FileCount * MpkEntry.DirectoryRecordSize);
        }
        catch (InvalidDataException ex)
        {
            throw new MpkFormatException("The directory block does not match the declared file count.", ex);
        }

        long dataLength = fileLength - header.DataOffset;
        var entries = new List<MpkEntry>((int)header.FileCount);

        for (int i = 0; i < header.FileCount; i++)
        {
            var record = directory.AsSpan(i * MpkEntry.DirectoryRecordSize, MpkEntry.DirectoryRecordSize);
            MpkEntry entry = MpkEntry.Parse(record, i);

            if ((long)entry.CompressedOffset + entry.CompressedSize > dataLength)
            {
                throw new MpkFormatException(
                    $"Entry \"{entry.Name}\" points past the end of the data region.");
            }

            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// Reads the raw, still compressed payload of an entry.
    /// </summary>
    public byte[] ReadCompressed(MpkEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_readLock)
        {
            _stream.Position = Header.DataOffset + entry.CompressedOffset;

            return ReadExactly(_stream, (int)entry.CompressedSize);
        }
    }

    /// <summary>
    /// Extracts an entry and returns its decompressed content.
    /// </summary>
    /// <param name="entry">Entry to extract.</param>
    /// <param name="verifyChecksum">
    /// Verify the stored CRC-32 before decompressing. The checksum covers the compressed
    /// bytes, not the decompressed ones.
    /// </param>
    public byte[] Extract(MpkEntry entry, bool verifyChecksum = true)
    {
        byte[] compressed = ReadCompressed(entry);

        if (verifyChecksum)
        {
            uint actual = Crc32.Compute(compressed);

            if (actual != entry.Crc32)
            {
                throw new MpkFormatException(
                    $"Checksum mismatch for \"{entry.Name}\": expected 0x{entry.Crc32:X8}, got 0x{actual:X8}.");
            }
        }

        try
        {
            return ZlibCodec.Decompress(compressed, (int)entry.UncompressedSize);
        }
        catch (InvalidDataException ex)
        {
            throw new MpkFormatException($"Payload of \"{entry.Name}\" could not be decompressed.", ex);
        }
    }

    /// <summary>
    /// Extracts an entry by name.
    /// </summary>
    public byte[] Extract(string name, bool verifyChecksum = true)
    {
        MpkEntry entry = this[name]
            ?? throw new FileNotFoundException($"The archive does not contain an entry named \"{name}\".", name);

        return Extract(entry, verifyChecksum);
    }

    /// <summary>
    /// Extracts an entry into <paramref name="destination"/>.
    /// </summary>
    public void ExtractTo(MpkEntry entry, Stream destination, bool verifyChecksum = true)
    {
        ArgumentNullException.ThrowIfNull(destination);

        byte[] data = Extract(entry, verifyChecksum);
        destination.Write(data, 0, data.Length);
    }

    /// <summary>
    /// Extracts every entry into <paramref name="targetDirectory"/>, using the entry name
    /// only. The recorded source path is ignored on purpose: it is unreliable and a
    /// crafted archive could otherwise write outside the target directory.
    /// </summary>
    /// <returns>The number of files written.</returns>
    public int ExtractAll(string targetDirectory, bool verifyChecksum = true, bool overwrite = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        Directory.CreateDirectory(targetDirectory);

        int written = 0;

        foreach (MpkEntry entry in Entries)
        {
            string safeName = Path.GetFileName(entry.Name);

            if (string.IsNullOrWhiteSpace(safeName))
            {
                continue;
            }

            string targetPath = Path.Combine(targetDirectory, safeName);

            if (!overwrite && File.Exists(targetPath))
            {
                continue;
            }

            File.WriteAllBytes(targetPath, Extract(entry, verifyChecksum));
            File.SetLastWriteTimeUtc(targetPath, entry.Timestamp.UtcDateTime);
            written++;
        }

        return written;
    }

    /// <summary>
    /// Walks every entry and reports the ones whose checksum, size or offset chain does
    /// not hold up. An empty result means the archive is intact.
    /// </summary>
    public IReadOnlyList<string> Verify()
    {
        var problems = new List<string>();

        uint expectedCompressedOffset = 0;
        uint expectedUncompressedOffset = 0;

        foreach (MpkEntry entry in Entries)
        {
            if (entry.CompressedOffset != expectedCompressedOffset)
            {
                problems.Add(
                    $"\"{entry.Name}\": compressed offset {entry.CompressedOffset} breaks the chain, " +
                    $"expected {expectedCompressedOffset}.");
            }

            if (entry.UncompressedOffset != expectedUncompressedOffset)
            {
                problems.Add(
                    $"\"{entry.Name}\": uncompressed offset {entry.UncompressedOffset} breaks the chain, " +
                    $"expected {expectedUncompressedOffset}.");
            }

            try
            {
                byte[] compressed = ReadCompressed(entry);
                uint crc = Crc32.Compute(compressed);

                if (crc != entry.Crc32)
                {
                    problems.Add($"\"{entry.Name}\": checksum 0x{crc:X8} does not match stored 0x{entry.Crc32:X8}.");
                }

                byte[] payload = ZlibCodec.Decompress(compressed, (int)entry.UncompressedSize);

                if (payload.Length != entry.UncompressedSize)
                {
                    problems.Add($"\"{entry.Name}\": expands to {payload.Length} instead of {entry.UncompressedSize} bytes.");
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or MpkFormatException or EndOfStreamException)
            {
                problems.Add($"\"{entry.Name}\": {ex.Message}");
            }

            expectedCompressedOffset = entry.CompressedOffset + entry.CompressedSize;
            expectedUncompressedOffset = entry.UncompressedOffset + entry.UncompressedSize;
        }

        long trailing = _stream.Length - (Header.DataOffset + expectedCompressedOffset);

        if (trailing != 0)
        {
            problems.Add($"{trailing} unaccounted byte(s) after the last entry.");
        }

        return problems;
    }

    private static byte[] ReadExactly(Stream stream, int count)
    {
        byte[] buffer = new byte[count];
        int total = 0;

        while (total < count)
        {
            int read = stream.Read(buffer, total, count - total);

            if (read <= 0)
            {
                throw new EndOfStreamException(
                    $"Unexpected end of file: wanted {count} bytes, got {total}.");
            }

            total += read;
        }

        return buffer;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }
}
