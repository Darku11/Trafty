using System.Buffers.Binary;
using System.Text;

namespace Trafty.Core.Textures;

/// <summary>
/// Builds standalone .dds files. The layout below was verified against a real terrain
/// entry extracted from ter002.mpk (128x128, BC2/DXT3): 4-byte magic, a 124-byte
/// DDS_HEADER, and the compressed payload — nothing more.
///
/// The client stores every mip level as its own complete .dds file (patchNNNN-00.dds,
/// -01.dds, ...) rather than one file with an embedded mip chain, so this writer never
/// sets DDSD_MIPMAPCOUNT or DDSCAPS_MIPMAP/DDSCAPS_COMPLEX — the reference file has
/// neither, and reproducing that keeps generated files indistinguishable from retail ones.
/// </summary>
public static class DdsWriter
{
    private const int HeaderSize = 128; // 4 byte magic + 124 byte DDS_HEADER
    private const uint DdsdCaps = 0x1;
    private const uint DdsdHeight = 0x2;
    private const uint DdsdWidth = 0x4;
    private const uint DdsdPixelFormat = 0x1000;
    private const uint DdsdLinearSize = 0x80000;
    private const uint DdpfFourCc = 0x4;
    private const uint DdscapsTexture = 0x1000;

    /// <summary>
    /// Assembles a complete .dds file from an already block-compressed payload.
    /// </summary>
    public static byte[] Write(int width, int height, DxtFormat format, ReadOnlySpan<byte> compressedPixels)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Width and height must be positive.");
        }

        byte[] file = new byte[HeaderSize + compressedPixels.Length];
        Span<byte> span = file;

        Encoding.ASCII.GetBytes("DDS ").CopyTo(span);

        Span<byte> h = span[4..HeaderSize];
        h.Clear();

        uint flags = DdsdCaps | DdsdHeight | DdsdWidth | DdsdPixelFormat | DdsdLinearSize;

        BinaryPrimitives.WriteUInt32LittleEndian(h[0..], 124);          // dwSize
        BinaryPrimitives.WriteUInt32LittleEndian(h[4..], flags);        // dwFlags
        BinaryPrimitives.WriteUInt32LittleEndian(h[8..], (uint)height); // dwHeight
        BinaryPrimitives.WriteUInt32LittleEndian(h[12..], (uint)width); // dwWidth
        BinaryPrimitives.WriteUInt32LittleEndian(h[16..], (uint)compressedPixels.Length); // dwPitchOrLinearSize
        // dwDepth (20), dwMipMapCount (24), dwReserved1[11] (28..72) stay zero.

        // DDS_PIXELFORMAT at offset 72 within h (== file offset 76), 32 bytes.
        Span<byte> pf = h[72..104];
        BinaryPrimitives.WriteUInt32LittleEndian(pf[0..], 32);          // dwSize
        BinaryPrimitives.WriteUInt32LittleEndian(pf[4..], DdpfFourCc);  // dwFlags
        Encoding.ASCII.GetBytes(format.FourCc()).CopyTo(pf[8..12]);     // dwFourCC
        // dwRGBBitCount and the four bit masks (12..32) stay zero: irrelevant for FourCC formats.

        // DDS_CAPS2 at offset 104 within h (== file offset 108).
        BinaryPrimitives.WriteUInt32LittleEndian(h[104..], DdscapsTexture); // dwCaps1
        // dwCaps2/3/4 and dwReserved2 (108..124 within h) stay zero.

        compressedPixels.CopyTo(span[HeaderSize..]);

        return file;
    }
}
