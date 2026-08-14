using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Trafty.Core.Weather;

/// <summary>
/// Renders a <see cref="SystemColFile"/> to a viewable PNG. Kept separate from the parser
/// so that Trafty.Core's parsing logic doesn't require an image library to compile and
/// test — only this export path does.
/// </summary>
public static class SystemColExporter
{
    public static void SaveAsPng(SystemColFile file, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        using Image<Rgb24> image = Render(file);
        image.SaveAsPng(outputPath);
    }

    /// <summary>Encodes the raster as PNG bytes into a stream, for in-memory use (e.g. UI previews).</summary>
    public static void SaveAsPng(SystemColFile file, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(destination);

        using Image<Rgb24> image = Render(file);
        image.SaveAsPng(destination);
    }

    private static Image<Rgb24> Render(SystemColFile file)
    {
        var image = new Image<Rgb24>(SystemColFile.Width, SystemColFile.Height);

        for (int y = 0; y < SystemColFile.Height; y++)
        {
            for (int x = 0; x < SystemColFile.Width; x++)
            {
                (byte r, byte g, byte b) = file.GetPixel(x, y);
                image[x, y] = new Rgb24(r, g, b);
            }
        }

        return image;
    }
}
