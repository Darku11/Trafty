namespace Trafty.Core.Archives;

/// <summary>
/// Grundprinzip #2: nothing gets modified without a backup first. Keeps timestamped
/// copies of archives next to a ".trafty-backups" folder so any edit can be undone from
/// the UI with a single "Restore" action.
/// </summary>
public sealed class BackupVault
{
    private const string VaultFolderName = ".trafty-backups";

    /// <summary>
    /// Root folder the vault stores backups under. Defaults to a hidden folder next to
    /// the archive being edited.
    /// </summary>
    public string VaultDirectory { get; }

    public BackupVault(string archiveDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveDirectory);

        VaultDirectory = Path.Combine(archiveDirectory, VaultFolderName);
    }

    /// <summary>
    /// Copies <paramref name="filePath"/> into the vault, tagged with the current time, so
    /// it does not collide with earlier backups of the same file. Safe to call before every
    /// write — copying a file that has not changed since the last backup still succeeds,
    /// it just costs a bit of disk space.
    /// </summary>
    /// <returns>Full path of the backup copy that was created.</returns>
    public string Backup(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Cannot back up a file that does not exist.", filePath);
        }

        Directory.CreateDirectory(VaultDirectory);

        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        string fileName = Path.GetFileName(filePath);
        string backupPath = Path.Combine(VaultDirectory, $"{fileName}.{stamp}.bak");

        File.Copy(filePath, backupPath, overwrite: false);

        return backupPath;
    }

    /// <summary>
    /// Lists every backup held for a given archive file name, most recent first.
    /// </summary>
    public IReadOnlyList<string> ListBackups(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (!Directory.Exists(VaultDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(VaultDirectory, $"{fileName}.*.bak")
            .OrderByDescending(f => f)
            .ToList();
    }

    /// <summary>
    /// Restores a specific backup over the given target path, overwriting whatever is
    /// there. The caller is responsible for confirming this with the user first — this
    /// method does not create a "backup of the backup".
    /// </summary>
    public void Restore(string backupPath, string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("Backup file not found.", backupPath);
        }

        File.Copy(backupPath, targetPath, overwrite: true);
    }
}
