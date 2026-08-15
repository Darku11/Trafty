using Trafty.Core.Archives;

namespace Trafty.Core.Client;

public static class ClientAssetScanner
{
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mpk", ".epk", ".npk",
    };

    private static readonly EnumerationOptions RecursiveEnumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
    };

    public static ClientAssetIndex Scan(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        string fullRootPath = Path.GetFullPath(rootPath);

        if (!Directory.Exists(fullRootPath))
        {
            throw new DirectoryNotFoundException($"Client folder not found: {fullRootPath}");
        }

        var assets = new List<ClientAssetRecord>();
        var failures = new List<ClientScanFailure>();
        int archiveCount = 0;

        foreach (string path in Directory.EnumerateFiles(fullRootPath, "*", RecursiveEnumeration))
        {
            string relativePath = Path.GetRelativePath(fullRootPath, path);
            string extension = Path.GetExtension(path);
            long size;

            try
            {
                size = new FileInfo(path).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new ClientScanFailure { Path = relativePath, Message = ex.Message });
                continue;
            }

            ClientAssetKind kind = Classify(Path.GetFileName(path));
            assets.Add(new ClientAssetRecord
            {
                Name = Path.GetFileName(path),
                Kind = kind,
                PhysicalPath = path,
                RelativeLocation = relativePath,
                Size = size,
            });

            if (!ArchiveExtensions.Contains(extension))
            {
                continue;
            }

            archiveCount++;

            try
            {
                using MpkArchive archive = MpkArchive.Open(path);

                foreach (MpkEntry entry in archive.Entries)
                {
                    assets.Add(new ClientAssetRecord
                    {
                        Name = entry.Name,
                        Kind = Classify(entry.Name),
                        PhysicalPath = path,
                        RelativeLocation = $"{relativePath} :: {entry.Name}",
                        Size = entry.UncompressedSize,
                        ArchiveEntryName = entry.Name,
                    });
                }
            }
            catch (Exception ex) when (ex is MpkFormatException or IOException or UnauthorizedAccessException)
            {
                failures.Add(new ClientScanFailure { Path = relativePath, Message = ex.Message });
            }
        }

        assets.Sort(static (left, right) =>
        {
            int kind = left.Kind.CompareTo(right.Kind);
            return kind != 0
                ? kind
                : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        });

        return new ClientAssetIndex(fullRootPath, assets, failures, archiveCount);
    }

    public static ClientAssetKind Classify(string name)
    {
        string extension = Path.GetExtension(name).ToLowerInvariant();

        return extension switch
        {
            ".mpk" or ".epk" or ".npk" => ClientAssetKind.Archive,
            ".nif" => ClientAssetKind.Model,
            ".nhd" => ClientAssetKind.WorldProp,
            ".dds" or ".tga" or ".pcx" or ".bmp" or ".png" or ".jpg" or ".jpeg" => ClientAssetKind.Texture,
            ".wav" => ClientAssetKind.Audio,
            ".xml" => ClientAssetKind.Ui,
            ".csv" or ".dat" => ClientAssetKind.ZoneData,
            ".col" => ClientAssetKind.ColorTable,
            ".txt" or ".ini" or ".cfg" => ClientAssetKind.TextData,
            _ => ClientAssetKind.Unknown,
        };
    }
}
