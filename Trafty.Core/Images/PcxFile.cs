namespace Trafty.Core.Images;

/// <summary>
/// Reads .pcx (ZSoft Paintbrush) images — used by the DAoC client for zone terrain textures
/// (terrain.pcx, densemap.pcx, grassmap.pcx, shademap.pcx, water.pcx, offset.pcx, shadow.pcx,
/// found alongside fixtures.csv/nifs.csv/bound.csv in a zone's dat archive, e.g. dat003.mpk).
///
/// PCX is a publicly documented format (ZSoft spec, 1990), not reverse-engineered — but only
/// the variant actually found in a real file is supported: 8 bits/pixel, 1 color plane,
/// RLE-encoded, with a 256-color VGA palette appended after the pixel data. That combination
/// was verified byte-for-byte against zone003's terrain.pcx (256x256): header fields, RLE
/// stream, and the trailing 0x0C palette marker + 768 palette bytes all matched the spec
/// exactly. Other PCX variants (different bit depths, multi-plane, uncompressed) are not
/// implemented — this parser rejects them rather than guessing at an untested code path.
///
/// Layout:
///   0x00        byte     Manufacturer, always 0x0A
///   0x01        byte     Version
///   0x02        byte     Encoding, 1 = RLE (only value supported)
///   0x03        byte     BitsPerPixel (only 8 supported)
///   0x04-0x0B   int16 x4 Xmin, Ymin, Xmax, Ymax — width = Xmax-Xmin+1, height = Ymax-Ymin+1
///   0x41        byte     NPlanes (only 1 supported)
///   0x42-0x43   uint16   BytesPerLine — RLE-decoded bytes per scanline per plane
///   0x80        —        pixel data starts here (fixed 128-byte header)
///   EOF-769     byte     palette marker, 0x0C, followed by 768 bytes of 256 RGB triplets
/// </summary>
public sealed class PcxFile
{
    private const int HeaderSize = 128;
    private const int PaletteSize = 256 * 3;
    private const byte PaletteMarker = 0x0C;

    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>RGBA pixel data, row-major, 4 bytes per pixel, opaque (alpha always 255).</summary>
    public required byte[] Rgba { get; init; }

    public static PcxFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new PcxFormatException($"File is too small to contain a PCX header ({data.Length} bytes).");
        }

        if (data[0] != 0x0A)
        {
            throw new PcxFormatException("Missing PCX signature (manufacturer byte).");
        }

        byte encoding = data[2];
        byte bitsPerPixel = data[3];

        if (encoding != 1)
        {
            throw new PcxFormatException($"Unsupported PCX encoding {encoding} — only RLE (1) is implemented.");
        }

        if (bitsPerPixel != 8)
        {
            throw new PcxFormatException($"Unsupported PCX bit depth {bitsPerPixel} — only 8 bits/pixel is implemented.");
        }

        int xMin = ReadInt16(data, 0x04);
        int yMin = ReadInt16(data, 0x06);
        int xMax = ReadInt16(data, 0x08);
        int yMax = ReadInt16(data, 0x0A);
        int width = xMax - xMin + 1;
        int height = yMax - yMin + 1;

        if (width <= 0 || height <= 0)
        {
            throw new PcxFormatException($"Invalid PCX dimensions ({width}x{height}).");
        }

        byte planes = data[0x41];

        if (planes != 1)
        {
            throw new PcxFormatException($"Unsupported PCX plane count {planes} — only 1 plane (paletted) is implemented.");
        }

        int bytesPerLine = ReadInt16(data, 0x42);

        if (data.Length < HeaderSize + PaletteSize + 1 || data[^(PaletteSize + 1)] != PaletteMarker)
        {
            throw new PcxFormatException("Missing trailing 256-color palette (0x0C marker not found).");
        }

        ReadOnlySpan<byte> palette = data[^PaletteSize..];
        byte[] indices = DecodeRle(data[HeaderSize..^(PaletteSize + 1)], bytesPerLine, height);

        byte[] rgba = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte paletteIndex = indices[y * bytesPerLine + x];
                int paletteOffset = paletteIndex * 3;
                int pixelOffset = (y * width + x) * 4;

                rgba[pixelOffset] = palette[paletteOffset];
                rgba[pixelOffset + 1] = palette[paletteOffset + 1];
                rgba[pixelOffset + 2] = palette[paletteOffset + 2];
                rgba[pixelOffset + 3] = 255;
            }
        }

        return new PcxFile { Width = width, Height = height, Rgba = rgba };
    }

    public static PcxFile Load(string path) => Parse(File.ReadAllBytes(path));

    private static short ReadInt16(ReadOnlySpan<byte> data, int offset) =>
        (short)(data[offset] | (data[offset + 1] << 8));

    /// <summary>
    /// Decodes the PCX RLE scheme: bytes with their top two bits set encode a run (low 6 bits
    /// = count 1-63, followed by the repeated value byte); any other byte is a literal pixel.
    /// Each scanline decodes to exactly <paramref name="bytesPerLine"/> bytes.
    /// </summary>
    private static byte[] DecodeRle(ReadOnlySpan<byte> data, int bytesPerLine, int height)
    {
        byte[] output = new byte[bytesPerLine * height];
        int outPos = 0;
        int inPos = 0;

        while (outPos < output.Length)
        {
            if (inPos >= data.Length)
            {
                throw new PcxFormatException("RLE stream ended before all scanlines were decoded.");
            }

            byte b = data[inPos++];

            if ((b & 0xC0) == 0xC0)
            {
                int count = b & 0x3F;

                if (inPos >= data.Length)
                {
                    throw new PcxFormatException("RLE run byte is missing its value byte.");
                }

                byte value = data[inPos++];

                for (int i = 0; i < count && outPos < output.Length; i++)
                {
                    output[outPos++] = value;
                }
            }
            else
            {
                output[outPos++] = b;
            }
        }

        return output;
    }
}
