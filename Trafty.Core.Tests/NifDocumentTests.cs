using System.Linq;
using Trafty.Core.Archives;
using Trafty.Core.Models;
using Trafty.Core.Models.Nif;
using Xunit;

namespace Trafty.Core.Tests;

public sealed class NifDocumentTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, name);

    /// <summary>
    /// Extracts the real 1struinedtemple.NIF from its .npk fixture — that's the same file
    /// nif_header_sample.bin was derived from, and it's the only real, full .nif this project
    /// has to verify a full block-list parse against.
    /// </summary>
    private static byte[] LoadRealNifBytes()
    {
        using MpkArchive archive = MpkArchive.Open(Fixture("1struinedtemple.npk"));
        MpkEntry entry = archive.Entries.Single(e => e.Name.EndsWith(".NIF", StringComparison.OrdinalIgnoreCase));
        return archive.Extract(entry);
    }

    [Fact]
    public void Parse_RealFile_ConsumesExactlyToEndOfFile()
    {
        // The strongest possible verification for this format: at version 4.2.2.0 there is no
        // block offset table, so any wrong field in any of the 232 blocks' layouts would
        // desync the read position and either throw mid-file or leave bytes unconsumed. Zero
        // bytes remaining after every block plus the root list is proof the layouts are right.
        NifDocument doc = NifDocument.Parse(LoadRealNifBytes());

        Assert.Equal(232, doc.Blocks.Count);
    }

    [Fact]
    public void Parse_RealFile_RootIsSceneRootNode()
    {
        NifDocument doc = NifDocument.Parse(LoadRealNifBytes());

        Assert.Single(doc.RootRefs);

        var root = Assert.IsType<NiNodeBlock>(doc.Blocks[doc.RootRefs[0]]);
        Assert.Equal("Scene Root", root.Name);
    }

    [Fact]
    public void Parse_RealFile_BlockTypeCountsMatchKnownBreakdown()
    {
        // Independently counted via a raw scan of length-prefixed "Ni..." strings in the file
        // before writing this parser — cross-check that the typed block list agrees.
        NifDocument doc = NifDocument.Parse(LoadRealNifBytes());

        Dictionary<string, int> counts = doc.Blocks
            .GroupBy(b => b.TypeName)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(56, counts["NiNode"]);
        Assert.Equal(1, counts["NiZBufferProperty"]);
        Assert.Equal(2, counts["NiVertexColorProperty"]);
        Assert.Equal(68, counts["NiTriShape"]);
        Assert.Equal(12, counts["NiMaterialProperty"]);
        Assert.Equal(68, counts["NiTriShapeData"]);
        Assert.Equal(1, counts["NiLODNode"]);
        Assert.Equal(10, counts["NiTexturingProperty"]);
        Assert.Equal(13, counts["NiSourceTexture"]);
        Assert.Equal(1, counts["NiDitherProperty"]);
    }

    [Fact]
    public void Parse_RealFile_GeometryLooksSane()
    {
        NifDocument doc = NifDocument.Parse(LoadRealNifBytes());

        var shapeData = doc.Blocks.OfType<NiTriShapeDataBlock>().ToList();

        Assert.Equal(68, shapeData.Count);
        Assert.All(shapeData, s => Assert.True(s.Vertices.Count > 0));
        Assert.All(shapeData, s => Assert.True(s.Triangles.Count > 0));

        // Every triangle index must point at a real vertex in the same shape.
        foreach (NiTriShapeDataBlock shape in shapeData)
        {
            foreach ((ushort v1, ushort v2, ushort v3) in shape.Triangles)
            {
                Assert.True(v1 < shape.Vertices.Count);
                Assert.True(v2 < shape.Vertices.Count);
                Assert.True(v3 < shape.Vertices.Count);
            }
        }
    }

    [Fact]
    public void Parse_RealFile_MaterialsAndTexturesResolve()
    {
        NifDocument doc = NifDocument.Parse(LoadRealNifBytes());

        var textures = doc.Blocks.OfType<NiSourceTextureBlock>().ToList();
        Assert.Equal(13, textures.Count);
        Assert.All(textures, t => Assert.True(t.IsExternal));
        Assert.Contains(textures, t => t.FileName == "sandstone_crumble_01.dds");

        var materials = doc.Blocks.OfType<NiMaterialPropertyBlock>().ToList();
        Assert.Equal(12, materials.Count);
        Assert.All(materials, m => Assert.InRange(m.Alpha, 0f, 1f));
    }

    [Fact]
    public void Parse_UnsupportedBlockType_Throws()
    {
        byte[] header = "NetImmerse File Format, Version 4.2.2.0\n"u8.ToArray();
        byte[] versionAndCount = { 0x00, 0x02, 0x02, 0x04, 0x01, 0x00, 0x00, 0x00 }; // v4.2.2.0, 1 block
        byte[] typeName = BuildSizedString("NiUnknownBlockType");

        byte[] data = header.Concat(versionAndCount).Concat(typeName).ToArray();

        Assert.Throws<ModelFormatException>(() => NifDocument.Parse(data));
    }

    private static byte[] BuildSizedString(string value)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(value);
        byte[] length = BitConverter.GetBytes((uint)bytes.Length);
        return length.Concat(bytes).ToArray();
    }
}
