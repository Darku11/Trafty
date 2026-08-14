using System.Buffers.Binary;

namespace Trafty.Core.Weather;

/// <summary>
/// Reads .col files — Modul B's color/lighting/weather tables. Located via
/// <c>color.dat</c>, which is a plain-text config pointing at the actual data file:
///
///   [color_tables]
///   00=system.col
///
/// SYSTEM.COL itself has no header at all: it is a headerless raw RGB565 raster,
/// 128 x 66 pixels, top row first. That size was not guessed — 128 * 66 * 2 bytes
/// equals the file's exact byte count with zero remainder, and rendering the pixels
/// (confirmed against your real SYSTEM.COL) produces a clean image with four vertical
/// color bands and a smooth dithered gradient in each — consistent with a set of
/// distinct lighting/weather states, each with a top-to-bottom intensity gradient,
/// rather than noise or an unrelated binary blob.
///
/// What the four bands individually represent (e.g. specific weather states, times of
/// day, or zone lighting presets) is not yet confirmed and is not asserted here.
/// </summary>
public sealed class SystemColFile
{
    /// <summary>Fixed width, confirmed by exact file-size match against real data.</summary>
    public const int Width = 128;

    /// <summary>Fixed height, confirmed by exact file-size match against real data.</summary>
    public const int Height = 66;

    private const int ExpectedByteCount = Width * Height * 2;

    /// <summary>Raw RGB565 pixel values, row-major, top row first.</summary>
    public required ushort[] Pixels { get; init; }

    public (byte R, byte G, byte B) GetPixel(int x, int y)
    {
        if ((uint)x >= Width || (uint)y >= Height)
        {
            throw new ArgumentOutOfRangeException(x >= Width ? nameof(x) : nameof(y), "Coordinate is outside the 128x66 raster.");
        }

        return Decode565(Pixels[y * Width + x]);
    }

    /// <summary>
    /// Overwrites one pixel in place (in memory only — call <see cref="Save"/> to persist).
    /// Supports Modul B's weather/atmosphere tweaker: editing a table pixel-by-pixel and
    /// previewing the result before writing it back.
    /// </summary>
    public void SetPixel(int x, int y, byte r, byte g, byte b)
    {
        if ((uint)x >= Width || (uint)y >= Height)
        {
            throw new ArgumentOutOfRangeException(x >= Width ? nameof(x) : nameof(y), "Coordinate is outside the 128x66 raster.");
        }

        Pixels[y * Width + x] = Encode565(r, g, b);
    }

    /// <summary>Writes the raster back out as a headerless raw RGB565 file, matching the original format exactly.</summary>
    public void Save(string path)
    {
        byte[] bytes = new byte[ExpectedByteCount];

        for (int i = 0; i < Pixels.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2, 2), Pixels[i]);
        }

        File.WriteAllBytes(path, bytes);
    }

    private static ushort Encode565(byte r, byte g, byte b) =>
        (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));

    /// <summary>
    /// Decodes the full raster into tightly packed RGB24 bytes (row-major, 3 bytes per
    /// pixel), suitable for handing to an image encoder.
    /// </summary>
    public byte[] ToRgb24()
    {
        byte[] rgb = new byte[Width * Height * 3];

        for (int i = 0; i < Pixels.Length; i++)
        {
            (byte r, byte g, byte b) = Decode565(Pixels[i]);
            rgb[i * 3] = r;
            rgb[i * 3 + 1] = g;
            rgb[i * 3 + 2] = b;
        }

        return rgb;
    }

    private static (byte R, byte G, byte B) Decode565(ushort value)
    {
        int r5 = (value >> 11) & 0x1F;
        int g6 = (value >> 5) & 0x3F;
        int b5 = value & 0x1F;

        byte r = (byte)((r5 << 3) | (r5 >> 2));
        byte g = (byte)((g6 << 2) | (g6 >> 4));
        byte b = (byte)((b5 << 3) | (b5 >> 2));

        return (r, g, b);
    }

    public static SystemColFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length != ExpectedByteCount)
        {
            throw new WeatherFormatException(
                $"Expected exactly {ExpectedByteCount} bytes for a {Width}x{Height} RGB565 raster, got {data.Length}.");
        }

        ushort[] pixels = new ushort[Width * Height];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(i * 2, 2));
        }

        return new SystemColFile { Pixels = pixels };
    }

    public static SystemColFile Load(string path) => Parse(File.ReadAllBytes(path));
}
