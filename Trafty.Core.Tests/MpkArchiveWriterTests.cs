using System.Linq;
using System.Text;
using Trafty.Core.Archives;
using Xunit;

namespace Trafty.Core.Tests;

/// <summary>
/// Covers MpkArchiveWriter.WriteReplacing against the real ter002.mpk fixture — previously
/// untested directly (only exercised indirectly through the App's texture-replace flow).
/// Added alongside the App's generic "Add Files" feature, which relies on the same
/// "unknown names get appended, known names get overwritten" behavior documented on
/// WriteReplacing itself.
/// </summary>
public sealed class MpkArchiveWriterTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "ter002.mpk");

    [Fact]
    public void WriteReplacing_NewFileName_AppendsWithoutTouchingExistingEntries()
    {
        using MpkArchive source = MpkArchive.Open(FixturePath);
        int originalCount = source.Entries.Count;

        byte[] newContent = Encoding.ASCII.GetBytes("hello from a brand new entry");
        var pending = new MpkPendingEntry { Name = "readme.txt", UncompressedData = newContent };

        string outputPath = Path.Combine(Path.GetTempPath(), $"mpk-writer-add-{Guid.NewGuid()}.mpk");

        try
        {
            MpkArchiveWriter.WriteReplacing(source, new[] { pending }, outputPath);

            using MpkArchive result = MpkArchive.Open(outputPath);
            Assert.Equal(originalCount + 1, result.Entries.Count);

            MpkEntry? added = result["readme.txt"];
            Assert.NotNull(added);
            Assert.Equal(newContent, result.Extract(added!));

            // Every original entry must still be present and byte-identical.
            foreach (MpkEntry original in source.Entries)
            {
                MpkEntry? carried = result[original.Name];
                Assert.NotNull(carried);
                Assert.Equal(source.Extract(original, verifyChecksum: false), result.Extract(carried!));
            }
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public void WriteReplacing_ExistingFileName_OverwritesContentInPlace()
    {
        using MpkArchive source = MpkArchive.Open(FixturePath);
        int originalCount = source.Entries.Count;
        string targetName = source.Entries[0].Name;

        byte[] replacementContent = Encoding.ASCII.GetBytes("replaced content");
        var pending = new MpkPendingEntry { Name = targetName, UncompressedData = replacementContent };

        string outputPath = Path.Combine(Path.GetTempPath(), $"mpk-writer-replace-{Guid.NewGuid()}.mpk");

        try
        {
            MpkArchiveWriter.WriteReplacing(source, new[] { pending }, outputPath);

            using MpkArchive result = MpkArchive.Open(outputPath);

            // Same entry count — a name that already existed must overwrite, not append.
            Assert.Equal(originalCount, result.Entries.Count);

            MpkEntry? replaced = result[targetName];
            Assert.NotNull(replaced);
            Assert.Equal(replacementContent, result.Extract(replaced!));
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public void WriteReplacing_OutputArchiveVerifiesClean()
    {
        using MpkArchive source = MpkArchive.Open(FixturePath);
        var pending = new MpkPendingEntry { Name = "extra.bin", UncompressedData = new byte[] { 1, 2, 3, 4 } };

        string outputPath = Path.Combine(Path.GetTempPath(), $"mpk-writer-verify-{Guid.NewGuid()}.mpk");

        try
        {
            MpkArchiveWriter.WriteReplacing(source, new[] { pending }, outputPath);

            using MpkArchive result = MpkArchive.Open(outputPath);
            Assert.Empty(result.Verify());
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }
}
