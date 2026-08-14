namespace Trafty.Core.Models.Nif;

/// <summary>Base type for every parsed NIF block, keyed by its position in the file's block list.</summary>
public abstract class NifBlock
{
    public required int BlockIndex { get; init; }
    public required string TypeName { get; init; }
}

/// <summary>Fields shared by every block derived from NiObjectNET (name/extra data/controller).</summary>
public abstract class NiObjectNetBlock : NifBlock
{
    public required string Name { get; init; }
    public required int ExtraDataRef { get; init; }
    public required int ControllerRef { get; init; }
}

/// <summary>Fields shared by every block derived from NiAVObject (transform, properties, bounds).</summary>
public abstract class NiAvObjectBlock : NiObjectNetBlock
{
    public required ushort Flags { get; init; }
    public required (float X, float Y, float Z) Translation { get; init; }
    public required float[] Rotation { get; init; }
    public required float Scale { get; init; }
    public required IReadOnlyList<int> PropertyRefs { get; init; }
}

public class NiNodeBlock : NiAvObjectBlock
{
    public required IReadOnlyList<int> ChildRefs { get; init; }
    public required IReadOnlyList<int> EffectRefs { get; init; }
}

public sealed class NiLodNodeBlock : NiNodeBlock
{
    public required uint SwitchIndex { get; init; }
    public required (float X, float Y, float Z) LodCenter { get; init; }
    public required IReadOnlyList<(float Near, float Far)> LodLevels { get; init; }
}

public sealed class NiTriShapeBlock : NiAvObjectBlock
{
    public required int DataRef { get; init; }
    public required int SkinInstanceRef { get; init; }
}

/// <summary>
/// The actual mesh geometry: vertex positions, optional normals/vertex colors, UV sets, and
/// the triangle index list. This is the payload a 3D preview would render.
/// </summary>
public sealed class NiTriShapeDataBlock : NifBlock
{
    public required IReadOnlyList<(float X, float Y, float Z)> Vertices { get; init; }
    public required IReadOnlyList<(float X, float Y, float Z)>? Normals { get; init; }
    public required IReadOnlyList<(float R, float G, float B, float A)>? VertexColors { get; init; }
    public required IReadOnlyList<IReadOnlyList<(float U, float V)>> UvSets { get; init; }
    public required IReadOnlyList<(ushort V1, ushort V2, ushort V3)> Triangles { get; init; }
}

public sealed class NiMaterialPropertyBlock : NiObjectNetBlock
{
    public required (float R, float G, float B) AmbientColor { get; init; }
    public required (float R, float G, float B) DiffuseColor { get; init; }
    public required (float R, float G, float B) SpecularColor { get; init; }
    public required (float R, float G, float B) EmissiveColor { get; init; }
    public required float Glossiness { get; init; }
    public required float Alpha { get; init; }
}

public sealed class NiTexturingPropertyBlock : NiObjectNetBlock
{
    /// <summary>Resolved NiSourceTexture refs for each texture slot that was present, keyed by slot name.</summary>
    public required IReadOnlyDictionary<string, int> TextureSlotSourceRefs { get; init; }
}

public sealed class NiSourceTextureBlock : NiObjectNetBlock
{
    public required bool IsExternal { get; init; }
    public required string? FileName { get; init; }
}

public sealed class NiVertexColorPropertyBlock : NiObjectNetBlock;

public sealed class NiZBufferPropertyBlock : NiObjectNetBlock;

public sealed class NiDitherPropertyBlock : NiObjectNetBlock;
