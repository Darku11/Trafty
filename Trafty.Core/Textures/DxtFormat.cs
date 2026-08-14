namespace Trafty.Core.Textures;

/// <summary>
/// Block compression formats the DAoC client accepts. Values match the FourCC codes
/// written into the DDS pixel format block.
/// </summary>
public enum DxtFormat
{
    /// <summary>
    /// 4 bits per pixel, no alpha channel. Best for fully opaque textures.
    /// </summary>
    Bc1,

    /// <summary>
    /// 8 bits per pixel: a BC1-style RGB block plus an explicit, non-interpolated 4-bit
    /// alpha value per pixel. FourCC "DXT3". This is the format retail terrain archives
    /// use (verified against ter002.mpk); sharp alpha edges (foliage cutouts, decals)
    /// look cleaner here than with the interpolated alpha of DXT5.
    /// </summary>
    Bc2,
}

internal static class DxtFormatInfo
{
    public static string FourCc(this DxtFormat format) => format switch
    {
        DxtFormat.Bc1 => "DXT1",
        DxtFormat.Bc2 => "DXT3",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Bytes consumed by a single 4x4 pixel block.</summary>
    public static int BlockSize(this DxtFormat format) => format switch
    {
        DxtFormat.Bc1 => 8,
        DxtFormat.Bc2 => 16,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };
}
