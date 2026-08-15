using Avalonia.Controls;
using Trafty.App.ViewModels;
using Trafty.Core.Client;

namespace Trafty.App.Views;

public partial class ClientExplorerWindow : Window
{
    private readonly ClientExplorerViewModel _viewModel = new();
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
