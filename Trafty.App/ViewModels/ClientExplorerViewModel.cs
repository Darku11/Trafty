using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Trafty.Core.Archives;
using Trafty.Core.Client;
using Trafty.Core.Models;
using Trafty.Core.Models.Nif;
using Trafty.Core.Textures;

namespace Trafty.App.ViewModels;

public sealed class ClientExplorerViewModel : INotifyPropertyChanged
{
    private const int PreviewSize = 360;
    private readonly List<ClientAssetRow> _allAssets = new();
    private string _searchText = string.Empty;
    private string _selectedKind = "All";
    private string _rootDisplayName = "No client folder scanned";
    private string _summaryText = "Choose a DAoC client folder to build an asset index.";
    private ClientAssetRow? _selectedAsset;
    private Bitmap? _previewImage;
    private string? _previewInfo;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ClientAssetRow> Assets { get; } = new();

    public IReadOnlyList<string> KindFilters { get; } = new[]
    {
        "All",
        "3D Models",
        "World Props",
        "Textures / Images",
        "Audio",
        "UI",
        "Zone / Data",
        "Color Tables",
        "Archives",
        "Text / Config",
        "Unknown",
    };

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public string SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (SetField(ref _selectedKind, value ?? "All"))
            {
                ApplyFilter();
            }
        }
    }

    public string RootDisplayName
    {
        get => _rootDisplayName;
        private set => SetField(ref _rootDisplayName, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetField(ref _summaryText, value);
    }

    public ClientAssetRow? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            if (SetField(ref _selectedAsset, value))
            {
                ClearPreview();
            }
        }
    }

    public Bitmap? PreviewImage
    {
        get => _previewImage;
        private set => SetField(ref _previewImage, value);
    }

    public string? PreviewInfo
    {
        get => _previewInfo;
        private set => SetField(ref _previewInfo, value);
    }

    public string VisibleCountText => $"{Assets.Count:N0} result(s)";

    public void BeginScan(string rootPath)
    {
        RootDisplayName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        SummaryText = "Scanning client files and archives…";
        _allAssets.Clear();
        Assets.Clear();
        SelectedAsset = null;
        OnPropertyChanged(nameof(VisibleCountText));
    }

    public void SetScanError(string message)
    {
        SummaryText = $"Client scan failed: {message}";
    }

    public void Load(ClientAssetIndex index)
    {
        _allAssets.Clear();
        _allAssets.AddRange(index.Assets.Select(ClientAssetRow.FromRecord));

        string folderName = Path.GetFileName(index.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        RootDisplayName = string.IsNullOrWhiteSpace(folderName) ? index.RootPath : folderName;

        string failureText = index.Failures.Count == 0
            ? string.Empty
            : $" {index.Failures.Count:N0} unreadable item(s) skipped.";

        SummaryText = $"{index.Assets.Count:N0} assets indexed from {index.ArchiveCount:N0} archive(s).{failureText}";
        SelectedAsset = null;
        ApplyFilter();
    }

    public async Task InspectSelectedAssetAsync()
    {
        ClientAssetRow? asset = SelectedAsset;

        if (asset is null || asset.Kind is not (ClientAssetKind.Model or ClientAssetKind.Texture))
        {
            return;
        }

        PreviewInfo = "Loading preview…";

        PreviewResult result;

        try
        {
            result = await Task.Run(() => BuildPreview(asset));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or MpkFormatException or ModelFormatException or TextureFormatException)
        {
            if (ReferenceEquals(SelectedAsset, asset))
            {
                PreviewInfo = $"Preview unavailable: {ex.Message}";
            }
            return;
        }

        if (!ReferenceEquals(SelectedAsset, asset))
        {
            return;
        }

        PreviewInfo = result.Info;

        if (result.PngBytes is not null)
        {
            using var stream = new MemoryStream(result.PngBytes, writable: false);
            PreviewImage = new Bitmap(stream);
        }
    }

    private static PreviewResult BuildPreview(ClientAssetRow asset)
    {
        byte[] bytes = ReadAssetBytes(asset);

        if (asset.Kind == ClientAssetKind.Model)
        {
            NifHeader header = NifHeader.Parse(bytes);
            string info = $"NetImmerse {header.VersionDisplay} — {header.BlockCount} blocks";

            try
            {
                NifDocument document = NifDocument.Parse(bytes);
                int vertexCount = document.Blocks.OfType<NiTriShapeDataBlock>().Sum(block => block.Vertices.Count);
                int triangleCount = document.Blocks.OfType<NiTriShapeDataBlock>().Sum(block => block.Triangles.Count);
                info += $"\n{vertexCount:N0} vertices, {triangleCount:N0} triangles";

                IReadOnlyList<NifWorldTriangle> mesh = NifSceneMesh.Build(document);

                if (mesh.Count > 0)
                {
                    using var png = new MemoryStream();
                    NifMeshPreviewRenderer.SaveAsPng(mesh, PreviewSize, PreviewSize, png);
                    return new PreviewResult(png.ToArray(), info);
                }
            }
            catch (ModelFormatException)
            {
                // Header information is still useful when this model contains a block type
                // that the current full NIF parser does not understand yet.
            }

            return new PreviewResult(null, info + "\n3D preview not available for this NIF variant yet.");
        }

        if (asset.Kind == ClientAssetKind.Texture &&
            Path.GetExtension(asset.Name).Equals(".dds", StringComparison.OrdinalIgnoreCase))
        {
            DdsFile dds = DdsFile.Parse(bytes);
            using var png = new MemoryStream();
            DdsExporter.SaveAsPng(dds, png, forceOpaque: false);
            return new PreviewResult(png.ToArray(), "DDS texture preview");
        }

        return new PreviewResult(null, "Preview is not implemented for this texture format yet.");
    }

    internal static byte[] ReadAssetBytes(ClientAssetRow asset)
    {
        if (!asset.IsArchived)
        {
            return File.ReadAllBytes(asset.PhysicalPath);
        }

        using MpkArchive archive = MpkArchive.Open(asset.PhysicalPath);
        MpkEntry entry = archive[asset.ArchiveEntryName!]
            ?? throw new FileNotFoundException($"Archive entry not found: {asset.ArchiveEntryName}");

        return archive.Extract(entry);
    }

    private void ClearPreview()
    {
        PreviewImage?.Dispose();
        PreviewImage = null;
        PreviewInfo = null;
    }

    private void ApplyFilter()
    {
        string query = SearchText.Trim();
        ClientAssetKind? kind = SelectedKind switch
        {
            "3D Models" => ClientAssetKind.Model,
            "World Props" => ClientAssetKind.WorldProp,
            "Textures / Images" => ClientAssetKind.Texture,
            "Audio" => ClientAssetKind.Audio,
            "UI" => ClientAssetKind.Ui,
            "Zone / Data" => ClientAssetKind.ZoneData,
            "Color Tables" => ClientAssetKind.ColorTable,
            "Archives" => ClientAssetKind.Archive,
            "Text / Config" => ClientAssetKind.TextData,
            "Unknown" => ClientAssetKind.Unknown,
            _ => null,
        };

        IEnumerable<ClientAssetRow> filtered = _allAssets;

        if (kind is not null)
        {
            filtered = filtered.Where(asset => asset.Kind == kind);
        }

        if (query.Length > 0)
        {
            filtered = filtered.Where(asset =>
                asset.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                asset.LocationDisplay.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        Assets.Clear();

        foreach (ClientAssetRow asset in filtered.Take(50_000))
        {
            Assets.Add(asset);
        }

        OnPropertyChanged(nameof(VisibleCountText));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record PreviewResult(byte[]? PngBytes, string Info);
}
