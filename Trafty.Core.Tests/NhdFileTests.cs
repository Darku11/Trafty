using Trafty.Core.Archives;
using Trafty.Core.WorldProps;
using Xunit;

namespace Trafty.Core.Tests;

public sealed class NhdFileTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, name);

    [Fact]
    public void Parse_FirstSample_MatchesKnownFields()
    {
        NhdFile nhd = NhdFile.Load(Fixture("1struinedtemple.nhd"));

        Assert.Equal(1, nhd.Version);
        Assert.Equal("1struinedtemple.nif", nhd.ModelName);
        Assert.Equal(-51, nhd.MinX);
        Assert.Equal(59, nhd.MaxX);
        Assert.Equal(-30, nhd.MinY);
        Assert.Equal(15, nhd.MaxY);
        Assert.Equal(110, nhd.GridWidth);
        Assert.Equal(45, nhd.GridHeight);
        Assert.Equal(110 * 45, nhd.Grid.Length);
    }

    [Fact]
    public void Parse_SecondSample_GridSizeMatchesRemainingBytes()
    {
        NhdFile nhd = NhdFile.Load(Fixture("1struinedtemple02.nhd"));

        Assert.Equal("1struinedtemple02.nif", nhd.ModelName);
        Assert.Equal(66, nhd.GridWidth);
        Assert.Equal(44, nhd.GridHeight);
        Assert.Equal(66 * 44, nhd.Grid.Length);
    }

    [Fact]
    public void Parse_GridValueAt_ResolvesRowMajorIndex()
    {
        NhdFile nhd = NhdFile.Load(Fixture("1struinedtemple.nhd"));

        Assert.Equal(nhd.Grid[0], nhd.GridValueAt(0, 0));
        Assert.Equal(nhd.Grid[1], nhd.GridValueAt(1, 0));
        Assert.Equal(nhd.Grid[nhd.GridWidth], nhd.GridValueAt(0, 1));
    }

    [Fact]
    public void Parse_OutOfRangeCoordinate_Throws()
    {
        NhdFile nhd = NhdFile.Load(Fixture("1struinedtemple.nhd"));

        Assert.Throws<ArgumentOutOfRangeException>(() => nhd.GridValueAt(nhd.GridWidth, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => nhd.GridValueAt(0, nhd.GridHeight));
    }

    [Theory]
    [InlineData("3rdruinedpiece.nhd", 20, 28)]
    [InlineData("aecentbarricade.nhd", 64, 63)]
    [InlineData("aegcliffpiece2.nhd", 137, 126)]
    public void Parse_AdditionalSamples_GridSizeMatchesRemainingBytes(string fileName, int expectedWidth, int expectedHeight)
    {
        NhdFile nhd = NhdFile.Load(Fixture(fileName));

        Assert.Equal(expectedWidth, nhd.GridWidth);
        Assert.Equal(expectedHeight, nhd.GridHeight);
        Assert.Equal(expectedWidth * expectedHeight, nhd.Grid.Length);
    }

    [Fact]
    public void Parse_LargerObjects_HaveLargerMaxGridValues()
    {
        // Across five real samples, the non-sentinel maximum grid value grows with the
        // object's physical size — evidence for (not proof of) a per-cell heightfield.
        // -2500 is excluded as the confirmed "no geometry" sentinel.
        int MaxNonSentinel(string fileName)
        {
            NhdFile nhd = NhdFile.Load(Fixture(fileName));
            return nhd.Grid.Where(v => v != -2500).DefaultIfEmpty((short)0).Max();
        }

        int small = MaxNonSentinel("3rdruinedpiece.nhd");
        int medium = MaxNonSentinel("aecentbarricade.nhd");
        int large = MaxNonSentinel("aegcliffpiece2.nhd");

        Assert.True(small < medium, $"expected small ({small}) < medium ({medium})");
        Assert.True(medium < large, $"expected medium ({medium}) < large ({large})");
    }

    [Fact]
    public void Parse_WrongMagic_Throws()
    {
        byte[] bytes = File.ReadAllBytes(Fixture("1struinedtemple.nhd"));
        bytes[0] = (byte)'X';

        Assert.Throws<WorldPropFormatException>(() => NhdFile.Parse(bytes));
    }

    [Fact]
    public void Parse_TruncatedFile_Throws()
    {
        byte[] truncated = File.ReadAllBytes(Fixture("1struinedtemple.nhd"))[..5];

        Assert.Throws<WorldPropFormatException>(() => NhdFile.Parse(truncated));
    }

    [Fact]
    public void NpkArchive_UsesTheSameMpakFormatAsMpk()
    {
        // .npk carries no format of its own: it is an MPAK container, verified against
        // 1struinedtemple.npk, which holds exactly one entry — the referenced .nif model.
        using MpkArchive archive = MpkArchive.Open(Fixture("1struinedtemple.npk"));

        Assert.Single(archive.Entries);

        MpkEntry entry = archive.Entries[0];
        Assert.Equal("1struinedtemple.NIF", entry.Name);

        byte[] model = archive.Extract(entry);
        Assert.Equal((int)entry.UncompressedSize, model.Length);
    }
}
