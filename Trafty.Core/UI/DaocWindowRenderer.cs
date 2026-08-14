using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Trafty.Core.UI;

/// <summary>
/// Renders a schematic layout preview of a <see cref="DaocWindowTemplate"/>: the window
/// outline, its title bar, every control with a known position/size as a colored rectangle,
/// and (when a font is available on the running machine — see <see cref="TryGetFont"/>) the
/// window name and each control's label as text. Still does not render the actual
/// button/background textures — that would need a TGA/DDS-to-screen compositing pass this
/// preview doesn't attempt — but a structural layout with real labels (are controls where
/// the XML says they are, do they overlap, is the window the right size, what does each one
/// actually say) is what's useful for checking a window definition.
/// </summary>
public static class DaocWindowRenderer
{
    private static readonly Rgba32[] KindPalette =
    {
        new(90, 140, 200, 200),  // blues/teals for structural + interactive kinds, cycled by hash
        new(200, 150, 80, 200),
        new(120, 180, 100, 200),
        new(180, 100, 160, 200),
        new(200, 190, 90, 200),
    };

    public static Image<Rgba32> Render(DaocWindowTemplate window, int padding = 12)
    {
        int canvasWidth = window.Width + padding * 2;
        int canvasHeight = window.Height + padding * 2;
        var image = new Image<Rgba32>(Math.Max(canvasWidth, 1), Math.Max(canvasHeight, 1));
        Font? font = TryGetFont(11);

        Fill(image, new Rgba32(20, 20, 20, 255));
        DrawRect(image, padding, padding, window.Width, window.Height, new Rgba32(45, 45, 45, 255), filled: true);

        if (window.TitleHeight > 0)
        {
            int titleWidth = window.TitleWidth > 0 ? window.TitleWidth : window.Width;
            DrawRect(image, padding, padding, titleWidth, window.TitleHeight, new Rgba32(70, 90, 110, 255), filled: true);
            DrawText(image, font, window.Name, padding + 4, padding + window.TitleHeight / 2, Rgba32.ParseHex("#F0F0F0"), verticalCenter: true);
        }

        foreach (DaocControlDef control in window.Controls)
        {
            int x = padding + (control.X ?? 0);
            int y = padding + (control.Y ?? 0);
            Rgba32 color = KindPalette[Math.Abs(control.Kind.GetHashCode()) % KindPalette.Length];
            string? label = control.Label ?? control.ControlId;

            if (control.Width is { } w && control.Height is { } h)
            {
                DrawRect(image, x, y, w, h, color, filled: true);
                DrawRect(image, x, y, w, h, new Rgba32(255, 255, 255, 120), filled: false);

                if (label is not null)
                {
                    DrawText(image, font, label, x + 3, y + h / 2, Rgba32.ParseHex("#101010"), verticalCenter: true);
                }
            }
            else
            {
                // No size in the XML — its real size comes from TemplateName (e.g.
                // "button_large"), which this project has no data for. Marking just the
                // known position, honestly, beats drawing a made-up box.
                const int markerSize = 6;
                DrawRect(image, x - markerSize / 2, y - markerSize / 2, markerSize, markerSize, color, filled: true);

                if (label is not null)
                {
                    DrawText(image, font, label, x + markerSize, y - 5, Rgba32.ParseHex("#E0E0E0"), verticalCenter: false);
                }
            }
        }

        DrawRect(image, padding, padding, window.Width, window.Height, new Rgba32(180, 180, 180, 255), filled: false);

        return image;
    }

    /// <summary>
    /// Finds any usable font installed on the running machine. Text labels are a nice-to-have
    /// on top of the structural rectangles, not something the preview depends on, so a
    /// headless machine with zero fonts installed degrades to no text rather than throwing.
    /// </summary>
    private static Font? TryGetFont(float size)
    {
        string[] preferredNames = { "Segoe UI", "Arial", "DejaVu Sans", "Liberation Sans", "FreeSans", "Noto Sans" };

        foreach (string name in preferredNames)
        {
            if (SystemFonts.Collection.TryGet(name, out FontFamily family))
            {
                return family.CreateFont(size);
            }
        }

        return SystemFonts.Collection.Families.Any()
            ? SystemFonts.Collection.Families.First().CreateFont(size)
            : null;
    }

    private static void DrawText(Image<Rgba32> image, Font? font, string text, int x, int y, Rgba32 color, bool verticalCenter)
    {
        if (font is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        var origin = new PointF(x, verticalCenter ? y - font.Size / 2 : y);
        image.Mutate(ctx => ctx.DrawText(text, font, color, origin));
    }

    public static void SaveAsPng(DaocWindowTemplate window, Stream destination)
    {
        using Image<Rgba32> image = Render(window);
        image.SaveAsPng(destination);
    }

    private static void Fill(Image<Rgba32> image, Rgba32 color) => image.ProcessPixelRows(accessor =>
    {
        for (int y = 0; y < accessor.Height; y++)
        {
            accessor.GetRowSpan(y).Fill(color);
        }
    });

    private static void DrawRect(Image<Rgba32> image, int x, int y, int width, int height, Rgba32 color, bool filled)
    {
        int x0 = Math.Max(0, x);
        int y0 = Math.Max(0, y);
        int x1 = Math.Min(image.Width - 1, x + width - 1);
        int y1 = Math.Min(image.Height - 1, y + height - 1);

        if (x0 > x1 || y0 > y1)
        {
            return;
        }

        image.ProcessPixelRows(accessor =>
        {
            for (int row = y0; row <= y1; row++)
            {
                bool isBorderRow = row == y0 || row == y1;
                Span<Rgba32> span = accessor.GetRowSpan(row);

                if (filled || isBorderRow)
                {
                    for (int col = x0; col <= x1; col++)
                    {
                        span[col] = Blend(span[col], color);
                    }
                }
                else
                {
                    span[x0] = Blend(span[x0], color);
                    span[x1] = Blend(span[x1], color);
                }
            }
        });
    }

    private static Rgba32 Blend(Rgba32 background, Rgba32 foreground)
    {
        float a = foreground.A / 255f;
        byte r = (byte)(foreground.R * a + background.R * (1 - a));
        byte g = (byte)(foreground.G * a + background.G * (1 - a));
        byte b = (byte)(foreground.B * a + background.B * (1 - a));
        return new Rgba32(r, g, b, 255);
    }
}
