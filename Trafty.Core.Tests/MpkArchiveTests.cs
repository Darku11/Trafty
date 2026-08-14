using Trafty.Core.Archives;
using Xunit;

namespace Trafty.Core.Tests;

/// <summary>
/// These tests run against a real client archive (ter002.mpk) rather than a synthetic
/// one, because the format was reverse engineered from that exact file. Any parser
/// regression that silently changes an offset calculation will show up here first.
/// </summary>
public sealed class MpkArchiveTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "ter002.mpk");

    [Fact]
    public void Open_ReadsHeaderFields()
    {
        using MpkArchive archive = MpkArchive.Open(FixturePath);

        Assert.Equal("ter002.mpk", archive.ArchiveName);
        Assert.Equal(MpkHeader.KnownVersion, archive.Header.Version);
        Assert.Equal(143u, archive.Header.FileCount);
        Assert.Equal(0x9B0FAF73u, archive.Header.DirectoryCrc32);
        Assert.Equal(17533u, archive.Header.DirectoryCompressedSize);
        Assert.Equal(18u, archive.Header.NameCompressedSize);
        Assert.Equal(143, archive.Entries.Count);
    }

    [Fact]
    public void FirstEntry_MatchesKnownValues()
    {
        using MpkArchive archive = MpkArchive.Open(FixturePath);

        MpkEntry first = archive.Entries[0];

        Assert.Equal("patch0000-00.dds", first.Name);
        Assert.Equal(0u, first.UncompressedOffset);
        Assert.Equal(16512u, first.UncompressedSize);
        Assert.Equal(0u, first.CompressedOffset);
        Assert.Equal(1832u, first.CompressedSize);
        Assert.Equal(0xC1C8B10Cu, first.Crc32);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1179409197), first.Timestamp);
    }

    [Fact]
    public void Indexer_LooksUpEntryByName()
    {
        using MpkArchive archive = MpkArchive.Open(FixturePath);

        MpkEntry? entry = archive["textures.csv"];

        Assert.NotNull(entry);
        Assert.Equal("textures.csv", entry!.Name);
    }

    [Fact]
    public void Indexer_IsCaseInsensitive()
    {
        using MpkArchive archive = MpkArchive.Open(FixturePath);

        Assert.NotNull(archive["TEXTURES.CSV"]);
    }

    [Fact]
    public void Extract_EveryEntry_MatchesDeclaredSizeAndChecksum()
    {
        using MpkArchive archive = MpkArchive.Open(FixturePath);

        foreach (MpkEntry entry in archive.Entries)
        {
            byte[] payload = archive.Extract(entry);

            Assert.Equal((int)entry.UncompressedSize, payload.Length);
        }
    }

    [Fact]
    public void Extract_ByName_ReturnsSameBytesAsExtract_ByEntry()
    {
        using MpkArchive archive = MpkArchive.Open(FixturePath);

        MpkEntry entry = archive.Entries[0];

        byte[] byEntry = archive.Extract(entry);
        byte[] byName = archive.Extract(entry.Name);

        Assert.Equal(byEntry, byName);
    }

    [Fact]
    public void Extract_UnknownName_Throws()
    {
        using MpkArchive archive = MpkArchive.Open(FixturePath);

        Assert.Throws<FileNotFoundException>(() => archive.Extract("does-not-exist.dds"));
    }

    [Fact]
    public void Extract_CorruptedPayload_ThrowsFormatException()
    {
        byte[] bytes = File.ReadAllBytes(FixturePath);

        using var archive = MpkArchive.Open(new MemoryStream(bytes), leaveOpen: true);
        MpkEntry entry = archive.Entries[0];

        // Flip a byte inside the first entry's compressed payload; the stored CRC-32
        // covers the compressed bytes, so this must be caught before decompression.
        long dataOffset = archive.Header.DataOffset + entry.CompressedOffset;
        bytes[dataOffset] ^= 0xFF;

        using var corrupted = MpkArchive.Open(new MemoryStream(bytes), leaveOpen: true);

        Assert.Throws<MpkFormatException>(() => corrupted.Extract(corrupted.Entries[0]));
    }

    [Fact]
    public void Verify_OnIntactArchive_ReturnsNoProblems()
    {
        using MpkArchive archive = MpkArchive.Open(FixturePath);

        Assert.Empty(archive.Verify());
    }

    [Fact]
    public void DirectoryEntry_RoundTrips_ForNamesLongerThanTwelveCharacters()
    {
        using MpkArchive archive = MpkArchive.Open(FixturePath);

        // patch0000-00.dds is 17 characters, which overruns the 12 usable characters of
        // the name field and truncates the front of the stored source path. Confirm the
        // parser surfaces that as documented rather than as silent corruption.
        MpkEntry entry = archive.Entries[0];

        Assert.Equal("patch0000-00.dds", entry.Name);
        Assert.EndsWith("patch0000-00.dds", entry.SourcePath);
        Assert.DoesNotContain("camelot", entry.SourcePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteTo_UnmodifiedEntry_ReproducesOriginalRecordBytes()
    {
        byte[] fileBytes = File.ReadAllBytes(FixturePath);
        using var archive = MpkArchive.Open(new MemoryStream(fileBytes), leaveOpen: true);

        // Re-derive the exact compressed directory bytes the archive was parsed from,
        // then compare a freshly serialised record against its slice of that buffer.
        var header = archive.Header;
        byte[] compressedDirectory = new byte[header.DirectoryCompressedSize];
        using (var raw = new MemoryStream(fileBytes))
        {
            raw.Position = header.DirectoryOffset;
            raw.ReadExactly(compressedDirectory, 0, compressedDirectory.Length);
        }

        byte[] directory = Trafty.Core.Compression.ZlibCodec.Decompress(
            compressedDirectory, (int)header.FileCount * MpkEntry.DirectoryRecordSize);

        for (int i = 0; i < archive.Entries.Count; i++)
        {
            Span<byte> rebuilt = stackalloc byte[MpkEntry.DirectoryRecordSize];
            archive.Entries[i].WriteTo(rebuilt);

            var original = directory.AsSpan(i * MpkEntry.DirectoryRecordSize, MpkEntry.DirectoryRecordSize);

            Assert.True(
                rebuilt.SequenceEqual(original),
                $"Record {i} ({archive.Entries[i].Name}) did not round-trip byte for byte.");
        }
    }

    [Fact]
    public void Open_TruncatedFile_ThrowsFormatException()
    {
        byte[] truncated = File.ReadAllBytes(FixturePath)[..10];

        Assert.Throws<MpkFormatException>(() => MpkArchive.Open(new MemoryStream(truncated)));
    }

    [Fact]
    public void Open_WrongMagic_ThrowsFormatException()
    {
        byte[] bytes = File.ReadAllBytes(FixturePath);
        bytes[0] = (byte)'X';

        Assert.Throws<MpkFormatException>(() => MpkArchive.Open(new MemoryStream(bytes)));
    }

    [Fact]
    public void ExtractAll_WritesEveryFileUnderTargetDirectory()
    {
        using MpkArchive archive = MpkArchive.Open(FixturePath);

        string target = Path.Combine(Path.GetTempPath(), "trafty-test-" + Guid.NewGuid());

        try
        {
            int written = archive.ExtractAll(target);

            Assert.Equal(archive.Entries.Count, written);
            Assert.Equal(archive.Entries.Count, Directory.GetFiles(target).Length);
        }
        finally
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
        }
    }
}
