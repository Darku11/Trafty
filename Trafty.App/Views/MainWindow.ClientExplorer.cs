using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Trafty.App.Views;

public partial class MainWindow
{
    private async void OnScanClientClick(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Dark Age of Camelot Client Folder",
            AllowMultiple = false,
        });

        if (folders.Count == 0)
        {
            return;
        }

        await new ClientExplorerWindow(folders[0].Path.LocalPath).ShowDialog(this);
    }
}
