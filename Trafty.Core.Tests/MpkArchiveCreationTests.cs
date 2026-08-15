using System.Text;
using Trafty.Core.Archives;
using Xunit;

namespace Trafty.Core.Tests;

public sealed class MpkArchiveCreationTests
{
    [Fact]
    public void Write_EmptyArchive_CreatesReadableMpk()
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"mpk-create-empty-{Guid.NewGuid()}.mpk");
        string archiveName = Path.GetFileName(outputPath);

        try
        {
            MpkArchiveWriter.Write(Array.Empty<MpkPendingEntry>(), archiveName, outputPath);

            using MpkArchive archive = MpkArchive.Open(outputPath);

            Assert.Equal(archiveName, archive.ArchiveName);
            Assert.Empty(archive.Entries);
            Assert.Empty(archive.Verify());
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
    public void Write_EmptyArchive_CanBeExtendedWithWriteReplacing()
    {
        string sourcePath = Path.Combine(Path.GetTempPath(), $"mpk-create-source-{Guid.NewGuid()}.mpk");
        string resultPath = Path.Combine(Path.GetTempPath(), $"mpk-create-result-{Guid.NewGuid()}.mpk");
        byte[] content = Encoding.ASCII.GetBytes("new archive entry");

        try
        {
            MpkArchiveWriter.Write(Array.Empty<MpkPendingEntry>(), Path.GetFileName(sourcePath), sourcePath);

            using (MpkArchive source = MpkArchive.Open(sourcePath))
            {
                MpkArchiveWriter.WriteReplacing(
                    source,
                    new[] { new MpkPendingEntry { Name = "entry.txt", UncompressedData = content } },
                    resultPath);
            }

            using MpkArchive result = MpkArchive.Open(resultPath);
            MpkEntry? entry = result["entry.txt"];

            Assert.NotNull(entry);
            Assert.Equal(content, result.Extract(entry!));
            Assert.Empty(result.Verify());
        }
        finally
        {
            if (File.Exists(sourcePath))
            {
                File.Delete(sourcePath);
            }

            if (File.Exists(resultPath))
            {
                File.Delete(resultPath);
            }
        }
    }
}
