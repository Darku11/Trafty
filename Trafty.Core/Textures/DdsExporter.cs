using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Trafty.Core.Textures;

/// <summary>
/// Renders a <see cref="DdsFile"/> to a viewable PNG — used for thumbnail previews (Modul A's
/// asset grid) so the UI doesn't need to know about DXT decoding at all. Kept separate from
/// the parser so Trafty.Core's parsing/decoding logic doesn't require an image library to
/// compile and test, matching the SystemColExporter/PcxExporter pattern.
/// </summary>
public static class DdsExporter
{
    /// <summary>
    /// Renders to PNG. <paramref name="forceOpaque"/> defaults to true because real retail
    /// terrain DXT3 patches were found to carry alpha=0 across the entire texture (verified
    /// against ter002.mpk) — the terrain renderer evidently ignores alpha entirely, so a
    /// literal alpha=0 export would show as fully transparent (blank) in a thumbnail despite
    /// having real, visible RGB content. Pass false only when the true stored alpha matters
    /// for the caller's purpose.
    /// </summary>
    public static void SaveAsPng(DdsFile file, Stream destination, bool forceOpaque = true)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(destination);

        using Image<Rgba32> image = Render(file, forceOpaque);
        image.SaveAsPng(destination);
    }

    private static Image<Rgba32> Render(DdsFile file, bool forceOpaque)
    {
        var image = new Image<Rgba32>(file.Width, file.Height);

        for (int y = 0; y < file.Height; y++)
        {
            for (int x = 0; x < file.Width; x++)
            {
                int offset = (y * file.Width + x) * 4;
                byte alpha = forceOpaque ? (byte)255 : file.Rgba[offset + 3];
                image[x, y] = new Rgba32(file.Rgba[offset], file.Rgba[offset + 1], file.Rgba[offset + 2], alpha);
            }
        }

        return image;
    }
}
