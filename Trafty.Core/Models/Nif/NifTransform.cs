namespace Trafty.Core.Models.Nif;

/// <summary>
/// A NIF scene-graph node's local transform (translation, rotation, uniform scale), and the
/// standard Gamebryo rule for composing a child's local transform with its parent's world
/// transform to get the child's world transform. Needed to place a NiTriShape's vertices
/// correctly when a model is built from many nodes (1struinedtemple.NIF has 56 NiNodes and
/// 68 NiTriShapes under a single "Scene Root").
/// </summary>
public readonly struct NifTransform
{
    public required (float X, float Y, float Z) Translation { get; init; }

    /// <summary>Row-major 3x3 rotation matrix, 9 floats.</summary>
    public required float[] Rotation { get; init; }

    public required float Scale { get; init; }

    public static NifTransform Identity { get; } = new()
    {
        Translation = (0, 0, 0),
        Rotation = [1, 0, 0, 0, 1, 0, 0, 0, 1],
        Scale = 1f,
    };

    /// <summary>
    /// Composes this (parent) transform with a child's local transform:
    /// worldT = parentT + parentR * (parentS * childT), worldR = parentR * childR,
    /// worldS = parentS * childS.
    /// </summary>
    public NifTransform Compose(NifTransform child)
    {
        (float X, float Y, float Z) scaledChildT = (
            child.Translation.X * Scale,
            child.Translation.Y * Scale,
            child.Translation.Z * Scale);

        (float X, float Y, float Z) rotatedChildT = MultiplyMatrixVector(Rotation, scaledChildT);

        (float X, float Y, float Z) worldTranslation = (
            Translation.X + rotatedChildT.X,
            Translation.Y + rotatedChildT.Y,
            Translation.Z + rotatedChildT.Z);

        float[] worldRotation = MultiplyMatrixMatrix(Rotation, child.Rotation);

        return new NifTransform
        {
            Translation = worldTranslation,
            Rotation = worldRotation,
            Scale = Scale * child.Scale,
        };
    }

    /// <summary>Transforms a local-space point into the space this transform represents.</summary>
    public (float X, float Y, float Z) TransformPoint((float X, float Y, float Z) point)
    {
        (float X, float Y, float Z) scaled = (point.X * Scale, point.Y * Scale, point.Z * Scale);
        (float X, float Y, float Z) rotated = MultiplyMatrixVector(Rotation, scaled);

        return (Translation.X + rotated.X, Translation.Y + rotated.Y, Translation.Z + rotated.Z);
    }

    private static (float X, float Y, float Z) MultiplyMatrixVector(float[] m, (float X, float Y, float Z) v) => (
        m[0] * v.X + m[1] * v.Y + m[2] * v.Z,
        m[3] * v.X + m[4] * v.Y + m[5] * v.Z,
        m[6] * v.X + m[7] * v.Y + m[8] * v.Z);

    private static float[] MultiplyMatrixMatrix(float[] a, float[] b)
    {
        var result = new float[9];

        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                float sum = 0;

                for (int k = 0; k < 3; k++)
                {
                    sum += a[row * 3 + k] * b[k * 3 + col];
                }

                result[row * 3 + col] = sum;
            }
        }

        return result;
    }
}
