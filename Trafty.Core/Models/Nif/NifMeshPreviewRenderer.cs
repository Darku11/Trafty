using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Trafty.Core.Models.Nif;

/// <summary>
/// Renders a flattened world-space mesh (see <see cref="NifSceneMesh"/>) to a 3/4-view
/// preview image at a caller-chosen rotation. No 3D library is used — Trafty stays
/// deliberately dependency-light (see project conventions), so this is a small hand-rolled
/// software rasterizer: orthographic projection, flat per-triangle shading from a fixed
/// light direction, back-face culling, and a per-pixel z-buffer for correct occlusion (an
/// earlier version used painter's-algorithm depth sorting instead, which got visibly wrong
/// on self-intersecting/non-convex parts of a real model). The App re-renders on every
/// mouse-drag step to let the user rotate the model (~90ms for the biggest test model at
/// 320x320 — not smooth 60fps, but fine for a preview).
/// </summary>
public static class NifMeshPreviewRenderer
{
    public const float DefaultRotationYDegrees = 35f;
    public const float DefaultRotationXDegrees = -25f;
    private static readonly (float X, float Y, float Z) LightDirection = Normalize((0.4f, 0.6f, 0.7f));

    public static Image<Rgba32> Render(
        IReadOnlyList<NifWorldTriangle> triangles,
        int width,
        int height,
        float rotationYDegrees = DefaultRotationYDegrees,
        float rotationXDegrees = DefaultRotationXDegrees)
    {
        var background = new Rgba32(26, 26, 26, 255);
        var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                accessor.GetRowSpan(y).Fill(background);
            }
        });

        if (triangles.Count == 0)
        {
            return image;
        }

        var viewTriangles = new List<((float X, float Y, float Z) A, (float X, float Y, float Z) B, (float X, float Y, float Z) C)>(triangles.Count);

        foreach (NifWorldTriangle t in triangles)
        {
            viewTriangles.Add((
                Rotate(t.A, rotationYDegrees, rotationXDegrees),
                Rotate(t.B, rotationYDegrees, rotationXDegrees),
                Rotate(t.C, rotationYDegrees, rotationXDegrees)));
        }

        (float MinX, float MaxX, float MinY, float MaxY) bounds = ComputeBounds(viewTriangles);
        float spanX = Math.Max(bounds.MaxX - bounds.MinX, 1e-3f);
        float spanY = Math.Max(bounds.MaxY - bounds.MinY, 1e-3f);
        float margin = 0.9f;
        float scale = margin * Math.Min(width / spanX, height / spanY);

        (float X, float Y) Project((float X, float Y, float Z) v) => (
            (v.X - bounds.MinX) * scale + (width - spanX * scale) / 2f,
            height - ((v.Y - bounds.MinY) * scale + (height - spanY * scale) / 2f)); // screen Y is flipped

        // Camera looks toward -Z, so larger Z is closer to the camera — the z-buffer keeps
        // whichever fragment has the greatest Z at each pixel. float.MinValue marks "nothing
        // drawn here yet" so any real fragment always wins the first comparison.
        float[] depthBuffer = new float[width * height];
        Array.Fill(depthBuffer, float.MinValue);

        foreach (var t in viewTriangles)
        {
            (float X, float Y, float Z) normal = FaceNormal(t.A, t.B, t.C);

            if (normal.Z <= 0)
            {
                continue; // back-facing relative to the camera looking down -Z; cull it
            }

            float intensity = Math.Clamp(Dot(normal, LightDirection), 0.2f, 1f);
            byte level = (byte)(60 + intensity * 150);
            var color = new Rgba32(level, level, (byte)Math.Min(255, level + 20), 255);

            FillTriangle(image, depthBuffer, Project(t.A), Project(t.B), Project(t.C), t.A.Z, t.B.Z, t.C.Z, color);
        }

        return image;
    }

    public static void SaveAsPng(
        IReadOnlyList<NifWorldTriangle> triangles,
        int width,
        int height,
        Stream destination,
        float rotationYDegrees = DefaultRotationYDegrees,
        float rotationXDegrees = DefaultRotationXDegrees)
    {
        using Image<Rgba32> image = Render(triangles, width, height, rotationYDegrees, rotationXDegrees);
        image.SaveAsPng(destination);
    }

    private static (float MinX, float MaxX, float MinY, float MaxY) ComputeBounds(
        List<((float X, float Y, float Z) A, (float X, float Y, float Z) B, (float X, float Y, float Z) C)> triangles)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;

        foreach (var t in triangles)
        {
            foreach ((float X, float Y, float Z) v in new[] { t.A, t.B, t.C })
            {
                minX = Math.Min(minX, v.X);
                maxX = Math.Max(maxX, v.X);
                minY = Math.Min(minY, v.Y);
                maxY = Math.Max(maxY, v.Y);
            }
        }

        return (minX, maxX, minY, maxY);
    }

    private static (float X, float Y, float Z) Rotate((float X, float Y, float Z) v, float rotationYDegrees, float rotationXDegrees)
    {
        float yRad = rotationYDegrees * MathF.PI / 180f;
        float xRad = rotationXDegrees * MathF.PI / 180f;

        // Rotate around Y first (turn to a 3/4 view), then around X (tilt down).
        float x1 = v.X * MathF.Cos(yRad) + v.Z * MathF.Sin(yRad);
        float z1 = -v.X * MathF.Sin(yRad) + v.Z * MathF.Cos(yRad);
        float y1 = v.Y;

        float y2 = y1 * MathF.Cos(xRad) - z1 * MathF.Sin(xRad);
        float z2 = y1 * MathF.Sin(xRad) + z1 * MathF.Cos(xRad);

        return (x1, y2, z2);
    }

    private static (float X, float Y, float Z) FaceNormal(
        (float X, float Y, float Z) a, (float X, float Y, float Z) b, (float X, float Y, float Z) c)
    {
        (float X, float Y, float Z) ab = (b.X - a.X, b.Y - a.Y, b.Z - a.Z);
        (float X, float Y, float Z) ac = (c.X - a.X, c.Y - a.Y, c.Z - a.Z);
        (float X, float Y, float Z) cross = (
            ab.Y * ac.Z - ab.Z * ac.Y,
            ab.Z * ac.X - ab.X * ac.Z,
            ab.X * ac.Y - ab.Y * ac.X);

        return Normalize(cross);
    }

    private static (float X, float Y, float Z) Normalize((float X, float Y, float Z) v)
    {
        float length = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return length < 1e-6f ? (0, 0, 1) : (v.X / length, v.Y / length, v.Z / length);
    }

    private static float Dot((float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>
    /// Fills a screen-space triangle using barycentric coordinates over its bounding box,
    /// testing each pixel's interpolated depth against <paramref name="depthBuffer"/> before
    /// writing — the actual z-buffer occlusion test.
    /// </summary>
    private static void FillTriangle(
        Image<Rgba32> image,
        float[] depthBuffer,
        (float X, float Y) a,
        (float X, float Y) b,
        (float X, float Y) c,
        float depthA,
        float depthB,
        float depthC,
        Rgba32 color)
    {
        int width = image.Width;
        int minX = Math.Max(0, (int)MathF.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
        int maxX = Math.Min(width - 1, (int)MathF.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
        int minY = Math.Max(0, (int)MathF.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
        int maxY = Math.Min(image.Height - 1, (int)MathF.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));

        if (minX > maxX || minY > maxY)
        {
            return;
        }

        float denom = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);

        if (MathF.Abs(denom) < 1e-6f)
        {
            return; // degenerate (zero-area) triangle
        }

        image.ProcessPixelRows(accessor =>
        {
            for (int y = minY; y <= maxY; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                int rowOffset = y * width;

                for (int x = minX; x <= maxX; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;

                    float w1 = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / denom;
                    float w2 = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / denom;
                    float w3 = 1 - w1 - w2;

                    if (w1 >= 0 && w2 >= 0 && w3 >= 0)
                    {
                        float depth = w1 * depthA + w2 * depthB + w3 * depthC;
                        int bufferIndex = rowOffset + x;

                        if (depth > depthBuffer[bufferIndex])
                        {
                            depthBuffer[bufferIndex] = depth;
                            row[x] = color;
                        }
                    }
                }
            }
        });
    }
}
