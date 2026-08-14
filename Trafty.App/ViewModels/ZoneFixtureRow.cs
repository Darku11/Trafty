namespace Trafty.App.ViewModels;

/// <summary>
/// Display-ready view of one placed object in a zone map: world position plus screen
/// position (pre-projected into the map canvas' coordinate space by the view model, so the
/// view itself stays a plain data-bound Canvas with no projection math).
/// </summary>
public sealed class ZoneFixtureRow
{
    public required int Id { get; init; }
    public required string TextualName { get; init; }
    public required string? NifFileName { get; init; }
    public required double WorldX { get; init; }
    public required double WorldY { get; init; }
    public required double CanvasX { get; init; }
    public required double CanvasY { get; init; }

    public string InfoDisplay => NifFileName is null
        ? $"{TextualName} (unresolved model)"
        : $"{TextualName} — {NifFileName}";
}
