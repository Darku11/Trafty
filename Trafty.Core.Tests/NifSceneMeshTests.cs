using System.Linq;
using Trafty.Core.Archives;
using Trafty.Core.Models.Nif;
using Xunit;

namespace Trafty.Core.Tests;

public sealed class NifSceneMeshTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, name);

    private static byte[] LoadRealNifBytes()
    {
        using MpkArchive archive = MpkArchive.Open(Fixture("1struinedtemple.npk"));
        MpkEntry entry = archive.Entries.Single(e => e.Name.EndsWith(".NIF", StringComparison.OrdinalIgnoreCase));
        return archive.Extract(entry);
    }

    [Fact]
    public void Build_RealFile_ProducesOneTrianglePerSourceTriangle()
    {
        NifDocument doc = NifDocument.Parse(LoadRealNifBytes());
        var mesh = NifSceneMesh.Build(doc);

        int expectedTriangleCount = doc.Blocks.OfType<NiTriShapeDataBlock>().Sum(b => b.Triangles.Count);
        Assert.Equal(expectedTriangleCount, mesh.Count);
    }

    [Fact]
    public void Build_RealFile_TransformsVerticesAwayFromLocalOrigin()
    {
        // A building-sized mesh made of many child NiTriShapes should not collapse onto (0,0,0)
        // after scene-graph transform composition — a sign the transform math is being applied
        // rather than silently skipped.
        NifDocument doc = NifDocument.Parse(LoadRealNifBytes());
        var mesh = NifSceneMesh.Build(doc);

        Assert.True(mesh.Count > 0);
        Assert.Contains(mesh, t => Distance(t.A, (0, 0, 0)) > 1f);
    }

    [Fact]
    public void Render_RealMesh_ProducesNonEmptyImage()
    {
        NifDocument doc = NifDocument.Parse(LoadRealNifBytes());
        var mesh = NifSceneMesh.Build(doc);

        using var stream = new MemoryStream();
        NifMeshPreviewRenderer.SaveAsPng(mesh, 128, 128, stream);

        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void Render_DifferentRotations_ProduceDifferentImages()
    {
        // Supports the App's drag-to-rotate preview: rotation is a caller-supplied
        // parameter now, not a fixed constant, and actually changes the output.
        NifDocument doc = NifDocument.Parse(LoadRealNifBytes());
        var mesh = NifSceneMesh.Build(doc);

        using var streamA = new MemoryStream();
        NifMeshPreviewRenderer.SaveAsPng(mesh, 128, 128, streamA, rotationYDegrees: 0, rotationXDegrees: 0);

        using var streamB = new MemoryStream();
        NifMeshPreviewRenderer.SaveAsPng(mesh, 128, 128, streamB, rotationYDegrees: 90, rotationXDegrees: 45);

        Assert.NotEqual(streamA.ToArray(), streamB.ToArray());
    }

    [Fact]
    public void Render_EmptyMesh_ProducesBlankImageWithoutThrowing()
    {
        using var stream = new MemoryStream();
        NifMeshPreviewRenderer.SaveAsPng(Array.Empty<NifWorldTriangle>(), 64, 64, stream);

        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void Compose_IdentityWithIdentity_StaysIdentity()
    {
        NifTransform result = NifTransform.Identity.Compose(NifTransform.Identity);

        Assert.Equal((0f, 0f, 0f), result.Translation);
        Assert.Equal(1f, result.Scale);
    }

    [Fact]
    public void Compose_ParentTranslation_OffsetsChildPoint()
    {
        var parent = new NifTransform
        {
            Translation = (10, 0, 0),
            Rotation = [1, 0, 0, 0, 1, 0, 0, 0, 1],
            Scale = 1f,
        };

        var child = new NifTransform
        {
            Translation = (0, 5, 0),
            Rotation = [1, 0, 0, 0, 1, 0, 0, 0, 1],
            Scale = 1f,
        };

        NifTransform world = parent.Compose(child);
        (float X, float Y, float Z) point = world.TransformPoint((0, 0, 0));

        Assert.Equal(10f, point.X, 3);
        Assert.Equal(5f, point.Y, 3);
        Assert.Equal(0f, point.Z, 3);
    }

    private static float Distance((float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
        MathF.Sqrt(MathF.Pow(a.X - b.X, 2) + MathF.Pow(a.Y - b.Y, 2) + MathF.Pow(a.Z - b.Z, 2));
}
