using Trafty.Core.WorldProps;

namespace Trafty.App.ViewModels;

/// <summary>
/// Flat, display-ready view of one .nhd file found while browsing a folder of world
/// props (e.g. a client's zones\Nifs directory).
/// </summary>
public sealed class WorldPropRow
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string ModelName { get; init; }
    public required int GridWidth { get; init; }
    public required int GridHeight { get; init; }

    public string GridDisplay => $"{GridWidth} x {GridHeight}";

    public static WorldPropRow FromNhd(string path, NhdFile nhd) => new()
    {
        FilePath = path,
        FileName = Path.GetFileName(path),
        ModelName = nhd.ModelName,
        GridWidth = nhd.GridWidth,
        GridHeight = nhd.GridHeight,
    };
}
