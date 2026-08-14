using System.Buffers.Binary;
using System.Text;

namespace Trafty.Core.Audio;

/// <summary>
/// Reads the header of a .wav file — the standard RIFF/WAVE container. Like .nif, this is
/// a publicly documented format (Microsoft/IBM RIFF spec), not something reverse
/// engineered from client data, so this parser follows the published layout directly.
///
/// Layout (little-endian throughout, as RIFF/WAVE always is):
///   0x00  char[4]   "RIFF"
///   0x04  uint32    file size - 8
///   0x08  char[4]   "WAVE"
///   ...   chunks: each is char[4] id + uint32 size + payload (padded to even length)
///                  The "fmt " chunk carries the audio format; "data" carries samples.
/// </summary>
public sealed class WavHeader
{
    public required ushort AudioFormat { get; init; } // 1 = PCM, 3 = IEEE float, etc.
    public required ushort ChannelCount { get; init; }
    public required uint SampleRate { get; init; }
    public required uint ByteRate { get; init; }
    public required ushort BlockAlign { get; init; }
    public required ushort BitsPerSample { get; init; }

    /// <summary>Size of the audio sample data, in bytes.</summary>
    public required uint DataSize { get; init; }

    /// <summary>Byte offset of the sample data within the file.</summary>
    public required int DataOffset { get; init; }

    public TimeSpan Duration => ByteRate == 0
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds((double)DataSize / ByteRate);

    public string AudioFormatDisplay => AudioFormat switch
    {
        1 => "PCM",
        3 => "IEEE float",
        6 => "A-law",
        7 => "mu-law",
        _ => $"format 0x{AudioFormat:X4}",
    };

    public static WavHeader Parse(ReadOnlySpan<byte> data)
    {
        const int RiffHeaderSize = 12; // "RIFF" + size + "WAVE"

        if (data.Length < RiffHeaderSize ||
            !data[..4].SequenceEqual("RIFF"u8) ||
            !data.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new AudioFormatException("Not a RIFF/WAVE file (missing \"RIFF\"/\"WAVE\" markers).");
        }

        int offset = RiffHeaderSize;

        ushort? audioFormat = null;
        ushort? channelCount = null;
        uint? sampleRate = null;
        uint? byteRate = null;
        ushort? blockAlign = null;
        ushort? bitsPerSample = null;
        uint? dataSize = null;
        int dataOffset = -1;

        while (offset + 8 <= data.Length)
        {
            ReadOnlySpan<byte> chunkId = data.Slice(offset, 4);
            uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
            int payloadOffset = offset + 8;

            if (payloadOffset + chunkSize > data.Length)
            {
                // A truncated chunk at the very end (common if a fixture only carries the
                // header) is not fatal for chunks we don't need to read into.
                if (!chunkId.SequenceEqual("fmt "u8))
                {
                    break;
                }
            }

            if (chunkId.SequenceEqual("fmt "u8))
            {
                if (chunkSize < 16 || payloadOffset + 16 > data.Length)
                {
                    throw new AudioFormatException("\"fmt \" chunk is smaller than the required 16 bytes.");
                }

                ReadOnlySpan<byte> fmt = data.Slice(payloadOffset, 16);
                audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                channelCount = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
                sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]);
                byteRate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[8..]);
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(fmt[12..]);
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
            }
            else if (chunkId.SequenceEqual("data"u8))
            {
                dataSize = chunkSize;
                dataOffset = payloadOffset;
            }

            // RIFF chunks are padded to an even byte boundary.
            int advance = (int)chunkSize + (chunkSize % 2 == 1 ? 1 : 0);
            offset = payloadOffset + advance;
        }

        if (audioFormat is null)
        {
            throw new AudioFormatException("No \"fmt \" chunk found.");
        }

        return new WavHeader
        {
            AudioFormat = audioFormat.Value,
            ChannelCount = channelCount!.Value,
            SampleRate = sampleRate!.Value,
            ByteRate = byteRate!.Value,
            BlockAlign = blockAlign!.Value,
            BitsPerSample = bitsPerSample!.Value,
            DataSize = dataSize ?? 0,
            DataOffset = dataOffset,
        };
    }

    public static WavHeader Load(string path) => Parse(File.ReadAllBytes(path));
}
