namespace Trafty.Core.UI;

/// <summary>
/// One control inside a DAoC UI WindowTemplate — a &lt;ButtonDef&gt;, &lt;LabelDef&gt;,
/// &lt;FullResizeImageDef&gt;, etc. There are many control element types across the client's
/// UI XML (only a handful are confirmed from real files: ButtonDef, LabelDef,
/// FullResizeImageDef, ChatControlDef, InvisibleButtonDef, HorizontalResizeImageButtonDef),
/// so rather than hardcoding a class per kind, this captures the element name as
/// <see cref="Kind"/> plus every direct child element as a name/value pair in
/// <see cref="Properties"/> — nothing is lost even for control kinds this project hasn't
/// seen yet. The handful of fields common across every kind seen so far (position, size,
/// control id, label, template name) are pulled out for convenience.
/// </summary>
public sealed class DaocControlDef
{
    /// <summary>XML element name, e.g. "ButtonDef".</summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Identifier for this control — usually numeric ("0", "1000") but sometimes a named
    /// constant ("Background", "TwoWayResizeTopRight"), so kept as text rather than parsed.
    /// </summary>
    public string? ControlId { get; init; }

    public int? X { get; init; }
    public int? Y { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Label { get; init; }
    public string? TemplateName { get; init; }
    public string? OnClickEvent { get; init; }

    /// <summary>Every direct child element's text, keyed by element name (case as in the file).</summary>
    public required IReadOnlyDictionary<string, string> Properties { get; init; }
}
