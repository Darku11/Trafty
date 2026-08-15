namespace Trafty.App.Guides;

/// <summary>
/// <paramref name="SigilGeometry"/> is Avalonia/SVG mini-language path data for a small
/// vector icon (24x24 viewbox) evoking the guide's race or craft — a leaf for an elf, an
/// anvil for a dwarf, and so on. Kept as plain path data (straight-line polygons only, no
/// arcs/curves) rather than image assets so the guide roster stays self-contained until
/// real illustrated portraits replace them.
/// </summary>
public sealed record GuideProfile(
    string Name,
    string Role,
    string Sigil,
    string SigilGeometry,
    string AccentHex,
    string Message,
    string Quip);
