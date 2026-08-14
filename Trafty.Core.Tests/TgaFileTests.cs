using Trafty.Core.Images;
using Xunit;

namespace Trafty.Core.Tests;

public class TgaFileTests
{
    [Fact]
    public void Parse_ReadsRealUiTextureFile()
    {
        TgaFile tga = TgaFile.Load("emoticons.tga");

        Assert.Equal(256, tga.Width);
        Assert.Equal(128, tga.Height);
        Assert.Equal(256 * 128 * 4, tga.Rgba.Length);
    }

    [Fact]
    public void Parse_BottomLeftOriginIsFlippedToTopDown()
    {
        // Verified by hand against the raw bytes: the header's descriptor byte (offset
        // 0x11) is 0x08 — bit 5 clear, meaning bottom-left origin — and the file's first
        // pixel data bytes (right after the 18-byte header) are B=FF G=FF R=FF A=00. Since
        // rows are stored bottom-to-top, that first row must land at the *last* output row
        // after the flip to top-down, not the first.
        TgaFile tga = TgaFile.Load("emoticons.tga");

        int lastRowOffset = (tga.Height - 1) * tga.Width * 4;
        Assert.Equal(255, tga.Rgba[lastRowOffset]);     // R
        Assert.Equal(255, tga.Rgba[lastRowOffset + 1]); // G
        Assert.Equal(255, tga.Rgba[lastRowOffset + 2]); // B
        Assert.Equal(0, tga.Rgba[lastRowOffset + 3]);   // A (transparent background of the sprite sheet)
    }

    [Fact]
    public void Parse_HasBothTransparentAndOpaquePixels()
    {
        // Sanity check that real image content decoded, not just a uniform buffer — an
        // emoticon sprite sheet should have plenty of fully transparent background and
        // plenty of fully opaque icon pixels.
        TgaFile tga = TgaFile.Load("emoticons.tga");

        bool sawTransparent = false;
        bool sawOpaque = false;

        for (int i = 3; i < tga.Rgba.Length; i += 4)
        {
            if (tga.Rgba[i] == 0)
            {
                sawTransparent = true;
            }
            else if (tga.Rgba[i] == 255)
            {
                sawOpaque = true;
            }
        }

        Assert.True(sawTransparent);
        Assert.True(sawOpaque);
    }

    [Fact]
    public void Parse_RejectsMissingHeader()
    {
        Assert.Throws<TgaFormatException>(() => TgaFile.Parse(new byte[10]));
    }

    [Fact]
    public void Parse_RejectsUnsupportedImageType()
    {
        byte[] bytes = File.ReadAllBytes("emoticons.tga");
        bytes[2] = 10; // RLE true-color, not implemented

        Assert.Throws<TgaFormatException>(() => TgaFile.Parse(bytes));
    }

    [Fact]
    public void Parse_RejectsUnsupportedPixelDepth()
    {
        byte[] bytes = File.ReadAllBytes("emoticons.tga");
        bytes[0x10] = 24; // 24bpp, not implemented

        Assert.Throws<TgaFormatException>(() => TgaFile.Parse(bytes));
    }

    [Fact]
    public void Parse_RejectsColorMappedImages()
    {
        byte[] bytes = File.ReadAllBytes("emoticons.tga");
        bytes[1] = 1; // color map present, not implemented

        Assert.Throws<TgaFormatException>(() => TgaFile.Parse(bytes));
    }

    [Fact]
    public void SaveAsPng_ProducesReadablePngBytes()
    {
        TgaFile tga = TgaFile.Load("emoticons.tga");

        using var stream = new MemoryStream();
        TgaExporter.SaveAsPng(tga, stream);

        Assert.True(stream.Length > 0);

        byte[] signature = stream.ToArray()[..8];
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, signature);
    }
}
