using System.Buffers.Binary;
using System.Text;

namespace Trafty.Core.Textures;

/// <summary>
/// Reads a standalone .dds file back into RGBA32 pixels — the inverse of
/// <see cref="DdsWriter"/>/<see cref="DdsEncoder"/>. Same header layout already verified
/// against a real terrain entry (see <see cref="DdsWriter"/>'s remarks): 4-byte magic, a
/// 124-byte DDS_HEADER, then the compressed payload. Only BC1 (DXT1) and BC2 (DXT3) FourCCs
/// are supported, matching what this project's encoder produces and what retail archives
/// were found to use — any other FourCC is rejected rather than guessed at.
/// </summary>
public sealed class DdsFile
{
    private const int HeaderSize = 128;

    public required int Width { get; init; }
    public required int Height { get; init; }
    public required DxtFormat Format { get; init; }

    /// <summary>Decoded RGBA32 pixel data, row-major, 4 bytes per pixel.</summary>
    public required byte[] Rgba { get; init; }

    public static DdsFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || !data[..4].SequenceEqual("DDS "u8))
        {
            throw new TextureFormatException("Missing \"DDS \" signature.");
        }

        ReadOnlySpan<byte> header = data[4..HeaderSize];
        int height = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        int width = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);

        ReadOnlySpan<byte> pixelFormat = header[72..104];
        string fourCc = Encoding.ASCII.GetString(pixelFormat.Slice(8, 4)).TrimEnd('\0');

        DxtFormat format = fourCc switch
        {
            "DXT1" => DxtFormat.Bc1,
            "DXT3" => DxtFormat.Bc2,
            _ => throw new TextureFormatException($"Unsupported DDS FourCC \"{fourCc}\" — only DXT1/DXT3 are implemented."),
        };

        byte[] rgba = BlockDecompressor.Decompress(data[HeaderSize..], width, height, format);

        return new DdsFile { Width = width, Height = height, Format = format, Rgba = rgba };
    }

    public static DdsFile Load(string path) => Parse(File.ReadAllBytes(path));
}
