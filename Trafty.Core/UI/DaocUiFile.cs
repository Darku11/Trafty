using System.Globalization;
using System.Xml.Linq;

namespace Trafty.Core.UI;

public sealed class DaocTextureDef
{
    public required string Name { get; init; }
    public required string File { get; init; }
}

public sealed class DaocImageAreaTemplate
{
    public required string Name { get; init; }
    public required string TextureName { get; init; }
    public int SizeX { get; init; }
    public int SizeY { get; init; }
    public int TopLeftX { get; init; }
    public int TopLeftY { get; init; }
}

/// <summary>
/// Reads a DAoC client UI XML file — window/HUD layout definitions (e.g. chat_window.xml,
/// command_window.xml, found in the client's UI folder). This is the client's own plain XML,
/// not a proprietary binary format, so parsing is just XML with no reverse engineering
/// involved. What needed real files to get right was the schema's inconsistencies: element
/// names are mostly PascalCase but not always (e.g. "&lt;width&gt;"/"&lt;height&gt;" appear
/// lowercase on some FullResizeImageDef/InvisibleButtonDef controls in the same file that
/// uses "&lt;Width&gt;"/"&lt;Height&gt;" elsewhere) — element lookups here are
/// case-insensitive to cope with that rather than silently missing values.
/// </summary>
public sealed class DaocUiFile
{
    public required IReadOnlyList<DaocTextureDef> Textures { get; init; }
    public required IReadOnlyList<DaocImageAreaTemplate> ImageAreaTemplates { get; init; }
    public required IReadOnlyList<DaocWindowTemplate> Windows { get; init; }

    private static readonly HashSet<string> WindowScalarFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name", "CloseButton", "MoveButton", "Width", "Height", "TitleWidth", "TitleHeight",
        "ResizeableWidth", "ResizeableHeight", "ResizeableTwoWayWidth", "ResizeableTwoWayHeight",
        "WindowId", "MinWidth", "MinHeight", "ContextTemplateName", "TabName",
    };

    public static DaocUiFile Parse(string xml)
    {
        XDocument doc;

        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new DaocUiFormatException($"Not valid XML: {ex.Message}");
        }

        XElement? root = doc.Root;

        if (root is null || !string.Equals(root.Name.LocalName, "Root_Element", StringComparison.OrdinalIgnoreCase))
        {
            throw new DaocUiFormatException("Missing \"Root_Element\" root node.");
        }

        var textures = new List<DaocTextureDef>();
        var imageAreas = new List<DaocImageAreaTemplate>();
        var windows = new List<DaocWindowTemplate>();

        foreach (XElement child in root.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "Texture":
                    textures.Add(ParseTexture(child));
                    break;
                case "ImageAreaTemplate":
                    imageAreas.Add(ParseImageAreaTemplate(child));
                    break;
                case "WindowTemplate":
                    windows.Add(ParseWindowTemplate(child));
                    break;
            }
        }

        return new DaocUiFile { Textures = textures, ImageAreaTemplates = imageAreas, Windows = windows };
    }

    public static DaocUiFile Load(string path) => Parse(File.ReadAllText(path));

    private static DaocTextureDef ParseTexture(XElement element) => new()
    {
        Name = ChildText(element, "Name") ?? throw new DaocUiFormatException("Texture is missing <Name>."),
        File = ChildText(element, "File") ?? throw new DaocUiFormatException("Texture is missing <File>."),
    };

    private static DaocImageAreaTemplate ParseImageAreaTemplate(XElement element)
    {
        XElement? size = Child(element, "Size");
        XElement? topLeft = Child(element, "TopLeft");

        return new DaocImageAreaTemplate
        {
            Name = ChildText(element, "Name") ?? throw new DaocUiFormatException("ImageAreaTemplate is missing <Name>."),
            TextureName = ChildText(element, "TextureName") ?? "",
            SizeX = size is null ? 0 : ParseInt(ChildText(size, "X")),
            SizeY = size is null ? 0 : ParseInt(ChildText(size, "Y")),
            TopLeftX = topLeft is null ? 0 : ParseInt(ChildText(topLeft, "X")),
            TopLeftY = topLeft is null ? 0 : ParseInt(ChildText(topLeft, "Y")),
        };
    }

    private static DaocWindowTemplate ParseWindowTemplate(XElement element)
    {
        var tabNames = new List<string>();
        var controls = new List<DaocControlDef>();

        foreach (XElement child in element.Elements())
        {
            if (string.Equals(child.Name.LocalName, "TabName", StringComparison.OrdinalIgnoreCase))
            {
                tabNames.Add(child.Value.Trim());
            }
            else if (!WindowScalarFieldNames.Contains(child.Name.LocalName))
            {
                controls.Add(ParseControl(child));
            }
        }

        return new DaocWindowTemplate
        {
            Name = ChildText(element, "Name") ?? throw new DaocUiFormatException("WindowTemplate is missing <Name>."),
            Width = ParseInt(ChildText(element, "Width")),
            Height = ParseInt(ChildText(element, "Height")),
            CloseButton = ParseBool(ChildText(element, "CloseButton")),
            MoveButton = ParseBool(ChildText(element, "MoveButton")),
            TitleWidth = ParseInt(ChildText(element, "TitleWidth")),
            TitleHeight = ParseInt(ChildText(element, "TitleHeight")),
            WindowId = ChildText(element, "WindowId"),
            MinWidth = TryParseInt(ChildText(element, "MinWidth")),
            MinHeight = TryParseInt(ChildText(element, "MinHeight")),
            ContextTemplateName = ChildText(element, "ContextTemplateName"),
            TabNames = tabNames,
            Controls = controls,
        };
    }

    private static DaocControlDef ParseControl(XElement element)
    {
        XElement? position = Child(element, "Position");
        var properties = new Dictionary<string, string>();

        foreach (XElement child in element.Elements())
        {
            if (!child.HasElements)
            {
                properties[child.Name.LocalName] = child.Value.Trim();
            }
        }

        return new DaocControlDef
        {
            Kind = element.Name.LocalName,
            ControlId = ChildText(element, "ControlId"),
            X = position is null ? null : TryParseInt(ChildText(position, "X")),
            Y = position is null ? null : TryParseInt(ChildText(position, "Y")),
            Width = TryParseInt(ChildText(element, "Width")),
            Height = TryParseInt(ChildText(element, "Height")),
            Label = ChildText(element, "Label"),
            TemplateName = ChildText(element, "TemplateName"),
            OnClickEvent = ChildText(element, "OnClickEvent"),
            Properties = properties,
        };
    }

    /// <summary>Finds a direct child element by name, case-insensitively (the files aren't consistent about casing).</summary>
    private static XElement? Child(XElement parent, string name) => parent.Elements()
        .FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));

    private static string? ChildText(XElement parent, string name) => Child(parent, name)?.Value.Trim();

    private static int ParseInt(string? text) => TryParseInt(text) ??
        throw new DaocUiFormatException($"Expected an integer, got \"{text}\".");

    private static int? TryParseInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

    private static bool ParseBool(string? text) => string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
}
