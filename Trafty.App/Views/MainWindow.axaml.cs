using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Trafty.App.Services;
using Trafty.App.ViewModels;
using Trafty.Core.Archives;
using Trafty.Core.Audio;
using Trafty.Core.Images;
using Trafty.Core.Textures;
using Trafty.Core.UI;
using Trafty.Core.Weather;
using Trafty.Core.WorldProps;
using Trafty.Core.Zones;

namespace Trafty.App.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();

        DataContext = _viewModel;

        var listBox = this.FindControl<ListBox>("AssetList");

        if (listBox is not null)
        {
            listBox.SelectionChanged += (_, _) => _viewModel.InspectSelectedIfModel();
        }

        var dropZone = this.FindControl<Border>("DropZone")!;
        dropZone.AddHandler(DragDrop.DropEvent, OnDropZoneDrop);
        dropZone.AddHandler(DragDrop.DragOverEvent, OnDropZoneDragOver);
    }

    private async void OnRestoreBackupClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedBackup is null)
        {
            return;
        }

        var confirmWindow = new Window
        {
            Title = "Confirm Restore",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        bool confirmed = false;

        var yesButton = new Button { Content = "Restore", Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        var noButton = new Button { Content = "Cancel" };

        yesButton.Click += (_, _) => { confirmed = true; confirmWindow.Close(); };
        noButton.Click += (_, _) => confirmWindow.Close();

        confirmWindow.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = $"Overwrite the current archive with the backup from " +
                           $"{_viewModel.SelectedBackup.TimestampDisplay}? This cannot be undone.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { yesButton, noButton },
                },
            },
        };

        await confirmWindow.ShowDialog(this);

        if (confirmed)
        {
            try
            {
                _viewModel.RestoreSelectedBackup();
            }
            catch (IOException ex)
            {
                _viewModel.StatusText = $"Restore failed: {ex.Message}";
            }
        }
    }

    private async void OnOpenWorldPropsFolderClick(object? sender, RoutedEventArgs e)
    {
        IStorageProvider storage = StorageProvider;

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open World Props Folder (e.g. zones\\Nifs)",
            AllowMultiple = false,
        });

        if (folders.Count == 0)
        {
            return;
        }

        try
        {
            _viewModel.LoadWorldPropsFolder(folders[0].Path.LocalPath);
        }
        catch (IOException ex)
        {
            _viewModel.StatusText = $"Could not read folder: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            _viewModel.StatusText = $"Access denied: {ex.Message}";
        }
    }

    private async void OnOpenColorTableClick(object? sender, RoutedEventArgs e)
    {
        IStorageProvider storage = StorageProvider;

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Color/Weather Table",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Color Tables") { Patterns = new[] { "*.col" } },
                FilePickerFileTypes.All,
            },
        });

        if (files.Count == 0)
        {
            return;
        }

        try
        {
            _viewModel.LoadColorTable(files[0].Path.LocalPath);
        }
        catch (WeatherFormatException ex)
        {
            _viewModel.StatusText = $"Could not read color table: {ex.Message}";
        }
        catch (IOException ex)
        {
            _viewModel.StatusText = $"I/O error: {ex.Message}";
        }
    }

    private async void OnOpenArchiveClick(object? sender, RoutedEventArgs e)
    {
        IStorageProvider storage = StorageProvider;

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open MPK/EPK Archive",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("MPK/EPK/NPK Archives") { Patterns = new[] { "*.mpk", "*.epk", "*.npk" } },
                FilePickerFileTypes.All,
            },
        });

        if (files.Count == 0)
        {
            return;
        }

        string path = files[0].Path.LocalPath;

        try
        {
            _viewModel.LoadArchive(path);
        }
        catch (MpkFormatException ex)
        {
            _viewModel.StatusText = $"Could not open archive: {ex.Message}";
        }
        catch (IOException ex)
        {
            _viewModel.StatusText = $"I/O error: {ex.Message}";
        }
    }

    private async void OnOpenZoneMapClick(object? sender, RoutedEventArgs e)
    {
        IStorageProvider storage = StorageProvider;

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Zone Map Archive (e.g. csv003.mpk or dat003.mpk)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("MPK Archives") { Patterns = new[] { "*.mpk" } },
                FilePickerFileTypes.All,
            },
        });

        if (files.Count == 0)
        {
            return;
        }

        try
        {
            _viewModel.LoadZoneMap(files[0].Path.LocalPath);
        }
        catch (ZoneCsvFormatException ex)
        {
            _viewModel.StatusText = $"Could not read zone map: {ex.Message}";
        }
        catch (MpkFormatException ex)
        {
            _viewModel.StatusText = $"Could not open archive: {ex.Message}";
        }
        catch (IOException ex)
        {
            _viewModel.StatusText = $"I/O error: {ex.Message}";
        }
    }

    private Point? _modelPreviewDragStart;

    private void OnModelPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control)
        {
            _modelPreviewDragStart = e.GetPosition(control);
            e.Pointer.Capture(control);
        }
    }

    private void OnModelPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_modelPreviewDragStart is not { } start || sender is not Control control)
        {
            return;
        }

        Point current = e.GetPosition(control);
        double deltaX = current.X - start.X;
        double deltaY = current.Y - start.Y;

        // A full drag across the preview's width/height sweeps ~180 degrees — feels
        // proportional regardless of the control's actual pixel size.
        float deltaYaw = (float)(deltaX / control.Bounds.Width * 180.0);
        float deltaPitch = (float)(-deltaY / control.Bounds.Height * 180.0);

        _viewModel.RotateModelPreview(deltaYaw, deltaPitch);
        _modelPreviewDragStart = current;
    }

    private void OnModelPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _modelPreviewDragStart = null;

        if (sender is Control control)
        {
            e.Pointer.Capture(null);
        }
    }

    private void OnZoneFixtureEllipsePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: ZoneFixtureRow fixture })
        {
            _viewModel.SelectedZoneFixture = fixture;
            e.Handled = true; // don't also start a pan-drag on the containing viewport
        }
    }

    private const double ZoneMapMinZoom = 0.3;
    private const double ZoneMapMaxZoom = 6.0;
    private Point? _zoneMapDragStart;
    private double _zoneMapZoom = 1.0;
    private double _zoneMapPanX;
    private double _zoneMapPanY;

    private void ApplyZoneMapTransform()
    {
        Canvas? canvas = this.FindControl<Canvas>("ZoneMapCanvas");

        if (canvas is not null)
        {
            canvas.RenderTransform = new MatrixTransform(
                Matrix.CreateScale(_zoneMapZoom, _zoneMapZoom) * Matrix.CreateTranslation(_zoneMapPanX, _zoneMapPanY));
        }
    }

    private void OnZoneMapPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        double factor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        _zoneMapZoom = Math.Clamp(_zoneMapZoom * factor, ZoneMapMinZoom, ZoneMapMaxZoom);
        ApplyZoneMapTransform();
        e.Handled = true;
    }

    private void OnZoneMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control)
        {
            _zoneMapDragStart = e.GetPosition(control);
            e.Pointer.Capture(control);
        }
    }

    private void OnZoneMapPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_zoneMapDragStart is not { } start || sender is not Control control)
        {
            return;
        }

        Point current = e.GetPosition(control);
        _zoneMapPanX += current.X - start.X;
        _zoneMapPanY += current.Y - start.Y;
        ApplyZoneMapTransform();
        _zoneMapDragStart = current;
    }

    private void OnZoneMapPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _zoneMapDragStart = null;

        if (sender is Control control)
        {
            e.Pointer.Capture(null);
        }
    }

    /// <summary>
    /// Maps a click on the (letterboxed, Stretch="Uniform") color table image back to a
    /// pixel coordinate in the underlying 128x66 raster, for Modul B's atmosphere tweaker.
    /// </summary>
    private void OnColorTableImagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Image { Source: Avalonia.Media.Imaging.Bitmap bitmap } image)
        {
            return;
        }

        Point clickPos = e.GetPosition(image);
        double controlWidth = image.Bounds.Width;
        double controlHeight = image.Bounds.Height;

        if (controlWidth <= 0 || controlHeight <= 0)
        {
            return;
        }

        int sourceWidth = bitmap.PixelSize.Width;
        int sourceHeight = bitmap.PixelSize.Height;
        double scale = Math.Min(controlWidth / sourceWidth, controlHeight / sourceHeight);
        double renderedWidth = sourceWidth * scale;
        double renderedHeight = sourceHeight * scale;
        double offsetX = (controlWidth - renderedWidth) / 2;
        double offsetY = (controlHeight - renderedHeight) / 2;

        int pixelX = (int)((clickPos.X - offsetX) / scale);
        int pixelY = (int)((clickPos.Y - offsetY) / scale);

        if ((uint)pixelX < (uint)sourceWidth && (uint)pixelY < (uint)sourceHeight)
        {
            _viewModel.SelectColorTablePixel(pixelX, pixelY);
        }
    }

    private void OnApplyColorTablePixelClick(object? sender, RoutedEventArgs e) =>
        _viewModel.ApplySelectedPixelColor();

    private void OnSaveColorTableClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.SaveColorTable();
        }
        catch (IOException ex)
        {
            _viewModel.StatusText = $"Save failed: {ex.Message}";
        }
    }

    private async void OnOpenUiWindowClick(object? sender, RoutedEventArgs e)
    {
        IStorageProvider storage = StorageProvider;

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open UI Window XML (e.g. chat_window.xml)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("UI Window XML") { Patterns = new[] { "*.xml" } },
                FilePickerFileTypes.All,
            },
        });

        if (files.Count == 0)
        {
            return;
        }

        try
        {
            _viewModel.LoadUiWindow(files[0].Path.LocalPath);
        }
        catch (DaocUiFormatException ex)
        {
            _viewModel.StatusText = $"Could not read UI window: {ex.Message}";
        }
        catch (IOException ex)
        {
            _viewModel.StatusText = $"I/O error: {ex.Message}";
        }
    }

    private async void OnOpenTextureClick(object? sender, RoutedEventArgs e)
    {
        IStorageProvider storage = StorageProvider;

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Texture (.tga or .dds)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Textures") { Patterns = new[] { "*.tga", "*.dds" } },
                FilePickerFileTypes.All,
            },
        });

        if (files.Count == 0)
        {
            return;
        }

        try
        {
            _viewModel.LoadTexturePreview(files[0].Path.LocalPath);
        }
        catch (TextureFormatException ex)
        {
            _viewModel.StatusText = $"Could not read texture: {ex.Message}";
        }
        catch (TgaFormatException ex)
        {
            _viewModel.StatusText = $"Could not read texture: {ex.Message}";
        }
        catch (IOException ex)
        {
            _viewModel.StatusText = $"I/O error: {ex.Message}";
        }
    }

    private async void OnOpenSoundClick(object? sender, RoutedEventArgs e)
    {
        IStorageProvider storage = StorageProvider;

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Sound",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("WAV Files") { Patterns = new[] { "*.wav" } },
                FilePickerFileTypes.All,
            },
        });

        if (files.Count == 0)
        {
            return;
        }

        try
        {
            _viewModel.LoadSound(files[0].Path.LocalPath);
        }
        catch (AudioFormatException ex)
        {
            _viewModel.StatusText = $"Could not read sound: {ex.Message}";
        }
        catch (IOException ex)
        {
            _viewModel.StatusText = $"I/O error: {ex.Message}";
        }
    }

    private void OnPlaySoundClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SoundPath is null)
        {
            return;
        }

        if (!WavPlayer.IsSupported)
        {
            _viewModel.StatusText = "Sound playback is only implemented for Windows (winmm.dll).";
            return;
        }

        WavPlayer.Play(_viewModel.SoundPath);
    }

    private void OnStopSoundClick(object? sender, RoutedEventArgs e) => WavPlayer.Stop();

    private void OnCloseSoundClick(object? sender, RoutedEventArgs e)
    {
        WavPlayer.Stop();
        _viewModel.CloseSound();
    }

    private async void OnAboutClick(object? sender, RoutedEventArgs e) => await new AboutWindow().ShowDialog(this);

    private void OnBackupNowClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.CreateBackupNow();
        }
        catch (IOException ex)
        {
            _viewModel.StatusText = $"Backup failed: {ex.Message}";
        }
    }

    private async void OnAddFilesClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.HasArchive)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add Files into Archive",
            AllowMultiple = true,
        });

        if (files.Count == 0)
        {
            return;
        }

        try
        {
            _viewModel.AddFiles(files.Select(f => f.Path.LocalPath));
        }
        catch (Exception ex) when (ex is IOException or MpkFormatException)
        {
            _viewModel.StatusText = $"Add files failed: {ex.Message}";
        }
    }

    private async void OnExtractAllClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.HasArchive)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Extract All To…",
            AllowMultiple = false,
        });

        if (folders.Count == 0)
        {
            return;
        }

        try
        {
            _viewModel.ExtractAll(folders[0].Path.LocalPath);
        }
        catch (Exception ex) when (ex is IOException or MpkFormatException)
        {
            _viewModel.StatusText = $"Extract failed: {ex.Message}";
        }
    }

    private async void OnExtractAllArchivesClick(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFolder> sourceFolders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Folder Containing Archives (searched recursively)",
            AllowMultiple = false,
        });

        if (sourceFolders.Count == 0)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> destinationFolders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Extract All Archives To…",
            AllowMultiple = false,
        });

        if (destinationFolders.Count == 0)
        {
            return;
        }

        _viewModel.StatusText = "Extracting all archives, this may take a while…";

        try
        {
            string sourcePath = sourceFolders[0].Path.LocalPath;
            string destinationPath = destinationFolders[0].Path.LocalPath;
            await Task.Run(() => _viewModel.ExtractAllArchives(sourcePath, destinationPath));
        }
        catch (IOException ex)
        {
            _viewModel.StatusText = $"Extract all archives failed: {ex.Message}";
        }
    }

    private async void OnExtractSelectedClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedEntry is not { } selected)
        {
            return;
        }

        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Extract Selected Entry",
            SuggestedFileName = selected.Name,
        });

        if (file is null)
        {
            return;
        }

        try
        {
            _viewModel.ExtractSelected(file.Path.LocalPath);
        }
        catch (Exception ex) when (ex is IOException or MpkFormatException)
        {
            _viewModel.StatusText = $"Extract failed: {ex.Message}";
        }
    }

    private void OnDropZoneDragOver(object? sender, DragEventArgs e)
    {
        bool isSingleImageFile =
            _viewModel.CanReplaceSelected &&
            e.Data.Contains(DataFormats.Files) &&
            e.Data.GetFiles()?.Count() == 1;

        e.DragEffects = isSingleImageFile ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDropZoneDrop(object? sender, DragEventArgs e)
    {
        if (!_viewModel.CanReplaceSelected)
        {
            _viewModel.StatusText = "Please select an asset in the list first.";
            return;
        }

        IStorageItem? file = e.Data.GetFiles()?.FirstOrDefault();

        if (file is null)
        {
            return;
        }

        await ReplaceSelectedAssetAsync(file.Path.LocalPath);
    }

    private async Task ReplaceSelectedAssetAsync(string imagePath)
    {
        AssetRow selected = _viewModel.SelectedEntry!;
        string archivePath = _viewModel.ArchivePath!;
        string baseName = Path.GetFileNameWithoutExtension(selected.Name);

        _viewModel.StatusText = $"Encoding {Path.GetFileName(imagePath)}…";

        try
        {
            await Task.Run(() => ReplaceAsset(archivePath, imagePath, baseName));

            _viewModel.Reload();
            _viewModel.RefreshBackups();
            _viewModel.StatusText = $"\"{selected.Name}\" replaced with {Path.GetFileName(imagePath)}. Backup created.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
        {
            _viewModel.StatusText = $"Replace failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Runs off the UI thread: encodes the dropped image into a full mip chain, backs up
    /// the archive, and repacks it with the new entries.
    /// </summary>
    private static void ReplaceAsset(string archivePath, string imagePath, string baseName)
    {
        IReadOnlyList<EncodedMipLevel> mips = DdsEncoder.EncodeFile(imagePath);
        IReadOnlyList<string> names = DdsEncoder.NameMipLevels(baseName, mips.Count);

        var replacements = new List<MpkPendingEntry>(mips.Count);

        for (int i = 0; i < mips.Count; i++)
        {
            replacements.Add(new MpkPendingEntry
            {
                Name = names[i],
                UncompressedData = mips[i].DdsBytes,
            });
        }

        string archiveDirectory = Path.GetDirectoryName(Path.GetFullPath(archivePath)) ?? ".";
        new BackupVault(archiveDirectory).Backup(archivePath);

        string tempPath = archivePath + ".tmp";

        using (MpkArchive source = MpkArchive.Open(archivePath))
        {
            MpkArchiveWriter.WriteReplacing(source, replacements, tempPath);
        }

        File.Move(tempPath, archivePath, overwrite: true);
    }
}
