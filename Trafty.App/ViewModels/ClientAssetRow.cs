using Trafty.Core.Client;

namespace Trafty.App.ViewModels;

public sealed class ClientAssetRow
{
    public required string Name { get; init; }
    public required ClientAssetKind Kind { get; init; }
    public required string KindDisplay { get; init; }
    public required string LocationDisplay { get; init; }
    public required string PhysicalPath { get; init; }
    public required long Size { get; init; }
    public string? ArchiveEntryName { get; init; }

    public bool IsArchived => ArchiveEntryName is not null;

    public string SizeDisplay => Size switch
    {
        >= 1024L * 1024L => $"{Size / (1024d * 1024d):0.##} MB",
        >= 1024L => $"{Size / 1024d:0.##} KB",
        _ => $"{Size} B",
    };

    public static ClientAssetRow FromRecord(ClientAssetRecord record) => new()
    {
        Name = record.Name,
        Kind = record.Kind,
        KindDisplay = record.Kind switch
        {
            ClientAssetKind.Archive => "Archive",
            ClientAssetKind.Model => "3D Model",
            ClientAssetKind.WorldProp => "World Prop",
            ClientAssetKind.Texture => "Texture / Image",
            ClientAssetKind.Audio => "Audio",
            ClientAssetKind.Ui => "UI",
            ClientAssetKind.ZoneData => "Zone / Data",
            ClientAssetKind.ColorTable => "Color Table",
            ClientAssetKind.TextData => "Text / Config",
            _ => "Unknown",
        },
        LocationDisplay = record.RelativeLocation,
        PhysicalPath = record.PhysicalPath,
        Size = record.Size,
        ArchiveEntryName = record.ArchiveEntryName,
    };
}
