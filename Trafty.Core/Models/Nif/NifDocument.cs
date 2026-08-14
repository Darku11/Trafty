namespace Trafty.Core.Models.Nif;

/// <summary>
/// Parses the full block list of a .nif file at format version 4.2.2.0 — not just the header
/// (see <see cref="NifHeader"/> for that). Field layouts below were taken from the public
/// NifTools nif.xml specification (https://github.com/niftools/nifxml), filtered to exactly
/// what applies at version 4.2.2.0 (many fields in that spec are gated to later format
/// versions and don't appear in a file this old) — not reverse-engineered, since this is a
/// long-documented open format, consistent with how <see cref="NifHeader"/> already treats it.
///
/// At this format version, blocks have no offset table and no per-block size field — each is
/// just a length-prefixed type name followed immediately by that type's fields, back to back,
/// with the next block starting wherever the previous one's fields end. That means a block
/// type whose layout isn't implemented here can't be safely skipped — there is nothing to skip
/// by, only to parse exactly right. Unsupported block types therefore throw rather than guess.
///
/// Only the block types found in this project's real test file are implemented (verified by a
/// zero-byte-remainder check after parsing every block plus the trailing root-list footer):
/// NiNode, NiLODNode, NiTriShape, NiTriShapeData, NiMaterialProperty, NiTexturingProperty,
/// NiSourceTexture, NiVertexColorProperty, NiZBufferProperty, NiDitherProperty.
/// </summary>
public sealed class NifDocument
{
    public required NifHeader Header { get; init; }
    public required IReadOnlyList<NifBlock> Blocks { get; init; }

    /// <summary>Root block indices from the file footer (usually just the top-level NiNode).</summary>
    public required IReadOnlyList<int> RootRefs { get; init; }

    public static NifDocument Parse(byte[] data)
    {
        NifHeader header = NifHeader.Parse(data);
        var reader = new NifByteReader(data, header.BlocksStartAt);

        var blocks = new List<NifBlock>((int)header.BlockCount);

        for (int i = 0; i < header.BlockCount; i++)
        {
            string typeName = reader.ReadString();
            blocks.Add(ParseBlock(ref reader, i, typeName));
        }

        uint numRoots = reader.ReadUInt32();
        var roots = new List<int>((int)numRoots);

        for (int i = 0; i < numRoots; i++)
        {
            roots.Add(reader.ReadRef());
        }

        if (reader.Remaining != 0)
        {
            throw new ModelFormatException(
                $"{reader.Remaining} byte(s) left over after parsing all {header.BlockCount} block(s) " +
                "and the root list — a block layout above doesn't match this file exactly.");
        }

        return new NifDocument { Header = header, Blocks = blocks, RootRefs = roots };
    }

    public static NifDocument Load(string path) => Parse(File.ReadAllBytes(path));

    private static NifBlock ParseBlock(ref NifByteReader reader, int index, string typeName) => typeName switch
    {
        "NiNode" => ParseNiNode(ref reader, index, typeName),
        "NiLODNode" => ParseNiLodNode(ref reader, index, typeName),
        "NiTriShape" => ParseNiTriShape(ref reader, index, typeName),
        "NiTriShapeData" => ParseNiTriShapeData(ref reader, index, typeName),
        "NiMaterialProperty" => ParseNiMaterialProperty(ref reader, index, typeName),
        "NiTexturingProperty" => ParseNiTexturingProperty(ref reader, index, typeName),
        "NiSourceTexture" => ParseNiSourceTexture(ref reader, index, typeName),
        "NiVertexColorProperty" => ParseNiVertexColorProperty(ref reader, index, typeName),
        "NiZBufferProperty" => ParseNiZBufferProperty(ref reader, index, typeName),
        "NiDitherProperty" => ParseNiDitherProperty(ref reader, index, typeName),
        _ => throw new ModelFormatException(
            $"Block {index} has unsupported type \"{typeName}\" — no verified field layout implemented for it."),
    };

    private static NiVertexColorPropertyBlock ParseNiVertexColorProperty(ref NifByteReader reader, int index, string typeName)
    {
        ObjectNetFields objectNet = ParseNiObjectNet(ref reader);
        reader.ReadUInt16(); // Flags
        reader.ReadUInt32(); // Vertex Mode
        reader.ReadUInt32(); // Lighting Mode

        return new NiVertexColorPropertyBlock
        {
            BlockIndex = index,
            TypeName = typeName,
            Name = objectNet.Name,
            ExtraDataRef = objectNet.ExtraDataRef,
            ControllerRef = objectNet.ControllerRef,
        };
    }

    private static NiZBufferPropertyBlock ParseNiZBufferProperty(ref NifByteReader reader, int index, string typeName)
    {
        ObjectNetFields objectNet = ParseNiObjectNet(ref reader);
        reader.ReadUInt16(); // Flags
        reader.ReadUInt32(); // Function

        return new NiZBufferPropertyBlock
        {
            BlockIndex = index,
            TypeName = typeName,
            Name = objectNet.Name,
            ExtraDataRef = objectNet.ExtraDataRef,
            ControllerRef = objectNet.ControllerRef,
        };
    }

    private static NiDitherPropertyBlock ParseNiDitherProperty(ref NifByteReader reader, int index, string typeName)
    {
        ObjectNetFields objectNet = ParseNiObjectNet(ref reader);
        reader.ReadUInt16(); // Flags

        return new NiDitherPropertyBlock
        {
            BlockIndex = index,
            TypeName = typeName,
            Name = objectNet.Name,
            ExtraDataRef = objectNet.ExtraDataRef,
            ControllerRef = objectNet.ControllerRef,
        };
    }

    private readonly record struct ObjectNetFields(string Name, int ExtraDataRef, int ControllerRef);

    private static ObjectNetFields ParseNiObjectNet(ref NifByteReader reader)
    {
        string name = reader.ReadString();
        int extraDataRef = reader.ReadRef();
        int controllerRef = reader.ReadRef();
        return new ObjectNetFields(name, extraDataRef, controllerRef);
    }

    private readonly record struct AvObjectFields(
        ObjectNetFields ObjectNet,
        ushort Flags,
        (float X, float Y, float Z) Translation,
        float[] Rotation,
        float Scale,
        IReadOnlyList<int> PropertyRefs);

    private static AvObjectFields ParseNiAvObject(ref NifByteReader reader)
    {
        ObjectNetFields objectNet = ParseNiObjectNet(ref reader);
        ushort flags = reader.ReadUInt16();
        (float, float, float) translation = reader.ReadVector3();
        float[] rotation = reader.ReadMatrix33();
        float scale = reader.ReadSingle();
        reader.ReadVector3(); // Velocity — deprecated, always (0,0,0) at this version, not kept

        uint numProperties = reader.ReadUInt32();
        var properties = new List<int>((int)numProperties);

        for (int i = 0; i < numProperties; i++)
        {
            properties.Add(reader.ReadRef());
        }

        bool hasBoundingVolume = reader.ReadBool();

        if (hasBoundingVolume)
        {
            int collisionType = reader.ReadInt32();

            if (collisionType != 0)
            {
                throw new ModelFormatException(
                    $"Bounding volume collision type {collisionType} is not implemented (only sphere/type 0 is verified).");
            }

            reader.SkipNiBound();
        }

        return new AvObjectFields(objectNet, flags, translation, rotation, scale, properties);
    }

    private static NiNodeBlock ParseNiNode(ref NifByteReader reader, int index, string typeName)
    {
        AvObjectFields av = ParseNiAvObject(ref reader);
        (List<int> children, List<int> effects) = ParseNiNodeChildrenAndEffects(ref reader);

        return new NiNodeBlock
        {
            BlockIndex = index,
            TypeName = typeName,
            Name = av.ObjectNet.Name,
            ExtraDataRef = av.ObjectNet.ExtraDataRef,
            ControllerRef = av.ObjectNet.ControllerRef,
            Flags = av.Flags,
            Translation = av.Translation,
            Rotation = av.Rotation,
            Scale = av.Scale,
            PropertyRefs = av.PropertyRefs,
            ChildRefs = children,
            EffectRefs = effects,
        };
    }

    private static (List<int> Children, List<int> Effects) ParseNiNodeChildrenAndEffects(ref NifByteReader reader)
    {
        uint numChildren = reader.ReadUInt32();
        var children = new List<int>((int)numChildren);

        for (int i = 0; i < numChildren; i++)
        {
            children.Add(reader.ReadRef());
        }

        uint numEffects = reader.ReadUInt32();
        var effects = new List<int>((int)numEffects);

        for (int i = 0; i < numEffects; i++)
        {
            effects.Add(reader.ReadRef());
        }

        return (children, effects);
    }

    private static NiLodNodeBlock ParseNiLodNode(ref NifByteReader reader, int index, string typeName)
    {
        AvObjectFields av = ParseNiAvObject(ref reader);
        (List<int> children, List<int> effects) = ParseNiNodeChildrenAndEffects(ref reader);

        uint switchIndex = reader.ReadUInt32(); // NiSwitchNode.Index (Switch Node Flags not present at this version)
        (float, float, float) lodCenter = reader.ReadVector3();
        uint numLodLevels = reader.ReadUInt32();
        var lodLevels = new List<(float, float)>((int)numLodLevels);

        for (int i = 0; i < numLodLevels; i++)
        {
            lodLevels.Add((reader.ReadSingle(), reader.ReadSingle())); // LODRange: Near/Far Extent
        }

        return new NiLodNodeBlock
        {
            BlockIndex = index,
            TypeName = typeName,
            Name = av.ObjectNet.Name,
            ExtraDataRef = av.ObjectNet.ExtraDataRef,
            ControllerRef = av.ObjectNet.ControllerRef,
            Flags = av.Flags,
            Translation = av.Translation,
            Rotation = av.Rotation,
            Scale = av.Scale,
            PropertyRefs = av.PropertyRefs,
            ChildRefs = children,
            EffectRefs = effects,
            SwitchIndex = switchIndex,
            LodCenter = lodCenter,
            LodLevels = lodLevels,
        };
    }

    private static NiTriShapeBlock ParseNiTriShape(ref NifByteReader reader, int index, string typeName)
    {
        AvObjectFields av = ParseNiAvObject(ref reader);
        int dataRef = reader.ReadRef();       // NiGeometry.Data
        int skinInstanceRef = reader.ReadRef(); // NiGeometry.SkinInstance

        return new NiTriShapeBlock
        {
            BlockIndex = index,
            TypeName = typeName,
            Name = av.ObjectNet.Name,
            ExtraDataRef = av.ObjectNet.ExtraDataRef,
            ControllerRef = av.ObjectNet.ControllerRef,
            Flags = av.Flags,
            Translation = av.Translation,
            Rotation = av.Rotation,
            Scale = av.Scale,
            PropertyRefs = av.PropertyRefs,
            DataRef = dataRef,
            SkinInstanceRef = skinInstanceRef,
        };
    }

    private static NiTriShapeDataBlock ParseNiTriShapeData(ref NifByteReader reader, int index, string typeName)
    {
        // NiGeometryData
        ushort numVertices = reader.ReadUInt16();
        bool hasVertices = reader.ReadBool();
        var vertices = new List<(float, float, float)>(hasVertices ? numVertices : 0);

        if (hasVertices)
        {
            for (int i = 0; i < numVertices; i++)
            {
                vertices.Add(reader.ReadVector3());
            }
        }

        bool hasNormals = reader.ReadBool();
        List<(float, float, float)>? normals = null;

        if (hasNormals)
        {
            normals = new List<(float, float, float)>(numVertices);

            for (int i = 0; i < numVertices; i++)
            {
                normals.Add(reader.ReadVector3());
            }
        }

        reader.SkipNiBound(); // Bounding Sphere

        bool hasVertexColors = reader.ReadBool();
        List<(float, float, float, float)>? vertexColors = null;

        if (hasVertexColors)
        {
            vertexColors = new List<(float, float, float, float)>(numVertices);

            for (int i = 0; i < numVertices; i++)
            {
                vertexColors.Add(reader.ReadColor4());
            }
        }

        ushort dataFlags = reader.ReadUInt16(); // low 6 bits = number of UV sets
        int numUvSets = dataFlags & 0x3F;
        var uvSets = new List<IReadOnlyList<(float, float)>>(numUvSets);

        for (int set = 0; set < numUvSets; set++)
        {
            var uvs = new List<(float, float)>(numVertices);

            for (int i = 0; i < numVertices; i++)
            {
                uvs.Add(reader.ReadTexCoord());
            }

            uvSets.Add(uvs);
        }

        // NiTriBasedGeomData
        ushort numTriangles = reader.ReadUInt16();

        // NiTriShapeData
        reader.ReadUInt32(); // Num Triangle Points (always Num Triangles * 3, not separately kept)
        var triangles = new List<(ushort, ushort, ushort)>(numTriangles);

        for (int i = 0; i < numTriangles; i++)
        {
            triangles.Add(reader.ReadTriangle());
        }

        ushort numMatchGroups = reader.ReadUInt16();

        for (int g = 0; g < numMatchGroups; g++)
        {
            ushort groupSize = reader.ReadUInt16();
            reader.Skip(groupSize * 2); // ushort vertex indices, not kept
        }

        return new NiTriShapeDataBlock
        {
            BlockIndex = index,
            TypeName = typeName,
            Vertices = vertices,
            Normals = normals,
            VertexColors = vertexColors,
            UvSets = uvSets,
            Triangles = triangles,
        };
    }

    private static NiMaterialPropertyBlock ParseNiMaterialProperty(ref NifByteReader reader, int index, string typeName)
    {
        ObjectNetFields objectNet = ParseNiObjectNet(ref reader);
        reader.ReadUInt16(); // Flags
        (float, float, float) ambient = reader.ReadColor3();
        (float, float, float) diffuse = reader.ReadColor3();
        (float, float, float) specular = reader.ReadColor3();
        (float, float, float) emissive = reader.ReadColor3();
        float glossiness = reader.ReadSingle();
        float alpha = reader.ReadSingle();

        return new NiMaterialPropertyBlock
        {
            BlockIndex = index,
            TypeName = typeName,
            Name = objectNet.Name,
            ExtraDataRef = objectNet.ExtraDataRef,
            ControllerRef = objectNet.ControllerRef,
            AmbientColor = ambient,
            DiffuseColor = diffuse,
            SpecularColor = specular,
            EmissiveColor = emissive,
            Glossiness = glossiness,
            Alpha = alpha,
        };
    }

    private static readonly string[] TextureSlotNames =
    {
        "Base", "Dark", "Detail", "Gloss", "Glow", "BumpMap", "Decal0", "Decal1", "Decal2", "Decal3",
    };

    private static NiTexturingPropertyBlock ParseNiTexturingProperty(ref NifByteReader reader, int index, string typeName)
    {
        ObjectNetFields objectNet = ParseNiObjectNet(ref reader);
        reader.ReadUInt16(); // Flags
        reader.ReadUInt32(); // Apply Mode
        uint textureCount = reader.ReadUInt32();

        var slotRefs = new Dictionary<string, int>();

        for (int slot = 0; slot < 5; slot++) // Base, Dark, Detail, Gloss, Glow — always present
        {
            if (reader.ReadBool())
            {
                slotRefs[TextureSlotNames[slot]] = ReadTexDescSourceRef(ref reader);
            }
        }

        if (textureCount > 5 && reader.ReadBool()) // Bump Map
        {
            slotRefs["BumpMap"] = ReadTexDescSourceRef(ref reader);
            reader.ReadSingle(); // Bump Map Luma Scale
            reader.ReadSingle(); // Bump Map Luma Offset
            reader.Skip(4 * 4);  // Bump Map Matrix (Matrix22, 4 floats)
        }

        for (int decal = 0; decal < 4; decal++) // Decal0..Decal3, each gated on textureCount > 6+decal
        {
            if (textureCount > 6 + decal && reader.ReadBool())
            {
                slotRefs[TextureSlotNames[6 + decal]] = ReadTexDescSourceRef(ref reader);
            }
        }

        return new NiTexturingPropertyBlock
        {
            BlockIndex = index,
            TypeName = typeName,
            Name = objectNet.Name,
            ExtraDataRef = objectNet.ExtraDataRef,
            ControllerRef = objectNet.ControllerRef,
            TextureSlotSourceRefs = slotRefs,
        };
    }

    /// <summary>Reads a TexDesc struct at this format version and returns just its Source ref.</summary>
    private static int ReadTexDescSourceRef(ref NifByteReader reader)
    {
        int sourceRef = reader.ReadRef();
        reader.ReadUInt32(); // Clamp Mode
        reader.ReadUInt32(); // Filter Mode
        reader.ReadUInt32(); // UV Set
        reader.ReadInt16();  // PS2 L
        reader.ReadInt16();  // PS2 K
        return sourceRef;
    }

    private static NiSourceTextureBlock ParseNiSourceTexture(ref NifByteReader reader, int index, string typeName)
    {
        ObjectNetFields objectNet = ParseNiObjectNet(ref reader);
        bool useExternal = reader.ReadByte() != 0;
        bool useInternal = false;

        if (!useExternal)
        {
            useInternal = reader.ReadByte() != 0;
        }

        string? fileName = null;

        if (useExternal)
        {
            fileName = reader.ReadString();
        }

        if (!useExternal && useInternal)
        {
            reader.ReadRef(); // Pixel Data — embedded pixel data not extracted here
        }

        reader.Skip(4 * 3); // FormatPrefs: Pixel Layout, Use Mipmaps, Alpha Format (3 x uint)
        reader.ReadByte();  // Is Static

        return new NiSourceTextureBlock
        {
            BlockIndex = index,
            TypeName = typeName,
            Name = objectNet.Name,
            ExtraDataRef = objectNet.ExtraDataRef,
            ControllerRef = objectNet.ControllerRef,
            IsExternal = useExternal,
            FileName = fileName,
        };
    }
}
