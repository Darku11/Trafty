using System.Text;
using Trafty.Core.Archives;
using Trafty.Core.Client;
using Xunit;

namespace Trafty.Core.Tests;

public sealed class ClientAssetScannerTests
{
    [Fact]
    public void Scan_IndexesLooseFilesAndArchiveEntries()
    {
        string root = Path.Combine(Path.GetTempPath(), $"trafty-client-scan-{Guid.NewGuid()}");
        Directory.CreateDirectory(root);

        try
        {
            string looseModel = Path.Combine(root, "oak_tree.nif");
            File.WriteAllBytes(looseModel, new byte[] { 1, 2, 3 });

            string archivePath = Path.Combine(root, "objects.mpk");
            MpkArchiveWriter.Write(
                new[]
                {
                    new MpkPendingEntry
                    {
                        Name = "alb_house_01.nif",
                        UncompressedData = Encoding.ASCII.GetBytes("model"),
                    },
                    new MpkPendingEntry
                    {
                        Name = "alb_house_wall.dds",
                        UncompressedData = Encoding.ASCII.GetBytes("texture"),
                    },
                },
                "objects.mpk",
                archivePath);

            ClientAssetIndex index = ClientAssetScanner.Scan(root);

            Assert.Contains(index.Assets, a => !a.IsArchived && a.Name == "oak_tree.nif" && a.Kind == ClientAssetKind.Model);
            Assert.Contains(index.Assets, a => a.IsArchived && a.Name == "alb_house_01.nif" && a.Kind == ClientAssetKind.Model);
            Assert.Contains(index.Assets, a => a.IsArchived && a.Name == "alb_house_wall.dds" && a.Kind == ClientAssetKind.Texture);
            Assert.Equal(1, index.ArchiveCount);
            Assert.Empty(index.Failures);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_BrokenArchive_IsReportedButDoesNotAbortScan()
    {
        string root = Path.Combine(Path.GetTempPath(), $"trafty-client-broken-{Guid.NewGuid()}");
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "broken.mpk"), "not an archive");
            File.WriteAllText(Path.Combine(root, "notes.txt"), "still index this");

            ClientAssetIndex index = ClientAssetScanner.Scan(root);

            Assert.Contains(index.Assets, a => a.Name == "broken.mpk" && a.Kind == ClientAssetKind.Archive);
            Assert.Contains(index.Assets, a => a.Name == "notes.txt" && a.Kind == ClientAssetKind.TextData);
            Assert.Single(index.Failures);
            Assert.Equal("broken.mpk", index.Failures[0].Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("tree.nif", ClientAssetKind.Model)]
    [InlineData("tree.nhd", ClientAssetKind.WorldProp)]
    [InlineData("tree.dds", ClientAssetKind.Texture)]
    [InlineData("sound.wav", ClientAssetKind.Audio)]
    [InlineData("chat_window.xml", ClientAssetKind.Ui)]
    [InlineData("fixtures.csv", ClientAssetKind.ZoneData)]
    [InlineData("SYSTEM.COL", ClientAssetKind.ColorTable)]
    public void Classify_KnownExtensions_ReturnsExpectedKind(string name, ClientAssetKind expected)
    {
        Assert.Equal(expected, ClientAssetScanner.Classify(name));
    }
}
