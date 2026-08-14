namespace Trafty.Core.UI;

/// <summary>One &lt;WindowTemplate&gt; from a DAoC UI XML file — a single window/HUD panel definition.</summary>
public sealed class DaocWindowTemplate
{
    public required string Name { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public bool CloseButton { get; init; }
    public bool MoveButton { get; init; }
    public int TitleWidth { get; init; }
    public int TitleHeight { get; init; }
    public string? WindowId { get; init; }
    public int? MinWidth { get; init; }
    public int? MinHeight { get; init; }
    public string? ContextTemplateName { get; init; }

    /// <summary>Chat-window-style tab labels ("Main", "Broad", "Guild", ...), empty for most windows.</summary>
    public required IReadOnlyList<string> TabNames { get; init; }

    /// <summary>Every control in file order — buttons, labels, image panels, etc.</summary>
    public required IReadOnlyList<DaocControlDef> Controls { get; init; }
}
