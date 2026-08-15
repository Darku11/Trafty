namespace Trafty.Core.Client;

public enum ClientAssetKind
{
    Unknown,
    Archive,
    Model,
    WorldProp,
    Texture,
    Audio,
    Ui,
    ZoneData,
    ColorTable,
    TextData,
}

public sealed record ClientAssetRecord
{
    public required string Name { get; init; }
    public required ClientAssetKind Kind { get; init; }
    public required string PhysicalPath { get; init; }
    public required string RelativeLocation { get; init; }
    public required long Size { get; init; }
    public string? ArchiveEntryName { get; init; }

    public bool IsArchived => ArchiveEntryName is not null;
}

public sealed record ClientScanFailure
{
    public required string Path { get; init; }
    public required string Message { get; init; }
}

public sealed class ClientAssetIndex
{
    public ClientAssetIndex(
        string rootPath,
        IReadOnlyList<ClientAssetRecord> assets,
        IReadOnlyList<ClientScanFailure> failures,
        int archiveCount)
    {
        RootPath = rootPath;
        Assets = assets;
        Failures = failures;
        ArchiveCount = archiveCount;
    }

    public string RootPath { get; }
    public IReadOnlyList<ClientAssetRecord> Assets { get; }
    public IReadOnlyList<ClientScanFailure> Failures { get; }
    public int ArchiveCount { get; }
}
