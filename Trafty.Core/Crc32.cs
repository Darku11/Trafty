namespace Trafty.Core.Hashing;

/// <summary>
/// CRC-32 (IEEE 802.3, reflected polynomial 0xEDB88320) as used by zlib and by the
/// MPAK container format. Implemented locally so that Trafty.Core stays dependency free.
/// </summary>
public static class Crc32
{
    private const uint Polynomial = 0xEDB88320u;
    private const uint Seed = 0xFFFFFFFFu;

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            uint value = i;

            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0
                    ? (value >> 1) ^ Polynomial
                    : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }

    /// <summary>
    /// Computes the CRC-32 checksum of the given buffer.
    /// </summary>
    public static uint Compute(ReadOnlySpan<byte> data) => Finish(Update(Seed, data));

    /// <summary>
    /// Feeds another chunk into a running checksum. Start with <see cref="Seed"/> and
    /// pass the result to <see cref="Finish"/> once all chunks have been processed.
    /// This allows hashing large payloads without buffering them completely.
    /// </summary>
    public static uint Update(uint runningValue, ReadOnlySpan<byte> data)
    {
        uint crc = runningValue;

        foreach (byte b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    /// <summary>
    /// Returns the initial value for an incremental computation via <see cref="Update"/>.
    /// </summary>
    public static uint Begin() => Seed;

    /// <summary>
    /// Converts a running value produced by <see cref="Update"/> into the final checksum.
    /// </summary>
    public static uint Finish(uint runningValue) => runningValue ^ Seed;

    /// <summary>
    /// Computes the CRC-32 checksum of a stream, starting at its current position and
    /// reading <paramref name="length"/> bytes.
    /// </summary>
    public static uint Compute(Stream stream, long length)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] buffer = new byte[81920];
        uint crc = Begin();
        long remaining = length;

        while (remaining > 0)
        {
            int wanted = (int)Math.Min(buffer.Length, remaining);
            int read = stream.Read(buffer, 0, wanted);

            if (read <= 0)
            {
                throw new EndOfStreamException("Unexpected end of stream while computing a checksum.");
            }

            crc = Update(crc, buffer.AsSpan(0, read));
            remaining -= read;
        }

        return Finish(crc);
    }
}
