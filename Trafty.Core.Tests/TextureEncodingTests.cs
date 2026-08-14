using Trafty.Core.Textures;
using Xunit;

namespace Trafty.Core.Tests;

public sealed class TextureEncodingTests
{
    [Fact]
    public void BlockCompressor_SolidOpaqueColor_RoundTripsCloselyThroughBc1()
    {
        byte[] pixels = SolidBlock(4, 4, r: 200, g: 40, b: 10, a: 255);

        byte[] compressed = BlockCompressor.Compress(pixels, 4, 4, DxtFormat.Bc1);

        Assert.Equal(8, compressed.Length);

        // A flat block should decode back to (almost) the same color: BC1 quantizes to
        // RGB565, so single-digit per-channel error is expected and correct, not a bug.
        (byte r, byte g, byte b) = DecodeBc1Pixel(compressed, pixelIndex: 0);

        Assert.InRange(r, 190, 210);
        Assert.InRange(g, 35, 45);
        Assert.InRange(b, 5, 20);
    }

    [Fact]
    public void BlockCompressor_Bc2_PreservesSharpAlphaSteps()
    {
        byte[] pixels = new byte[4 * 4 * 4];

        // Left half opaque, right half fully transparent - BC2's explicit alpha should
        // keep that hard edge instead of blending it, unlike interpolated DXT5 alpha.
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int o = (y * 4 + x) * 4;
                pixels[o + 3] = (byte)(x < 2 ? 255 : 0);
            }
        }

        byte[] compressed = BlockCompressor.Compress(pixels, 4, 4, DxtFormat.Bc2);

        Assert.Equal(16, compressed.Length);

        byte alphaByte0 = compressed[0]; // pixels (0,0) and (1,0): both opaque
        byte alphaByte1 = compressed[1]; // pixels (2,0) and (3,0): both transparent

        Assert.Equal(0xFF, alphaByte0);
        Assert.Equal(0x00, alphaByte1);
    }

    [Fact]
    public void BlockCompressor_RejectsDimensionsNotMultipleOfFour()
    {
        byte[] pixels = new byte[5 * 4 * 4];

        Assert.Throws<ArgumentException>(() => BlockCompressor.Compress(pixels, 5, 4, DxtFormat.Bc1));
    }

    [Fact]
    public void DdsWriter_ProducesHeaderMatchingRetailLayout()
    {
        byte[] compressed = new byte[16]; // one BC2 block
        byte[] dds = DdsWriter.Write(4, 4, DxtFormat.Bc2, compressed);

        Assert.Equal("DDS ", System.Text.Encoding.ASCII.GetString(dds, 0, 4));
        Assert.Equal(124u, BitConverter.ToUInt32(dds, 4));   // dwSize
        Assert.Equal(4u, BitConverter.ToUInt32(dds, 12));   // dwHeight
        Assert.Equal(4u, BitConverter.ToUInt32(dds, 16));   // dwWidth
        Assert.Equal("DXT3", System.Text.Encoding.ASCII.GetString(dds, 84, 4));
        Assert.Equal(0x1000u, BitConverter.ToUInt32(dds, 108)); // dwCaps1
        Assert.Equal(128 + 16, dds.Length);
    }

    [Fact]
    public void DdsEncoder_NameMipLevels_MatchesRetailNamingConvention()
    {
        IReadOnlyList<string> names = DdsEncoder.NameMipLevels("patch0000", 3);

        Assert.Equal(new[] { "patch0000-00.dds", "patch0000-01.dds", "patch0000-02.dds" }, names);
    }

    private static byte[] SolidBlock(int width, int height, byte r, byte g, byte b, byte a)
    {
        byte[] pixels = new byte[width * height * 4];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = a;
        }

        return pixels;
    }

    /// <summary>Minimal BC1 decoder: resolves one pixel through its actual 2-bit index.</summary>
    private static (byte r, byte g, byte b) DecodeBc1Pixel(byte[] block, int pixelIndex)
    {
        ushort color0 = (ushort)(block[0] | (block[1] << 8));
        ushort color1 = (ushort)(block[2] | (block[3] << 8));

        (byte r, byte g, byte b)[] palette = new (byte, byte, byte)[4];
        palette[0] = Unpack565(color0);
        palette[1] = Unpack565(color1);
        palette[2] = Lerp(palette[0], palette[1], 1, 3);
        palette[3] = Lerp(palette[0], palette[1], 2, 3);

        uint indices = (uint)(block[4] | (block[5] << 8) | (block[6] << 16) | (block[7] << 24));
        int index = (int)((indices >> (pixelIndex * 2)) & 0x3);

        return palette[index];
    }

    private static (byte r, byte g, byte b) Unpack565(ushort value)
    {
        int r5 = (value >> 11) & 0x1F;
        int g6 = (value >> 5) & 0x3F;
        int b5 = value & 0x1F;

        byte r = (byte)((r5 << 3) | (r5 >> 2));
        byte g = (byte)((g6 << 2) | (g6 >> 4));
        byte b = (byte)((b5 << 3) | (b5 >> 2));

        return (r, g, b);
    }

    private static (byte r, byte g, byte b) Lerp((byte r, byte g, byte b) a, (byte r, byte g, byte b) b, int weightB, int totalWeight)
    {
        int weightA = totalWeight - weightB;

        return (
            (byte)((a.r * weightA + b.r * weightB) / totalWeight),
            (byte)((a.g * weightA + b.g * weightB) / totalWeight),
            (byte)((a.b * weightA + b.b * weightB) / totalWeight));
    }
}
