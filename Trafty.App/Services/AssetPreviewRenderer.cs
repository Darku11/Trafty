using Trafty.Core.Images;
using Trafty.Core.Models;
using Trafty.Core.Models.Nif;
using Trafty.Core.Textures;

namespace Trafty.App.Services;

/// <summary>
/// Renders a PNG thumbnail for a texture or model, given its raw bytes and file extension.
/// Shared between the hover/right-click preview popups and the existing selection-driven
/// preview panels, so both stay in sync with what formats are actually supported.
/// </summary>
public static class AssetPreviewRenderer
{
    public const int DefaultSize = 320;

    /// <summary>True for extensions this renderer knows how to turn into a PNG preview.</summary>
    public static bool IsPreviewable(string extension) => extension.TrimStart('.').ToLowerInvariant() switch
    {
        "dds" or "tga" or "nif" => true,
        _ => false,
    };

    /// <summary>
    /// Returns PNG bytes for the given asset, or null if the format isn't previewable or
    /// the file/bytes turned out to be unreadable. Never throws — callers show nothing
    /// rather than an error dialog when a hover preview can't be built.
    /// </summary>
    public static byte[]? TryRenderPng(byte[] bytes, string extension, int size = DefaultSize)
    {
        try
        {
            switch (extension.TrimStart('.').ToLowerInvariant())
            {
                case "dds":
                {
                    DdsFile dds = DdsFile.Parse(bytes);
                    using var png = new MemoryStream();
                    DdsExporter.SaveAsPng(dds, png, forceOpaque: false);
                    return png.ToArray();
                }
                case "tga":
                {
                    TgaFile tga = TgaFile.Parse(bytes);
                    using var png = new MemoryStream();
                    TgaExporter.SaveAsPng(tga, png);
                    return png.ToArray();
                }
                case "nif":
                {
                    NifDocument document = NifDocument.Parse(bytes);
                    IReadOnlyList<NifWorldTriangle> mesh = NifSceneMesh.Build(document);

                    if (mesh.Count == 0)
                    {
                        return null;
                    }

                    using var png = new MemoryStream();
                    NifMeshPreviewRenderer.SaveAsPng(mesh, size, size, png);
                    return png.ToArray();
                }
                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is IOException or TgaFormatException or TextureFormatException or ModelFormatException)
        {
            return null;
        }
    }
}
