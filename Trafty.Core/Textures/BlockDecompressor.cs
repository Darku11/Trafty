namespace Trafty.Core.Textures;

/// <summary>
/// Decodes BC1 (DXT1) / BC2 (DXT3) blocks back into RGBA32 pixels — the inverse of
/// <see cref="BlockCompressor"/>, following the same public, standard DXT block layout
/// (no reverse engineering; this is how every DXT decoder works). Needed for a thumbnail
/// preview of a texture already stored in an archive, as opposed to encoding a new one.
/// </summary>
public static class BlockDecompressor
{
    /// <summary>
    /// Decompresses a full BCn payload into RGBA32 pixel data. Width and height must both
    /// be multiples of 4, matching what <see cref="BlockCompressor.Compress"/> requires.
    /// </summary>
    public static byte[] Decompress(ReadOnlySpan<byte> compressed, int width, int height, DxtFormat format)
    {
        if (width <= 0 || height <= 0 || width % 4 != 0 || height % 4 != 0)
        {
            throw new ArgumentException($"Width and height must be positive multiples of 4 (got {width}x{height}).");
        }

        int blocksX = width / 4;
        int blocksY = height / 4;
        int blockSize = format.BlockSize();
        int expectedLength = blocksX * blocksY * blockSize;

        if (compressed.Length < expectedLength)
        {
            throw new ArgumentException(
                $"Compressed data is too short: need {expectedLength} byte(s) for a {width}x{height} {format} image, got {compressed.Length}.");
        }

        byte[] rgba = new byte[width * height * 4];
        Span<byte> blockPixels = stackalloc byte[16 * 4];
        int inOffset = 0;

        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                Span<byte> alphaBlock = default;

                if (format == DxtFormat.Bc2)
                {
                    alphaBlock = compressed.Slice(inOffset, 8).ToArray();
                    inOffset += 8;
                }

                ReadColorBlock(compressed.Slice(inOffset, 8), blockPixels);
                inOffset += 8;

                if (format == DxtFormat.Bc2)
                {
                    ApplyExplicitAlpha(alphaBlock, blockPixels);
                }

                WriteBlock(rgba, width, height, bx * 4, by * 4, blockPixels);
            }
        }

        return rgba;
    }

    private static void ReadColorBlock(ReadOnlySpan<byte> block, Span<byte> pixels)
    {
        ushort c0 = (ushort)(block[0] | (block[1] << 8));
        ushort c1 = (ushort)(block[2] | (block[3] << 8));

        (byte R, byte G, byte B) color0 = Unpack565(c0);
        (byte R, byte G, byte B) color1 = Unpack565(c1);

        var palette = new (byte R, byte G, byte B, byte A)[4];
        palette[0] = (color0.R, color0.G, color0.B, 255);
        palette[1] = (color1.R, color1.G, color1.B, 255);

        if (c0 > c1)
        {
            palette[2] = (
                (byte)((2 * color0.R + color1.R) / 3),
                (byte)((2 * color0.G + color1.G) / 3),
                (byte)((2 * color0.B + color1.B) / 3),
                255);
            palette[3] = (
                (byte)((color0.R + 2 * color1.R) / 3),
                (byte)((color0.G + 2 * color1.G) / 3),
                (byte)((color0.B + 2 * color1.B) / 3),
                255);
        }
        else
        {
            palette[2] = (
                (byte)((color0.R + color1.R) / 2),
                (byte)((color0.G + color1.G) / 2),
                (byte)((color0.B + color1.B) / 2),
                255);
            palette[3] = (0, 0, 0, 0); // transparent black — only reachable for BC1
        }

        uint indices = (uint)(block[4] | (block[5] << 8) | (block[6] << 16) | (block[7] << 24));

        for (int i = 0; i < 16; i++)
        {
            int index = (int)((indices >> (i * 2)) & 0x3);
            (byte r, byte g, byte b, byte a) = palette[index];
            pixels[i * 4] = r;
            pixels[i * 4 + 1] = g;
            pixels[i * 4 + 2] = b;
            pixels[i * 4 + 3] = a;
        }
    }

    /// <summary>BC2's explicit alpha block: 16 pixels x 4 bits each, non-interpolated.</summary>
    private static void ApplyExplicitAlpha(ReadOnlySpan<byte> alphaBlock, Span<byte> pixels)
    {
        for (int i = 0; i < 16; i++)
        {
            byte packed = alphaBlock[i / 2];
            int nibble = (i % 2 == 0) ? packed & 0xF : (packed >> 4) & 0xF;
            pixels[i * 4 + 3] = (byte)(nibble * 17); // scale 4-bit (0-15) to 8-bit (0-255)
        }
    }

    private static (byte R, byte G, byte B) Unpack565(ushort value)
    {
        int r5 = (value >> 11) & 0x1F;
        int g6 = (value >> 5) & 0x3F;
        int b5 = value & 0x1F;

        byte r = (byte)((r5 << 3) | (r5 >> 2));
        byte g = (byte)((g6 << 2) | (g6 >> 4));
        byte b = (byte)((b5 << 3) | (b5 >> 2));

        return (r, g, b);
    }

    private static void WriteBlock(byte[] rgba, int width, int height, int startX, int startY, ReadOnlySpan<byte> block)
    {
        for (int y = 0; y < 4; y++)
        {
            int py = startY + y;

            if (py >= height)
            {
                continue;
            }

            for (int x = 0; x < 4; x++)
            {
                int px = startX + x;

                if (px >= width)
                {
                    continue;
                }

                int srcOffset = (y * 4 + x) * 4;
                int dstOffset = (py * width + px) * 4;
                rgba[dstOffset] = block[srcOffset];
                rgba[dstOffset + 1] = block[srcOffset + 1];
                rgba[dstOffset + 2] = block[srcOffset + 2];
                rgba[dstOffset + 3] = block[srcOffset + 3];
            }
        }
    }
}
