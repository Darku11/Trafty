using System.Buffers.Binary;
using System.Text;

namespace Trafty.Core.Archives;

/// <summary>
/// The 21 byte header that opens every MPAK container (.mpk / .npk and friends).
///
/// Layout:
///   0x00  char[4]  "MPAK"
///   0x04  byte     format version (2 in every retail archive observed so far)
///   0x05  uint32   CRC-32 of the compressed directory block
///   0x09  uint32   compressed size of the directory block
///   0x0D  uint32   compressed size of the archive name block
///   0x11  uint32   number of files
///
/// Everything from 0x05 onwards is obfuscated: each byte is XORed with a counter that
/// starts at zero at offset 0x05 and increments once per byte. The version byte at 0x04
/// is stored in the clear. The obfuscation is symmetric, so the same routine encodes and
/// decodes.
/// </summary>
public sealed class MpkHeader
{
    /// <summary>Total size of the header in bytes.</summary>
    public const int Size = 21;

    /// <summary>Offset at which the XOR obfuscation begins.</summary>
    public const int ObfuscationStart = 5;

    /// <summary>Format version written by the retail packer.</summary>
    public const byte KnownVersion = 2;

    private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes("MPAK");

    /// <summary>Format version byte.</summary>
    public byte Version { get; init; }

    /// <summary>CRC-32 over the compressed bytes of the directory block.</summary>
    public uint DirectoryCrc32 { get; init; }

    /// <summary>Compressed size of the directory block, in bytes.</summary>
    public uint DirectoryCompressedSize { get; init; }

    /// <summary>Compressed size of the archive name block, in bytes.</summary>
    public uint NameCompressedSize { get; init; }

    /// <summary>Number of directory entries in the archive.</summary>
    public uint FileCount { get; init; }

    /// <summary>Absolute offset of the compressed archive name block.</summary>
    public long NameOffset => Size;

    /// <summary>Absolute offset of the compressed directory block.</summary>
    public long DirectoryOffset => Size + NameCompressedSize;

    /// <summary>Absolute offset at which the payload region starts.</summary>
    public long DataOffset => DirectoryOffset + DirectoryCompressedSize;

    /// <summary>
    /// Reads a header from the first <see cref="Size"/> bytes of <paramref name="raw"/>.
    /// </summary>
    public static MpkHeader Parse(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < Size)
        {
            throw new MpkFormatException(
                $"File is too small to contain an MPAK header ({raw.Length} of {Size} bytes).");
        }

        if (!raw[..4].SequenceEqual(MagicBytes))
        {
            throw new MpkFormatException(
                "Missing MPAK signature. The file is not an MPK/EPK container, or it is encrypted.");
        }

        Span<byte> fields = stackalloc byte[Size - ObfuscationStart];
        raw.Slice(ObfuscationStart, fields.Length).CopyTo(fields);
        ApplyObfuscation(fields);

        return new MpkHeader
        {
            Version = raw[4],
            DirectoryCrc32 = BinaryPrimitives.ReadUInt32LittleEndian(fields),
            DirectoryCompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(fields[4..]),
            NameCompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(fields[8..]),
            FileCount = BinaryPrimitives.ReadUInt32LittleEndian(fields[12..]),
        };
    }

    /// <summary>
    /// Serialises this header into <paramref name="destination"/>, which must hold at
    /// least <see cref="Size"/> bytes.
    /// </summary>
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException($"Destination must be at least {Size} bytes.", nameof(destination));
        }

        MagicBytes.CopyTo(destination);
        destination[4] = Version;

        Span<byte> fields = destination.Slice(ObfuscationStart, Size - ObfuscationStart);
        BinaryPrimitives.WriteUInt32LittleEndian(fields, DirectoryCrc32);
        BinaryPrimitives.WriteUInt32LittleEndian(fields[4..], DirectoryCompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(fields[8..], NameCompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(fields[12..], FileCount);

        ApplyObfuscation(fields);
    }

    /// <summary>
    /// Applies the symmetric XOR mask used by the header fields. The mask is simply the
    /// index of each byte within the field block.
    /// </summary>
    private static void ApplyObfuscation(Span<byte> fields)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            fields[i] ^= (byte)i;
        }
    }

    /// <summary>
    /// Verifies that the declared block sizes actually fit inside a file of the given
    /// length. Called before any offset from the header is used for seeking.
    /// </summary>
    public void ValidateAgainstFileLength(long fileLength)
    {
        if (NameCompressedSize > fileLength || DirectoryCompressedSize > fileLength)
        {
            throw new MpkFormatException("Header declares block sizes that exceed the file length.");
        }

        if (DataOffset > fileLength)
        {
            throw new MpkFormatException(
                $"Header points past the end of the file (data offset {DataOffset}, file length {fileLength}).");
        }

        long directoryBytes = (long)FileCount * MpkEntry.DirectoryRecordSize;

        if (FileCount > int.MaxValue / MpkEntry.DirectoryRecordSize || directoryBytes < 0)
        {
            throw new MpkFormatException($"Header declares an implausible file count of {FileCount}.");
        }
    }
}
