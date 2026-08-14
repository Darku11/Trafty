using System.IO.Compression;

namespace Trafty.Core.Compression;

/// <summary>
/// Thin wrapper around the framework zlib implementation. MPAK stores every payload as a
/// raw zlib stream (RFC 1950), which maps directly onto <see cref="ZLibStream"/>.
/// </summary>
public static class ZlibCodec
{
    /// <summary>
    /// Inflates <paramref name="compressed"/> into a buffer of exactly
    /// <paramref name="expectedSize"/> bytes.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The stream is malformed or does not expand to the announced size.
    /// </exception>
    public static byte[] Decompress(ReadOnlySpan<byte> compressed, int expectedSize)
    {
        if (expectedSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedSize));
        }

        byte[] output = new byte[expectedSize];

        using var input = new MemoryStream(compressed.ToArray(), writable: false);
        using var inflater = new ZLibStream(input, CompressionMode.Decompress);

        int total = 0;

        while (total < expectedSize)
        {
            int read = inflater.Read(output, total, expectedSize - total);

            if (read == 0)
            {
                throw new InvalidDataException(
                    $"Compressed payload expanded to {total} bytes, but {expectedSize} bytes were announced.");
            }

            total += read;
        }

        // A single trailing read must hit the end of the stream. Anything else means the
        // entry announces a size smaller than its actual content, which would silently
        // truncate assets on extraction.
        if (inflater.ReadByte() != -1)
        {
            throw new InvalidDataException(
                $"Compressed payload is larger than the announced size of {expectedSize} bytes.");
        }

        return output;
    }

    /// <summary>
    /// Inflates a zlib stream whose expanded size is not known in advance.
    /// </summary>
    public static byte[] Decompress(ReadOnlySpan<byte> compressed)
    {
        using var input = new MemoryStream(compressed.ToArray(), writable: false);
        using var inflater = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        inflater.CopyTo(output);

        return output.ToArray();
    }

    /// <summary>
    /// Deflates <paramref name="data"/> into a zlib stream.
    /// </summary>
    public static byte[] Compress(ReadOnlySpan<byte> data, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var output = new MemoryStream();

        using (var deflater = new ZLibStream(output, level, leaveOpen: true))
        {
            deflater.Write(data);
        }

        return output.ToArray();
    }
}
