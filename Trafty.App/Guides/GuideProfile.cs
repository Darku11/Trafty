namespace Trafty.App.Guides;

public sealed record GuideProfile(
    string Name,
    string Role,
    string Sigil,
    string AccentHex,
    string Message,
    string Quip);
