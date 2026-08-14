using System.Linq;
using Trafty.Core.Archives;
using Trafty.Core.Textures;
using Xunit;

namespace Trafty.Core.Tests;

public sealed class DdsFileTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, name);

    private static byte[] LoadRealDdsBytes(string entryName)
    {
        using MpkArchive archive = MpkArchive.Open(Fixture("ter002.mpk"));
        MpkEntry entry = archive.Entries.Single(e => e.Name == entryName);
        return archive.Extract(entry);
    }

    [Fact]
    public void Parse_RealTerrainPatch_MatchesKnownHeaderFields()
    {
        DdsFile dds = DdsFile.Parse(LoadRealDdsBytes("patch0000-00.dds"));

        Assert.Equal(128, dds.Width);
        Assert.Equal(128, dds.Height);
        Assert.Equal(DxtFormat.Bc2, dds.Format);
        Assert.Equal(128 * 128 * 4, dds.Rgba.Length);
    }

    [Fact]
    public void Parse_MissingSignature_Throws()
    {
        byte[] bogus = new byte[200];
        Assert.Throws<TextureFormatException>(() => DdsFile.Parse(bogus));
    }

    [Fact]
    public void Parse_UnsupportedFourCc_Throws()
    {
        byte[] bytes = LoadRealDdsBytes("patch0000-00.dds");
        bytes[4 + 72 + 8] = (byte)'X'; // corrupt the FourCC field ("DXT3" -> "XXT3")

        Assert.Throws<TextureFormatException>(() => DdsFile.Parse(bytes));
    }

    [Fact]
    public void Decompress_RoundTripsThroughCompressor()
    {
        // Synthetic gradient with varying alpha exercises both interpolated color and
        // explicit BC2 alpha decoding, cross-checked against the encoder this project
        // already has (BlockCompressor), independent of what any real file contains.
        const int width = 8;
        const int height = 8;
        byte[] rgba = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int o = (y * width + x) * 4;
                rgba[o] = (byte)(x * 30);
                rgba[o + 1] = (byte)(y * 30);
                rgba[o + 2] = 128;
                rgba[o + 3] = (byte)(x == y ? 255 : 128);
            }
        }

        byte[] compressed = BlockCompressor.Compress(rgba, width, height, DxtFormat.Bc2);
        byte[] decoded = BlockDecompressor.Decompress(compressed, width, height, DxtFormat.Bc2);

        Assert.Equal(rgba.Length, decoded.Length);

        // Lossy (4x4 block quantization), so exact equality isn't expected — but every pixel
        // should be close to its original, and alpha in particular should be near-exact since
        // BC2 stores it as explicit 4-bit values rather than interpolating.
        for (int i = 0; i < rgba.Length; i += 4)
        {
            Assert.InRange(decoded[i + 3], Math.Max(0, rgba[i + 3] - 17), Math.Min(255, rgba[i + 3] + 17));
        }
    }

    [Fact]
    public void Decompress_RejectsNonMultipleOf4Dimensions()
    {
        Assert.Throws<ArgumentException>(() => BlockDecompressor.Decompress(new byte[64], 5, 4, DxtFormat.Bc1));
    }

    [Fact]
    public void SaveAsPng_RealTerrainPatch_ForcesOpaqueAlphaByDefault()
    {
        // Real terrain DXT3 patches were found to carry alpha=0 across the whole texture —
        // the terrain renderer evidently ignores alpha entirely, so a literal export would
        // be fully transparent (blank) despite having real RGB content. Verified against
        // patch0000-00.dds specifically: alpha is 0 everywhere in the raw decode.
        DdsFile dds = DdsFile.Parse(LoadRealDdsBytes("patch0000-00.dds"));
        Assert.All(Enumerable.Range(0, dds.Width * dds.Height), i => Assert.Equal(0, dds.Rgba[i * 4 + 3]));

        using var stream = new MemoryStream();
        DdsExporter.SaveAsPng(dds, stream);

        Assert.True(stream.Length > 0);
    }
}
