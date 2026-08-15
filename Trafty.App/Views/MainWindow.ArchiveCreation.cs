using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Trafty.Core.Archives;

namespace Trafty.App.Views;

public partial class MainWindow
{
    private async void OnNewArchiveClick(object? sender, RoutedEventArgs e)
    {
        var mpkFileType = new FilePickerFileType("MPK Archives")
        {
            Patterns = new[] { "*.mpk" },
        };

        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create MPK Archive",
            SuggestedFileName = "new_archive.mpk",
            DefaultExtension = "mpk",
            FileTypeChoices = new[] { mpkFileType },
            ShowOverwritePrompt = true,
        });

        if (file is null)
        {
            return;
        }

        string path = file.Path.LocalPath;
        string archiveName = Path.GetFileName(path);
        string tempPath = path + ".tmp";
        bool backedUpExisting = false;

        try
        {
            MpkArchiveWriter.Write(Array.Empty<MpkPendingEntry>(), archiveName, tempPath);

            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
                new BackupVault(directory).Backup(path);
                backedUpExisting = true;
            }

            File.Move(tempPath, path, overwrite: true);
            _viewModel.LoadArchive(path);
            _viewModel.StatusText = backedUpExisting
                ? $"Created new archive {archiveName}. Previous file backed up."
                : $"Created new archive {archiveName}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or MpkFormatException)
        {
            _viewModel.StatusText = $"Could not create archive: {ex.Message}";
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // Best-effort cleanup only. The target archive is never replaced until
                    // the temporary archive has been written successfully.
                }
            }
        }
    }
}
