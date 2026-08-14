using System.Buffers.Binary;
using System.Text;

namespace Trafty.Core.Archives;

/// <summary>
/// One record of the MPAK directory block. Each record is a fixed 284 bytes:
///
///   0x000  char[13]  file name, NUL terminated
///   0x00D  char[243] original source path on the packer's machine, NUL terminated
///   0x100  uint32    modification timestamp (Unix seconds, UTC)
///   0x104  uint32    flags, value 4 in every retail archive observed so far
///   0x108  uint32    offset of the entry inside the uncompressed payload stream
///   0x10C  uint32    uncompressed size
///   0x110  uint32    offset of the entry relative to the start of the data region
///   0x114  uint32    compressed size
///   0x118  uint32    CRC-32 of the COMPRESSED bytes
///
/// Two quirks of the retail packer matter here:
///
/// The name field is undersized. The packer writes the source path at a fixed offset of
/// 0x0D and only afterwards copies the file name over it, so a name longer than twelve
/// characters eats the leading characters of the stored path. That is why retail archives
/// contain paths such as "lot\labyrinth\..." instead of "camelot\labyrinth\...". The name
/// itself is never affected and stays the authoritative identifier.
///
/// The padding behind the path terminator is uninitialised memory rather than zeroes in
/// most records. The raw 256 byte string block is therefore kept verbatim so that an
/// untouched entry repacks to exactly the bytes it was read from.
/// </summary>
public sealed class MpkEntry
{
    /// <summary>Size of a single directory record in bytes.</summary>
    public const int DirectoryRecordSize = 284;

    /// <summary>Offset at which the packer starts the source path field.</summary>
    public const int PathFieldOffset = 13;

    /// <summary>Combined size of the name and path fields.</summary>
    public const int StringBlockSize = 256;

    /// <summary>Size of the numeric field block that follows the strings.</summary>
    public const int FieldBlockSize = DirectoryRecordSize - StringBlockSize;

    /// <summary>Value the flags field carries in every archive encountered so far.</summary>
    public const uint DefaultFlags = 4;

    // The retail packer emits Windows-1252 text. Latin1 covers the same byte range for the
    // ASCII-only names the client uses and needs no encoding provider registration.
    private static readonly Encoding TextEncoding = Encoding.Latin1;

    /// <summary>Name of the packed file, for example "patch0000-00.dds".</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Source path recorded by the packer. Informational only: it may be truncated at the
    /// front for names longer than twelve characters, and it reflects a directory layout
    /// that no longer exists.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>Modification timestamp stored with the entry.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Raw flags field, preserved so that repacking stays byte compatible.</summary>
    public uint Flags { get; init; } = DefaultFlags;

    /// <summary>Offset of this entry within the concatenated uncompressed payloads.</summary>
    public uint UncompressedOffset { get; init; }

    /// <summary>Size of the payload after decompression.</summary>
    public uint UncompressedSize { get; init; }

    /// <summary>Offset of the compressed payload, relative to the start of the data region.</summary>
    public uint CompressedOffset { get; init; }

    /// <summary>Size of the compressed payload.</summary>
    public uint CompressedSize { get; init; }

    /// <summary>CRC-32 of the compressed payload bytes.</summary>
    public uint Crc32 { get; init; }

    /// <summary>Index of this entry within the directory.</summary>
    public int Index { get; init; }

    /// <summary>
    /// The untouched 256 byte name and path block, including the packer's uninitialised
    /// padding. Empty for entries that were created in memory rather than read from a file.
    /// </summary>
    public ReadOnlyMemory<byte> RawStringBlock { get; init; }

    /// <summary>File name extension in lower case, without the leading dot.</summary>
    public string Extension => Path.GetExtension(Name).TrimStart('.').ToLowerInvariant();

    /// <summary>
    /// Parses a single directory record.
    /// </summary>
    public static MpkEntry Parse(ReadOnlySpan<byte> record, int index)
    {
        if (record.Length < DirectoryRecordSize)
        {
            throw new MpkFormatException(
                $"Directory record {index} is truncated ({record.Length} of {DirectoryRecordSize} bytes).");
        }

        ReadOnlySpan<byte> strings = record[..StringBlockSize];
        string name = ReadNulTerminated(strings);

        if (name.Length == 0)
        {
            throw new MpkFormatException($"Directory record {index} has an empty file name.");
        }

        // The path begins at the field offset for short names, and directly behind the
        // name terminator once the name has overrun that offset.
        int pathStart = PathStartFor(name);
        string sourcePath = pathStart < StringBlockSize
            ? ReadNulTerminated(strings[pathStart..])
            : string.Empty;

        ReadOnlySpan<byte> fields = record.Slice(StringBlockSize, FieldBlockSize);

        return new MpkEntry
        {
            Index = index,
            Name = name,
            SourcePath = sourcePath,
            RawStringBlock = strings.ToArray(),
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadUInt32LittleEndian(fields)),
            Flags = BinaryPrimitives.ReadUInt32LittleEndian(fields[4..]),
            UncompressedOffset = BinaryPrimitives.ReadUInt32LittleEndian(fields[8..]),
            UncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(fields[12..]),
            CompressedOffset = BinaryPrimitives.ReadUInt32LittleEndian(fields[16..]),
            CompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(fields[20..]),
            Crc32 = BinaryPrimitives.ReadUInt32LittleEndian(fields[24..]),
        };
    }

    /// <summary>
    /// Serialises this entry into a directory record. Entries that were read from a file
    /// reuse their original string block, which keeps an untouched archive byte identical
    /// after a repack.
    /// </summary>
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < DirectoryRecordSize)
        {
            throw new ArgumentException(
                $"Destination must be at least {DirectoryRecordSize} bytes.", nameof(destination));
        }

        Span<byte> strings = destination[..StringBlockSize];

        if (RawStringBlock.Length == StringBlockSize)
        {
            RawStringBlock.Span.CopyTo(strings);
        }
        else
        {
            BuildStringBlock(strings, Name, SourcePath);
        }

        Span<byte> fields = destination.Slice(StringBlockSize, FieldBlockSize);
        BinaryPrimitives.WriteUInt32LittleEndian(fields, (uint)Timestamp.ToUnixTimeSeconds());
        BinaryPrimitives.WriteUInt32LittleEndian(fields[4..], Flags);
        BinaryPrimitives.WriteUInt32LittleEndian(fields[8..], UncompressedOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(fields[12..], UncompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(fields[16..], CompressedOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(fields[20..], CompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(fields[24..], Crc32);
    }

    /// <summary>
    /// Writes a fresh name and path block, laid out the way the retail packer lays it out.
    /// The block is zero filled first, so a rebuilt entry loses the original padding noise.
    /// </summary>
    public static void BuildStringBlock(Span<byte> block, string name, string sourcePath)
    {
        if (block.Length < StringBlockSize)
        {
            throw new ArgumentException($"Block must be at least {StringBlockSize} bytes.", nameof(block));
        }

        block[..StringBlockSize].Clear();

        int pathStart = PathStartFor(name);

        WriteNulTerminated(block[..StringBlockSize], name);
        WriteNulTerminated(block[pathStart..StringBlockSize], sourcePath);
    }

    private static int PathStartFor(string name) => Math.Max(PathFieldOffset, name.Length + 1);

    private static string ReadNulTerminated(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        ReadOnlySpan<byte> text = end < 0 ? field : field[..end];

        return TextEncoding.GetString(text);
    }

    private static void WriteNulTerminated(Span<byte> field, string value)
    {
        byte[] bytes = TextEncoding.GetBytes(value);

        if (bytes.Length + 1 > field.Length)
        {
            throw new ArgumentException(
                $"Value \"{value}\" does not fit into a {field.Length} byte field.", nameof(value));
        }

        bytes.CopyTo(field);
        field[bytes.Length] = 0;
    }

    public override string ToString() =>
        $"{Name} ({UncompressedSize} bytes, {CompressedSize} packed)";
}
