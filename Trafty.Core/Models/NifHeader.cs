using System.Buffers.Binary;
using System.Text;

namespace Trafty.Core.Models;

/// <summary>
/// Reads the header of a .nif file — the NetImmerse/Gamebryo model format used for every
/// mesh the DAoC client loads (weapons, armor, buildings, terrain props). Unlike MPAK or
/// NHD, this is a publicly documented, open format (NifTools project), so this parser
/// follows the published specification rather than reverse-engineered guesswork. It was
/// cross-checked against a real extracted model (1struinedtemple.NIF, pulled from
/// 1struinedtemple.npk): header string, version, and block count all matched exactly.
///
/// Layout:
///   Header line   ASCII text ending in '\n', e.g.
///                 "NetImmerse File Format, Version 4.2.2.0"
///   uint32        Packed version: byte layout is [build, patch, minor, major], so
///                 4.2.2.0 is stored as bytes 00 02 02 04.
///   uint32        Number of blocks in the file.
///
/// What follows the header is a sequence of blocks, each preceded by a length-prefixed
/// type name (e.g. "NiNode", "NiTriShape") for this NIF version. Fully walking that
/// sequence requires per-block-type layouts (dozens of them) and is not attempted here —
/// this parser deliberately stops at the header, which is enough to identify a file's
/// format version and rough complexity without risking incorrect guesses about block
/// contents.
/// </summary>
public sealed class NifHeader
{
    /// <summary>Full header line, e.g. "NetImmerse File Format, Version 4.2.2.0".</summary>
    public required string Signature { get; init; }

    public byte VersionMajor { get; init; }
    public byte VersionMinor { get; init; }
    public byte VersionPatch { get; init; }
    public byte VersionBuild { get; init; }

    public string VersionDisplay => $"{VersionMajor}.{VersionMinor}.{VersionPatch}.{VersionBuild}";

    /// <summary>Number of blocks (nodes, shapes, properties, etc.) in the file.</summary>
    public uint BlockCount { get; init; }

    /// <summary>Byte offset immediately after the header, where the first block begins.</summary>
    public int BlocksStartAt { get; init; }

    public static NifHeader Parse(ReadOnlySpan<byte> data)
    {
        int newline = data.IndexOf((byte)'\n');

        if (newline < 0)
        {
            throw new ModelFormatException("Not a .nif file: no header line found.");
        }

        string signature = Encoding.ASCII.GetString(data[..newline]);

        if (!signature.StartsWith("NetImmerse File Format", StringComparison.Ordinal) &&
            !signature.StartsWith("Gamebryo File Format", StringComparison.Ordinal))
        {
            throw new ModelFormatException($"Unrecognized .nif header: \"{signature}\".");
        }

        int offset = newline + 1;

        if (data.Length < offset + 8)
        {
            throw new ModelFormatException("File is truncated: missing version/block count fields.");
        }

        // Packed as [build, patch, minor, major] in that byte order.
        byte build = data[offset];
        byte patch = data[offset + 1];
        byte minor = data[offset + 2];
        byte major = data[offset + 3];

        uint blockCount = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);

        return new NifHeader
        {
            Signature = signature,
            VersionMajor = major,
            VersionMinor = minor,
            VersionPatch = patch,
            VersionBuild = build,
            BlockCount = blockCount,
            BlocksStartAt = offset + 8,
        };
    }

    public static NifHeader Load(string path) => Parse(File.ReadAllBytes(path));
}
