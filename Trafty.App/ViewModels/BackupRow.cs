using System.Globalization;

namespace Trafty.App.ViewModels;

/// <summary>
/// One entry in the Backup Vault, parsed from its file name
/// ("archive.mpk.20260814-153000-123.bak") into a displayable timestamp.
/// </summary>
public sealed class BackupRow
{
    public required string FullPath { get; init; }
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// True for the oldest backup of this archive — the pristine, untouched state captured
    /// before Trafty's very first write to it. Backups are never pruned, so this is always
    /// recoverable no matter how many edits followed.
    /// </summary>
    public bool IsOriginal { get; init; }

    public string TimestampDisplay => IsOriginal
        ? $"{Timestamp.UtcDateTime:yyyy-MM-dd HH:mm:ss} (Original)"
        : Timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");

    public static BackupRow FromPath(string path)
    {
        string fileName = Path.GetFileName(path);

        // Expected shape: "<archiveName>.<yyyyMMdd-HHmmss-fff>.bak"
        string[] parts = fileName.Split('.');
        DateTimeOffset timestamp = default;

        if (parts.Length >= 3)
        {
            string stamp = parts[^2]; // second-to-last segment, before ".bak"

            if (DateTime.TryParseExact(
                    stamp, "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out DateTime parsed))
            {
                timestamp = new DateTimeOffset(parsed, TimeSpan.Zero);
            }
        }

        return new BackupRow { FullPath = path, Timestamp = timestamp };
    }
}
