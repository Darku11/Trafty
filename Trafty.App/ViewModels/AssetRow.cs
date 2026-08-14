using Avalonia.Media.Imaging;
using Trafty.Core.Archives;

namespace Trafty.App.ViewModels;

/// <summary>
/// Flat, display-ready view of an <see cref="MpkEntry"/>. Kept separate from the core
/// model so the UI never depends on parser internals.
/// </summary>
public sealed class AssetRow
{
    public required string Name { get; init; }
    public required string Extension { get; init; }
    public required uint UncompressedSize { get; init; }
    public required uint CompressedSize { get; init; }
    public required uint Crc32 { get; init; }
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Decoded texture preview for the Modul A thumbnail grid, set by the view model after
    /// construction for .dds entries. Null for everything else, or if decoding failed.
    /// </summary>
    public Bitmap? Thumbnail { get; set; }

    public string SizeDisplay => $"{UncompressedSize / 1024.0:N1} KiB";
    public string Crc32Display => $"0x{Crc32:X8}";
    public string TimestampDisplay => Timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm");

    /// <summary>True for 3D model entries (.nif) — these get an extra "Inspect" action.</summary>
    public bool IsModel => Extension.Equals("nif", StringComparison.OrdinalIgnoreCase);

    public static AssetRow FromEntry(MpkEntry entry) => new()
    {
        Name = entry.Name,
        Extension = entry.Extension,
        UncompressedSize = entry.UncompressedSize,
        CompressedSize = entry.CompressedSize,
        Crc32 = entry.Crc32,
        Timestamp = entry.Timestamp,
    };
}
