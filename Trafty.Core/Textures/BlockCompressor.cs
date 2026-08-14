namespace Trafty.Core.Textures;

/// <summary>
/// Encodes RGBA32 pixel data into BC1 (DXT1) or BC2 (DXT3) blocks.
///
/// This uses the "range fit" approach: per 4x4 block, the two RGB565 endpoints are taken
/// directly from the block's minimum and maximum color rather than via principal
/// component analysis. It is not as sharp as squish's cluster fit, but it is simple,
/// dependency free, and fully deterministic, which matters more here than shaving off the
/// last bit of PSNR on UI icons and terrain patches.
/// </summary>
public static class BlockCompressor
{
    /// <summary>
    /// Compresses a full RGBA32 image into a single BCn payload. Width and height must
    /// both be multiples of 4; use <see cref="MipChainBuilder"/> to produce mip levels
    /// that satisfy this.
    /// </summary>
    public static byte[] Compress(ReadOnlySpan<byte> rgba, int width, int height, DxtFormat format)
    {
        if (width <= 0 || height <= 0 || width % 4 != 0 || height % 4 != 0)
        {
            throw new ArgumentException($"Width and height must be positive multiples of 4 (got {width}x{height}).");
        }

        if (rgba.Length < width * height * 4)
        {
            throw new ArgumentException("Pixel buffer is smaller than width * height * 4 bytes.", nameof(rgba));
        }

        int blocksX = width / 4;
        int blocksY = height / 4;
        int blockSize = format.BlockSize();
        byte[] output = new byte[blocksX * blocksY * blockSize];

        Span<byte> block = stackalloc byte[16 * 4]; // 16 pixels, RGBA each

        int outOffset = 0;

        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                ExtractBlock(rgba, width, height, bx * 4, by * 4, block);

                if (format == DxtFormat.Bc2)
                {
                    WriteAlphaBlock(block, output.AsSpan(outOffset, 8));
                    outOffset += 8;
                }

                WriteColorBlock(block, output.AsSpan(outOffset, 8));
                outOffset += 8;
            }
        }

        return output;
    }

    private static void ExtractBlock(ReadOnlySpan<byte> rgba, int width, int height, int startX, int startY, Span<byte> block)
    {
        for (int y = 0; y < 4; y++)
        {
            // Edge blocks on non-multiple-of-4 source content are not expected here (the
            // mip builder pads to 4x4 boundaries), but clamping keeps this safe even if
            // it is ever called with a partial buffer.
            int sy = Math.Min(startY + y, height - 1);

            for (int x = 0; x < 4; x++)
            {
                int sx = Math.Min(startX + x, width - 1);
                int srcOffset = (sy * width + sx) * 4;
                int dstOffset = (y * 4 + x) * 4;

                rgba.Slice(srcOffset, 4).CopyTo(block.Slice(dstOffset, 4));
            }
        }
    }

    /// <summary>
    /// Writes the shared BC1-style 8-byte RGB block: two RGB565 endpoints followed by
    /// sixteen 2-bit palette indices.
    /// </summary>
    private static void WriteColorBlock(ReadOnlySpan<byte> block, Span<byte> destination)
    {
        (ushort min565, ushort max565) = FindColorEndpoints(block);

        // BC1 opaque (4 color) mode requires color0 > color1 numerically. Since our
        // encoder never emits the punch-through-alpha 3-color mode, force that ordering
        // even on flat blocks where min == max.
        if (min565 == max565)
        {
            if (max565 < 0xFFFF)
            {
                max565++;
            }
            else
            {
                min565--;
            }
        }

        ushort color0 = max565;
        ushort color1 = min565;

        Span<Rgb> palette = stackalloc Rgb[4];
        palette[0] = Rgb.From565(color0);
        palette[1] = Rgb.From565(color1);
        palette[2] = Rgb.Lerp(palette[0], palette[1], 1, 3);
        palette[3] = Rgb.Lerp(palette[0], palette[1], 2, 3);

        destination[0] = (byte)(color0 & 0xFF);
        destination[1] = (byte)(color0 >> 8);
        destination[2] = (byte)(color1 & 0xFF);
        destination[3] = (byte)(color1 >> 8);

        uint indices = 0;

        for (int i = 0; i < 16; i++)
        {
            var pixel = new Rgb(block[i * 4], block[i * 4 + 1], block[i * 4 + 2]);
            int best = ClosestPaletteEntry(pixel, palette);
            indices |= (uint)best << (i * 2);
        }

        destination[4] = (byte)(indices & 0xFF);
        destination[5] = (byte)((indices >> 8) & 0xFF);
        destination[6] = (byte)((indices >> 16) & 0xFF);
        destination[7] = (byte)((indices >> 24) & 0xFF);
    }

    /// <summary>
    /// Writes the BC2 explicit alpha block: sixteen 4-bit alpha values, two pixels per
    /// byte, low nibble first. No interpolation, matching what the retail packer emits.
    /// </summary>
    private static void WriteAlphaBlock(ReadOnlySpan<byte> block, Span<byte> destination)
    {
        for (int i = 0; i < 8; i++)
        {
            byte a0 = QuantizeAlpha(block[(i * 2) * 4 + 3]);
            byte a1 = QuantizeAlpha(block[(i * 2 + 1) * 4 + 3]);

            destination[i] = (byte)(a0 | (a1 << 4));
        }
    }

    private static byte QuantizeAlpha(byte alpha8) => (byte)((alpha8 + 8) / 17); // 0..255 -> 0..15, rounded

    private static (ushort min, ushort max) FindColorEndpoints(ReadOnlySpan<byte> block)
    {
        byte rMin = 255, gMin = 255, bMin = 255;
        byte rMax = 0, gMax = 0, bMax = 0;

        for (int i = 0; i < 16; i++)
        {
            byte r = block[i * 4];
            byte g = block[i * 4 + 1];
            byte b = block[i * 4 + 2];

            if (r < rMin) rMin = r;
            if (g < gMin) gMin = g;
            if (b < bMin) bMin = b;
            if (r > rMax) rMax = r;
            if (g > gMax) gMax = g;
            if (b > bMax) bMax = b;
        }

        ushort min = Rgb.To565(rMin, gMin, bMin);
        ushort max = Rgb.To565(rMax, gMax, bMax);

        return (min, max);
    }

    private static int ClosestPaletteEntry(Rgb pixel, ReadOnlySpan<Rgb> palette)
    {
        int bestIndex = 0;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < palette.Length; i++)
        {
            int dr = pixel.R - palette[i].R;
            int dg = pixel.G - palette[i].G;
            int db = pixel.B - palette[i].B;
            int distance = dr * dr + dg * dg + db * db;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private readonly struct Rgb
    {
        public Rgb(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        public static ushort To565(byte r, byte g, byte b) =>
            (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));

        public static Rgb From565(ushort value)
        {
            int r5 = (value >> 11) & 0x1F;
            int g6 = (value >> 5) & 0x3F;
            int b5 = value & 0x1F;

            // Expand to 8 bits by replicating the high bits into the low bits, the same
            // technique GPUs use when sampling BC1 textures.
            byte r = (byte)((r5 << 3) | (r5 >> 2));
            byte g = (byte)((g6 << 2) | (g6 >> 4));
            byte b = (byte)((b5 << 3) | (b5 >> 2));

            return new Rgb(r, g, b);
        }

        public static Rgb Lerp(Rgb a, Rgb b, int weightB, int totalWeight)
        {
            int weightA = totalWeight - weightB;

            byte r = (byte)((a.R * weightA + b.R * weightB) / totalWeight);
            byte g = (byte)((a.G * weightA + b.G * weightB) / totalWeight);
            byte bl = (byte)((a.B * weightA + b.B * weightB) / totalWeight);

            return new Rgb(r, g, bl);
        }
    }
}
