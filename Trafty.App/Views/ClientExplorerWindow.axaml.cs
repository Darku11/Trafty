using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Trafty.App.Services;
using Trafty.App.ViewModels;
using Trafty.Core.Archives;
using Trafty.Core.Client;

namespace Trafty.App.Views;

public partial class ClientExplorerWindow : Window
{
    private readonly ClientExplorerViewModel _viewModel = new();
    private readonly AssetPreviewPopupController _previewPopup;
    private readonly string? _rootPath;
    private bool _scanStarted;

    public ClientExplorerWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        ListBox? assetList = this.FindControl<ListBox>("ClientAssetList");

        if (assetList is not null)
        {
            assetList.SelectionChanged += async (_, _) => await _viewModel.InspectSelectedAssetAsync();
        }

        _previewPopup = new AssetPreviewPopupController(
            this.FindControl<Popup>("AssetPreviewPopupSmall")!,
            this.FindControl<Image>("AssetPreviewImageSmall")!,
            this.FindControl<Popup>("AssetPreviewPopupLarge")!,
            this.FindControl<Image>("AssetPreviewImageLarge")!,
            this.FindControl<TextBlock>("AssetPreviewInfoLarge"),
            smallMessage: this.FindControl<TextBlock>("AssetPreviewMessageSmall"));
    }

    /// <summary>
    /// Right-click on an asset opens a small preview popup near the cursor — a thumbnail for
    /// a previewable format, or a short explanation otherwise. Hovering over a thumbnail
    /// (handled by <see cref="AssetPreviewPopupController"/>) swaps to a larger popup. Reads
    /// and renders off the UI thread since archive-contained NIF/DDS entries can be large.
    /// Bound to the ListBox rather than each row's DataTemplate so it fires reliably no
    /// matter which child control the click landed on.
    /// </summary>
    private async void OnAssetContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (FindDataContext<ClientAssetRow>(e.Source as StyledElement) is not { } row)
        {
            return;
        }

        e.Handled = true;
        _previewPopup.HideAll();
        string extension = Path.GetExtension(row.Name);

        if (!AssetPreviewRenderer.IsPreviewable(extension))
        {
            _previewPopup.ShowMessage($"\"{row.Name}\" has no visual preview — Trafty can only render .dds, .tga and .nif files.");
            return;
        }

        byte[]? png = await Task.Run(() =>
        {
            try
            {
                byte[] bytes = ClientExplorerViewModel.ReadAssetBytes(row);
                return AssetPreviewRenderer.TryRenderPng(bytes, extension);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or MpkFormatException)
            {
                return null;
            }
        });

        if (png is null)
        {
            _previewPopup.ShowMessage($"\"{row.Name}\" could not be rendered — the file may use a variant of the format Trafty doesn't support yet.");
            return;
        }

        using var stream = new MemoryStream(png, writable: false);
        _previewPopup.ShowSmall(new Bitmap(stream), row.Name);
    }

    private static T? FindDataContext<T>(StyledElement? element) where T : class
    {
        while (element is not null)
        {
            if (element.DataContext is T match)
            {
                return match;
            }

            element = element.Parent;
        }

        return null;
    }

    public ClientExplorerWindow(string rootPath) : this()
    {
        _rootPath = rootPath;
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (_scanStarted || string.IsNullOrWhiteSpace(_rootPath))
        {
            return;
        }

        _scanStarted = true;
        _viewModel.BeginScan(_rootPath);

        try
        {
            ClientAssetIndex index = await Task.Run(() => ClientAssetScanner.Scan(_rootPath));
            _viewModel.Load(index);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            _viewModel.SetScanError(ex.Message);
        }
    }
}
