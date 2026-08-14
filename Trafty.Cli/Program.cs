using Trafty.Core.Archives;
using Trafty.Core.Audio;
using Trafty.Core.Models;
using Trafty.Core.Textures;
using Trafty.Core.Weather;
using Trafty.Core.WorldProps;

namespace Trafty.Cli;

/// <summary>
/// Command line front end for the archive layer. It exists mainly to exercise the parser
/// against real client archives before the Avalonia shell is built on top of it.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            PrintUsage();
            return 1;
        }

        string command = args[0].ToLowerInvariant();
        string archivePath = args[1];

        if (!File.Exists(archivePath))
        {
            Console.Error.WriteLine($"File not found: {archivePath}");
            return 1;
        }

        try
        {
            if (command == "replace")
            {
                return Replace(archivePath, args);
            }

            if (command == "nhd")
            {
                return InspectNhd(archivePath);
            }

            if (command == "nif")
            {
                return InspectNif(archivePath);
            }

            if (command == "wav")
            {
                return InspectWav(archivePath);
            }

            if (command == "col")
            {
                return InspectCol(archivePath, args);
            }

            if (command == "backups")
            {
                return ListBackups(archivePath);
            }

            if (command == "restore")
            {
                return Restore(archivePath, args);
            }

            using MpkArchive archive = MpkArchive.Open(archivePath);

            return command switch
            {
                "info" => Info(archive),
                "list" => List(archive),
                "verify" => Verify(archive),
                "extract" => Extract(archive, args.Length > 2 ? args[2] : "extracted"),
                _ => UnknownCommand(command),
            };
        }
        catch (MpkFormatException ex)
        {
            Console.Error.WriteLine($"Not a usable MPAK container: {ex.Message}");
            return 2;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"I/O error: {ex.Message}");
            return 3;
        }
    }

    private static int Info(MpkArchive archive)
    {
        MpkHeader header = archive.Header;

        Console.WriteLine($"Archive name     : {archive.ArchiveName}");
        Console.WriteLine($"Format version   : {header.Version}");
        Console.WriteLine($"Files            : {header.FileCount}");
        Console.WriteLine($"Directory CRC32  : 0x{header.DirectoryCrc32:X8}");
        Console.WriteLine($"Name block       : offset {header.NameOffset}, {header.NameCompressedSize} bytes packed");
        Console.WriteLine($"Directory block  : offset {header.DirectoryOffset}, {header.DirectoryCompressedSize} bytes packed");
        Console.WriteLine($"Data region      : offset {header.DataOffset}");

        var byExtension = archive.Entries
            .GroupBy(e => e.Extension)
            .OrderByDescending(g => g.Count());

        Console.WriteLine();
        Console.WriteLine("Content by type:");

        foreach (var group in byExtension)
        {
            long bytes = group.Sum(e => (long)e.UncompressedSize);
            string extension = string.IsNullOrEmpty(group.Key) ? "(none)" : group.Key;

            Console.WriteLine($"  {extension,-8} {group.Count(),5} file(s)  {bytes / 1024.0:N1} KiB unpacked");
        }

        return 0;
    }

    private static int List(MpkArchive archive)
    {
        Console.WriteLine($"{"Name",-32} {"Packed",10} {"Unpacked",12}   {"CRC32",-10} {"Modified",-19}");

        foreach (MpkEntry entry in archive.Entries)
        {
            Console.WriteLine(
                $"{entry.Name,-32} {entry.CompressedSize,10} {entry.UncompressedSize,12} " +
                $"0x{entry.Crc32:X8}   {entry.Timestamp.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC");
        }

        Console.WriteLine();
        Console.WriteLine($"{archive.Entries.Count} entries.");

        return 0;
    }

    private static int Verify(MpkArchive archive)
    {
        IReadOnlyList<string> problems = archive.Verify();

        if (problems.Count == 0)
        {
            Console.WriteLine($"All {archive.Entries.Count} entries are intact.");
            return 0;
        }

        Console.WriteLine($"{problems.Count} problem(s) found:");

        foreach (string problem in problems)
        {
            Console.WriteLine($"  {problem}");
        }

        return 4;
    }

    private static int Extract(MpkArchive archive, string targetDirectory)
    {
        int count = archive.ExtractAll(targetDirectory);

        Console.WriteLine($"Extracted {count} file(s) to {Path.GetFullPath(targetDirectory)}");

        return 0;
    }

    /// <summary>
    /// replace &lt;archive.mpk&gt; &lt;image.png&gt; &lt;baseName&gt;
    /// Encodes the image into a full mip chain, backs up the archive, and writes a new
    /// archive with the generated "baseName-00.dds", "baseName-01.dds", ... entries
    /// replacing (or added to) whatever was there before.
    /// </summary>
    private static int Replace(string archivePath, string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("Usage: trafty replace <archive.mpk> <image.png> <baseName>");
            return 1;
        }

        string imagePath = args[2];
        string baseName = args[3];

        if (!File.Exists(imagePath))
        {
            Console.Error.WriteLine($"Image not found: {imagePath}");
            return 1;
        }

        IReadOnlyList<EncodedMipLevel> mips = DdsEncoder.EncodeFile(imagePath);
        IReadOnlyList<string> names = DdsEncoder.NameMipLevels(baseName, mips.Count);

        Console.WriteLine($"Encoded {mips.Count} mip level(s) from {Path.GetFileName(imagePath)}:");

        for (int i = 0; i < mips.Count; i++)
        {
            Console.WriteLine($"  {names[i]}  {mips[i].Width}x{mips[i].Height}  {mips[i].DdsBytes.Length} bytes");
        }

        string archiveDirectory = Path.GetDirectoryName(Path.GetFullPath(archivePath)) ?? ".";
        var vault = new BackupVault(archiveDirectory);
        string backupPath = vault.Backup(archivePath);
        Console.WriteLine($"Backed up original archive to {backupPath}");

        var replacements = new List<MpkPendingEntry>(mips.Count);

        for (int i = 0; i < mips.Count; i++)
        {
            replacements.Add(new MpkPendingEntry
            {
                Name = names[i],
                UncompressedData = mips[i].DdsBytes,
            });
        }

        string tempPath = archivePath + ".tmp";

        // The archive must be closed before its file can be replaced on Windows, so the
        // read and the move happen in separate scopes rather than one "using" block.
        using (MpkArchive source = MpkArchive.Open(archivePath))
        {
            MpkArchiveWriter.WriteReplacing(source, replacements, tempPath);
        }

        File.Move(tempPath, archivePath, overwrite: true);

        Console.WriteLine($"Wrote {replacements.Count} entrie(s) into {archivePath}");

        return 0;
    }

    /// <summary>nhd &lt;file.nhd&gt; — prints the referenced model and grid dimensions.</summary>
    private static int InspectNhd(string path)
    {
        NhdFile nhd = NhdFile.Load(path);

        Console.WriteLine($"Model reference : {nhd.ModelName}");
        Console.WriteLine($"Version         : {nhd.Version}");
        Console.WriteLine($"Bounding box    : X [{nhd.MinX}, {nhd.MaxX}]  Y [{nhd.MinY}, {nhd.MaxY}]");
        Console.WriteLine($"Grid            : {nhd.GridWidth} x {nhd.GridHeight} ({nhd.Grid.Length} cells)");

        var distinct = nhd.Grid.GroupBy(v => v).OrderByDescending(g => g.Count()).Take(5);

        Console.WriteLine("Most common grid values:");

        foreach (var group in distinct)
        {
            Console.WriteLine($"  {group.Key,6}  x{group.Count()}");
        }

        return 0;
    }

    /// <summary>nif &lt;file.nif&gt; — prints the NetImmerse header (version, block count).</summary>
    private static int InspectNif(string path)
    {
        NifHeader header = NifHeader.Load(path);

        Console.WriteLine($"Signature   : {header.Signature}");
        Console.WriteLine($"Version     : {header.VersionDisplay}");
        Console.WriteLine($"Block count : {header.BlockCount}");

        return 0;
    }

    /// <summary>wav &lt;file.wav&gt; — prints format, channels, sample rate, duration.</summary>
    private static int InspectWav(string path)
    {
        WavHeader header = WavHeader.Load(path);

        Console.WriteLine($"Format        : {header.AudioFormatDisplay}");
        Console.WriteLine($"Channels      : {header.ChannelCount}");
        Console.WriteLine($"Sample rate   : {header.SampleRate} Hz");
        Console.WriteLine($"Bits/sample   : {header.BitsPerSample}");
        Console.WriteLine($"Data size     : {header.DataSize / 1024.0:N1} KiB");
        Console.WriteLine($"Duration      : {header.Duration:mm\\:ss\\.fff}");

        return 0;
    }

    /// <summary>col &lt;file.col&gt; [output.png] — parses a color/weather raster and optionally exports it as PNG.</summary>
    private static int InspectCol(string path, string[] args)
    {
        SystemColFile col = SystemColFile.Load(path);

        Console.WriteLine($"Dimensions : {SystemColFile.Width} x {SystemColFile.Height}");
        Console.WriteLine($"Pixels     : {col.Pixels.Length}");

        if (args.Length > 2)
        {
            string outputPath = args[2];
            SystemColExporter.SaveAsPng(col, outputPath);
            Console.WriteLine($"Wrote preview to {outputPath}");
        }

        return 0;
    }

    /// <summary>backups &lt;archive.mpk&gt; — lists every backup held for this archive, newest first.</summary>
    private static int ListBackups(string archivePath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(archivePath)) ?? ".";
        var vault = new BackupVault(directory);
        string fileName = Path.GetFileName(archivePath);

        IReadOnlyList<string> backups = vault.ListBackups(fileName);

        if (backups.Count == 0)
        {
            Console.WriteLine($"No backups found for {fileName}.");
            return 0;
        }

        Console.WriteLine($"{backups.Count} backup(s) for {fileName}, newest first:");

        for (int i = 0; i < backups.Count; i++)
        {
            Console.WriteLine($"  [{i}] {Path.GetFileName(backups[i])}");
        }

        return 0;
    }

    /// <summary>
    /// restore &lt;archive.mpk&gt; [index] — overwrites the archive with a backup. Index 0
    /// (the default) is the newest backup; use "backups" to see the full list and indices.
    /// </summary>
    private static int Restore(string archivePath, string[] args)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(archivePath)) ?? ".";
        var vault = new BackupVault(directory);
        string fileName = Path.GetFileName(archivePath);

        IReadOnlyList<string> backups = vault.ListBackups(fileName);

        if (backups.Count == 0)
        {
            Console.Error.WriteLine($"No backups found for {fileName}.");
            return 1;
        }

        int index = 0;

        if (args.Length > 2 && !int.TryParse(args[2], out index))
        {
            Console.Error.WriteLine($"Invalid backup index: \"{args[2]}\". Run \"backups\" to see valid indices.");
            return 1;
        }

        if (index < 0 || index >= backups.Count)
        {
            Console.Error.WriteLine($"Backup index {index} is out of range (0-{backups.Count - 1}).");
            return 1;
        }

        string chosen = backups[index];
        vault.Restore(chosen, archivePath);

        Console.WriteLine($"Restored {fileName} from {Path.GetFileName(chosen)}.");

        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();

        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: trafty <command> <archive.mpk> [target]");
        Console.WriteLine();
        Console.WriteLine("  info     Print header fields and a content summary");
        Console.WriteLine("  list     List every directory entry");
        Console.WriteLine("  verify   Check all checksums, sizes and offset chains");
        Console.WriteLine("  extract  Unpack every entry into the target directory");
        Console.WriteLine("  replace  <archive> <image> <baseName>  Encode an image into a mip chain and pack it in");
        Console.WriteLine("  nhd      <file.nhd>  Print the referenced model name, bounding box, and grid stats");
        Console.WriteLine("  nif      <file.nif>  Print the NetImmerse header (version, block count)");
        Console.WriteLine("  wav      <file.wav>  Print audio format, channels, sample rate, duration");
        Console.WriteLine("  col      <file.col> [out.png]  Parse a weather/color raster, optionally export PNG");
        Console.WriteLine("  backups  <archive>  List every backup held for this archive, newest first");
        Console.WriteLine("  restore  <archive> [index]  Restore a backup (default: newest, index 0)");
    }
}
