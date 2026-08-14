using Trafty.Core.Models;
using Xunit;

namespace Trafty.Core.Tests;

public sealed class NifHeaderTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, name);

    [Fact]
    public void Parse_RealSample_MatchesKnownFields()
    {
        NifHeader header = NifHeader.Load(Fixture("nif_header_sample.bin"));

        Assert.Equal("NetImmerse File Format, Version 4.2.2.0", header.Signature);
        Assert.Equal(4, header.VersionMajor);
        Assert.Equal(2, header.VersionMinor);
        Assert.Equal(2, header.VersionPatch);
        Assert.Equal(0, header.VersionBuild);
        Assert.Equal("4.2.2.0", header.VersionDisplay);
        Assert.Equal(232u, header.BlockCount);
    }

    [Fact]
    public void Parse_MissingNewline_Throws()
    {
        byte[] bytes = "not a nif file at all"u8.ToArray();

        Assert.Throws<ModelFormatException>(() => NifHeader.Parse(bytes));
    }

    [Fact]
    public void Parse_WrongSignature_Throws()
    {
        byte[] bytes = "Some Other Format, Version 1.0.0.0\nrest"u8.ToArray();

        Assert.Throws<ModelFormatException>(() => NifHeader.Parse(bytes));
    }

    [Fact]
    public void Parse_TruncatedAfterHeaderLine_Throws()
    {
        byte[] bytes = "NetImmerse File Format, Version 4.2.2.0\n\x00\x02"u8.ToArray();

        Assert.Throws<ModelFormatException>(() => NifHeader.Parse(bytes));
    }
}
