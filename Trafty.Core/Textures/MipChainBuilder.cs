using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Trafty.Core.Textures;

/// <summary>
/// One level of a mip chain, ready for block compression.
/// </summary>
public sealed class MipLevel
{
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>Tightly packed RGBA32 pixels, row major, top to bottom.</summary>
    public required byte[] Pixels { get; init; }
}

/// <summary>
/// Builds a mip chain the way the retail packer's naming convention implies: each level
/// is half the size of the one before it, stored as an independent image.
///
/// Block compression needs dimensions that are multiples of 4, so the chain stops at the
/// first level that would drop below 4x4 — 2x2 and 1x1 mips exist in some retail archives
/// but were not present in ter002.mpk's dataset in a form this encoder could confirm, so
/// generating them is left out rather than guessed at.
/// </summary>
public static class MipChainBuilder
{
    /// <summary>Smallest edge length this builder will produce a level for.</summary>
    public const int MinimumDimension = 4;

    /// <summary>
    /// Loads an image from disk and builds its mip chain. The base level is resized up or
    /// down to the nearest multiple of 4 on each axis first, using the same filter as the
    /// mip downsampling, so the whole chain is generated consistently.
    /// </summary>
    public static IReadOnlyList<MipLevel> BuildFromFile(string imagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        using Image<Rgba32> image = Image.Load<Rgba32>(imagePath);

        return Build(image);
    }

    /// <summary>
    /// Builds a mip chain from an already loaded image. The image is not disposed.
    /// </summary>
    public static IReadOnlyList<MipLevel> Build(Image<Rgba32> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int baseWidth = RoundToMultipleOf4(source.Width);
        int baseHeight = RoundToMultipleOf4(source.Height);

        using Image<Rgba32> current = source.Clone(ctx => ctx.Resize(baseWidth, baseHeight));

        var levels = new List<MipLevel>();
        int width = baseWidth;
        int height = baseHeight;

        using Image<Rgba32> working = current.Clone();

        while (width >= MinimumDimension && height >= MinimumDimension)
        {
            using Image<Rgba32> level = working.Clone(ctx => ctx.Resize(width, height));

            levels.Add(new MipLevel
            {
                Width = width,
                Height = height,
                Pixels = ExtractRgba(level),
            });

            width /= 2;
            height /= 2;
        }

        return levels;
    }

    private static byte[] ExtractRgba(Image<Rgba32> image)
    {
        byte[] pixels = new byte[image.Width * image.Height * 4];

        image.CopyPixelDataTo(pixels);

        return pixels;
    }

    private static int RoundToMultipleOf4(int value)
    {
        int rounded = (int)(Math.Round(value / 4.0) * 4);

        return Math.Max(rounded, MinimumDimension);
    }
}
