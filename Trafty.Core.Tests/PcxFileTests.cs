using Trafty.Core.Images;
using Xunit;

namespace Trafty.Core.Tests;

public class PcxFileTests
{
    [Fact]
    public void Parse_ReadsRealZoneTerrainFile()
    {
        PcxFile pcx = PcxFile.Load("terrain.pcx");

        Assert.Equal(256, pcx.Width);
        Assert.Equal(256, pcx.Height);
        Assert.Equal(256 * 256 * 4, pcx.Rgba.Length);
    }

    [Fact]
    public void Parse_FirstPixelMatchesManualRleAndPaletteDecode()
    {
        // Verified by hand against the raw bytes: header ends at 0x80, first RLE bytes are
        // 5b 5d c2 5b (two literals, then a run of two 0x5b), and the trailing palette maps
        // index N to (N, N, N) — a grayscale ramp — for every index seen here.
        PcxFile pcx = PcxFile.Load("terrain.pcx");

        Assert.Equal(0x5B, pcx.Rgba[0]);  // R
        Assert.Equal(0x5B, pcx.Rgba[1]);  // G
        Assert.Equal(0x5B, pcx.Rgba[2]);  // B
        Assert.Equal(255, pcx.Rgba[3]);   // A

        Assert.Equal(0x5D, pcx.Rgba[4]);  // second pixel, still row 0
    }

    [Fact]
    public void Parse_PaletteIsGrayscaleRamp()
    {
        PcxFile pcx = PcxFile.Load("terrain.pcx");

        for (int i = 0; i < pcx.Rgba.Length; i += 4)
        {
            Assert.Equal(pcx.Rgba[i], pcx.Rgba[i + 1]);
            Assert.Equal(pcx.Rgba[i], pcx.Rgba[i + 2]);
        }
    }

    [Fact]
    public void Parse_RejectsMissingSignature()
    {
        byte[] bogus = new byte[200];
        Assert.Throws<PcxFormatException>(() => PcxFile.Parse(bogus));
    }

    [Fact]
    public void Parse_RejectsTruncatedHeader()
    {
        Assert.Throws<PcxFormatException>(() => PcxFile.Parse(new byte[10]));
    }
}
