using Trafty.Core.Audio;
using Xunit;

namespace Trafty.Core.Tests;

/// <summary>
/// The synthetic sample_*.wav fixtures below predate real DAoC audio being available.
/// agramon_die.wav/adrghit.wav/adrghit3.wav are real sound effects pulled from the
/// client's sounds/ folder (a monster death cry and two hit sounds) — the first real
/// confirmation that RIFF/WAVE, being a public stable format, was implemented correctly
/// without needing any reverse engineering.
/// </summary>
public sealed class WavHeaderTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, name);

    [Fact]
    public void Parse_StereoSample_MatchesKnownFields()
    {
        WavHeader header = WavHeader.Load(Fixture("sample_stereo_22050.wav"));

        Assert.Equal(1, header.AudioFormat); // PCM
        Assert.Equal("PCM", header.AudioFormatDisplay);
        Assert.Equal(2, header.ChannelCount);
        Assert.Equal(22050u, header.SampleRate);
        Assert.Equal(16, header.BitsPerSample);
        Assert.Equal(88200u, header.DataSize);
        Assert.Equal(TimeSpan.FromSeconds(1), header.Duration);
    }

    [Fact]
    public void Parse_MonoSample_MatchesKnownFields()
    {
        WavHeader header = WavHeader.Load(Fixture("sample_mono_11025.wav"));

        Assert.Equal(1, header.ChannelCount);
        Assert.Equal(11025u, header.SampleRate);
        Assert.Equal(8, header.BitsPerSample);
        Assert.Equal(TimeSpan.FromSeconds(5512.0 / 11025.0), header.Duration);
    }

    [Theory]
    [InlineData("agramon_die.wav", 195316u)]
    [InlineData("adrghit.wav", 63646u)]
    [InlineData("adrghit3.wav", 45910u)]
    public void Parse_RealClientSoundEffects_MatchKnownFields(string fileName, uint expectedDataSize)
    {
        WavHeader header = WavHeader.Load(Fixture(fileName));

        Assert.Equal(1, header.AudioFormat); // PCM
        Assert.Equal(1, header.ChannelCount); // mono, like all three real samples
        Assert.Equal(22050u, header.SampleRate);
        Assert.Equal(16, header.BitsPerSample);
        Assert.Equal(44100u, header.ByteRate); // SampleRate * BlockAlign, consistent with 16-bit mono
        Assert.Equal(2, header.BlockAlign);
        Assert.Equal(expectedDataSize, header.DataSize);

        // File size sanity check: 8 (RIFF header) + 4 ("WAVE") + 8+16 ("fmt " chunk) +
        // 8 ("data" chunk header) + DataSize should equal the actual file length exactly
        // (all three files have even-length data, so no padding byte to account for).
        long expectedFileLength = 8 + 4 + 8 + 16 + 8 + expectedDataSize;
        Assert.Equal(expectedFileLength, new FileInfo(Fixture(fileName)).Length);
    }

    [Fact]
    public void Parse_MissingRiffMarker_Throws()
    {
        byte[] bytes = "not a wav file at all, just some bytes"u8.ToArray();

        Assert.Throws<AudioFormatException>(() => WavHeader.Parse(bytes));
    }

    [Fact]
    public void Parse_RiffButNotWave_Throws()
    {
        byte[] bytes = File.ReadAllBytes(Fixture("sample_stereo_22050.wav"));
        bytes[8] = (byte)'X'; // corrupt "WAVE" -> "XAVE"

        Assert.Throws<AudioFormatException>(() => WavHeader.Parse(bytes));
    }

    [Fact]
    public void Parse_TruncatedBeforeFmtChunk_Throws()
    {
        byte[] bytes = File.ReadAllBytes(Fixture("sample_stereo_22050.wav"))[..20];

        Assert.Throws<AudioFormatException>(() => WavHeader.Parse(bytes));
    }
}
