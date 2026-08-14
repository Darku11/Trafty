namespace Trafty.Core.Images;

/// <summary>
/// Reads .tga (Truevision TGA) images — used by the DAoC client's UI for texture atlases
/// referenced from UI XML window definitions (e.g. atlantis/emoticons.tga, referenced by
/// chat_window.xml's &lt;Texture&gt; element). Public, long-documented format (Truevision
/// TGA File Format Specification), not reverse engineered — but as with
/// <see cref="Trafty.Core.Images.PcxFile"/>, only the variant actually found in a real file
/// is implemented: image type 2 (uncompressed true-color), 32 bits/pixel (BGRA), no color
/// map. That was verified byte-for-byte against the real emoticons.tga (256x128): header
/// fields, pixel data length (18-byte header + 256*128*4 bytes = exactly the file size minus
/// the 26-byte TGA 2.0 footer), and the "TRUEVISION-XFILE." footer signature all matched.
/// Other TGA variants (RLE-compressed, 24-bit, paletted) are not implemented — this parser
/// rejects them rather than guessing at an untested code path.
///
/// Layout (18-byte header):
///   0x00  byte     ID length (0 in the verified file — no ID field follows)
///   0x01  byte     Color map type (0 = none)
///   0x02  byte     Image type (2 = uncompressed true-color, only value supported)
///   0x03-0x07      Color map spec (unused when Color map type is 0)
///   0x08-0x0B      X/Y origin (ignored — irrelevant for a full-image decode)
///   0x0C  uint16   Width
///   0x0E  uint16   Height
///   0x10  byte     Pixel depth (32 supported; bits/pixel)
///   0x11  byte     Image descriptor — bit 5 selects the vertical origin: 0 = bottom-left
///                  (rows stored bottom-to-top, as in the verified file — flipped here to a
///                  conventional top-down raster), 1 = top-left (stored as-is)
///   0x12  —        Pixel data: Width*Height pixels, 4 bytes each, in B,G,R,A order
/// </summary>
public sealed class TgaFile
{
    private const int HeaderSize = 18;
    private const byte ImageTypeUncompressedTrueColor = 2;
    private const byte OriginTopLeftBit = 0x20;

    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>RGBA pixel data, row-major, top row first, 4 bytes per pixel.</summary>
    public required byte[] Rgba { get; init; }

    public static TgaFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new TgaFormatException($"File is too small to contain a TGA header ({data.Length} bytes).");
        }

        byte idLength = data[0];
        byte colorMapType = data[1];
        byte imageType = data[2];

        if (colorMapType != 0)
        {
            throw new TgaFormatException($"Unsupported TGA color map type {colorMapType} — only type 0 (no color map) is implemented.");
        }

        if (imageType != ImageTypeUncompressedTrueColor)
        {
            throw new TgaFormatException($"Unsupported TGA image type {imageType} — only type 2 (uncompressed true-color) is implemented.");
        }

        int width = data[0x0C] | (data[0x0D] << 8);
        int height = data[0x0E] | (data[0x0F] << 8);
        byte pixelDepth = data[0x10];
        byte descriptor = data[0x11];

        if (pixelDepth != 32)
        {
            throw new TgaFormatException($"Unsupported TGA pixel depth {pixelDepth} — only 32 bits/pixel (BGRA) is implemented.");
        }

        if (width <= 0 || height <= 0)
        {
            throw new TgaFormatException($"Invalid TGA dimensions ({width}x{height}).");
        }

        int pixelDataOffset = HeaderSize + idLength;
        int expectedBytes = width * height * 4;

        if (data.Length < pixelDataOffset + expectedBytes)
        {
            throw new TgaFormatException(
                $"File is truncated: need {expectedBytes} byte(s) of pixel data after the header, " +
                $"only {data.Length - pixelDataOffset} available.");
        }

        bool topLeftOrigin = (descriptor & OriginTopLeftBit) != 0;
        byte[] rgba = new byte[expectedBytes];

        for (int row = 0; row < height; row++)
        {
            int sourceRow = topLeftOrigin ? row : height - 1 - row;
            int sourceOffset = pixelDataOffset + sourceRow * width * 4;
            int destOffset = row * width * 4;

            for (int x = 0; x < width; x++)
            {
                byte b = data[sourceOffset + x * 4];
                byte g = data[sourceOffset + x * 4 + 1];
                byte r = data[sourceOffset + x * 4 + 2];
                byte a = data[sourceOffset + x * 4 + 3];

                rgba[destOffset + x * 4] = r;
                rgba[destOffset + x * 4 + 1] = g;
                rgba[destOffset + x * 4 + 2] = b;
                rgba[destOffset + x * 4 + 3] = a;
            }
        }

        return new TgaFile { Width = width, Height = height, Rgba = rgba };
    }

    public static TgaFile Load(string path) => Parse(File.ReadAllBytes(path));
}
