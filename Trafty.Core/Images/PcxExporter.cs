using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Trafty.Core.Images;

/// <summary>
/// Renders a <see cref="PcxFile"/> to a viewable PNG. Kept separate from the parser so that
/// Trafty.Core's parsing logic doesn't require an image library to compile and test — only
/// this export path does.
/// </summary>
public static class PcxExporter
{
    public static void SaveAsPng(PcxFile file, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        using Image<Rgba32> image = Render(file);
        image.SaveAsPng(outputPath);
    }

    /// <summary>Encodes the raster as PNG bytes into a stream, for in-memory use (e.g. UI previews).</summary>
    public static void SaveAsPng(PcxFile file, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(destination);

        using Image<Rgba32> image = Render(file);
        image.SaveAsPng(destination);
    }

    private static Image<Rgba32> Render(PcxFile file)
    {
        var image = new Image<Rgba32>(file.Width, file.Height);

        for (int y = 0; y < file.Height; y++)
        {
            for (int x = 0; x < file.Width; x++)
            {
                int offset = (y * file.Width + x) * 4;
                image[x, y] = new Rgba32(
                    file.Rgba[offset],
                    file.Rgba[offset + 1],
                    file.Rgba[offset + 2],
                    file.Rgba[offset + 3]);
            }
        }

        return image;
    }
}
