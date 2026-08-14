using Trafty.Core.UI;

namespace Trafty.App.ViewModels;

/// <summary>Display-ready view of one control inside a UI window preview's side list.</summary>
public sealed class UiControlRow
{
    public required string Kind { get; init; }
    public required string? ControlId { get; init; }
    public required string? Label { get; init; }
    public required int? X { get; init; }
    public required int? Y { get; init; }

    public string SummaryDisplay
    {
        get
        {
            string id = ControlId is null ? "" : $"#{ControlId} ";
            string label = Label is null ? "" : $"\"{Label}\" ";
            string pos = X is null || Y is null ? "" : $"({X}, {Y})";
            return $"{id}{label}{pos}".Trim();
        }
    }

    public static UiControlRow FromControl(DaocControlDef control) => new()
    {
        Kind = control.Kind,
        ControlId = control.ControlId,
        Label = control.Label,
        X = control.X,
        Y = control.Y,
    };
}
