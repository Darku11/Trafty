namespace Trafty.Core.Textures;

/// <summary>
/// One generated mip level, packaged as a standalone .dds file — matching how the retail
/// client stores each patchNNNN-MM.dds independently rather than as an embedded chain.
/// </summary>
/// <param name="Level">Zero-based mip index, 0 being the full-size base texture.</param>
/// <param name="Width">Width in pixels of this level.</param>
/// <param name="Height">Height in pixels of this level.</param>
/// <param name="DdsBytes">The complete, ready-to-write .dds file for this level.</param>
public sealed record EncodedMipLevel(int Level, int Width, int Height, byte[] DdsBytes);

/// <summary>
/// Drag-and-drop entry point for Modul A's Smart Replace Engine: turns a PNG/JPG/TGA
/// source image into the same file shape the retail packer produces — one .dds per mip
/// level, block compressed with BC1 or BC2 depending on whether the source has alpha.
/// </summary>
public static class DdsEncoder
{
    /// <summary>
    /// Encodes an image file into a full mip chain of .dds payloads.
    /// </summary>
    /// <param name="imagePath">Source PNG, JPG, TGA, or any format ImageSharp reads.</param>
    /// <param name="format">
    /// Compression to use. Pass <see langword="null"/> to auto-detect: BC2 (DXT3) if the
    /// source has any non-opaque pixel, BC1 (DXT1) otherwise.
    /// </param>
    public static IReadOnlyList<EncodedMipLevel> EncodeFile(string imagePath, DxtFormat? format = null)
    {
        IReadOnlyList<MipLevel> mips = MipChainBuilder.BuildFromFile(imagePath);

        if (mips.Count == 0)
        {
            throw new InvalidOperationException(
                $"\"{imagePath}\" is smaller than {MipChainBuilder.MinimumDimension}x{MipChainBuilder.MinimumDimension} " +
                "after rounding to a multiple of 4; no mip levels could be generated.");
        }

        DxtFormat resolvedFormat = format ?? (HasTransparency(mips[0]) ? DxtFormat.Bc2 : DxtFormat.Bc1);

        var results = new List<EncodedMipLevel>(mips.Count);

        for (int i = 0; i < mips.Count; i++)
        {
            MipLevel mip = mips[i];
            byte[] compressed = BlockCompressor.Compress(mip.Pixels, mip.Width, mip.Height, resolvedFormat);
            byte[] dds = DdsWriter.Write(mip.Width, mip.Height, resolvedFormat, compressed);

            results.Add(new EncodedMipLevel(i, mip.Width, mip.Height, dds));
        }

        return results;
    }

    /// <summary>
    /// Builds the file names the retail packer would use for a given base name, e.g.
    /// "patch0000" with 5 levels yields "patch0000-00.dds" .. "patch0000-04.dds".
    /// </summary>
    public static IReadOnlyList<string> NameMipLevels(string baseName, int levelCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);

        var names = new List<string>(levelCount);

        for (int i = 0; i < levelCount; i++)
        {
            names.Add($"{baseName}-{i:D2}.dds");
        }

        return names;
    }

    private static bool HasTransparency(MipLevel level)
    {
        byte[] pixels = level.Pixels;

        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 255)
            {
                return true;
            }
        }

        return false;
    }
}
