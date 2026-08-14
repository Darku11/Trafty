namespace Trafty.Core.Models.Nif;

/// <summary>
/// One triangle with all three vertices already transformed into scene-root (world) space.
/// </summary>
public readonly record struct NifWorldTriangle(
    (float X, float Y, float Z) A,
    (float X, float Y, float Z) B,
    (float X, float Y, float Z) C);

/// <summary>
/// Walks a <see cref="NifDocument"/>'s scene graph from its root(s), accumulating each node's
/// transform down to every NiTriShape, and returns all geometry flattened into world-space
/// triangles — what a 3D preview needs to render the whole model correctly positioned, not
/// just one shape's local vertices.
/// </summary>
public static class NifSceneMesh
{
    public static IReadOnlyList<NifWorldTriangle> Build(NifDocument doc)
    {
        var triangles = new List<NifWorldTriangle>();
        var visited = new HashSet<int>();

        foreach (int rootRef in doc.RootRefs)
        {
            Visit(doc, rootRef, NifTransform.Identity, triangles, visited);
        }

        return triangles;
    }

    private static void Visit(
        NifDocument doc, int blockRef, NifTransform parentWorld, List<NifWorldTriangle> triangles, HashSet<int> visited)
    {
        if (blockRef < 0 || blockRef >= doc.Blocks.Count || !visited.Add(blockRef))
        {
            return; // -1 (no ref) or already-visited (defends against cyclic references)
        }

        NifBlock block = doc.Blocks[blockRef];

        if (block is not NiAvObjectBlock avObject)
        {
            return;
        }

        NifTransform localTransform = new()
        {
            Translation = avObject.Translation,
            Rotation = avObject.Rotation,
            Scale = avObject.Scale,
        };

        NifTransform worldTransform = parentWorld.Compose(localTransform);

        if (block is NiTriShapeBlock triShape && triShape.DataRef >= 0 && triShape.DataRef < doc.Blocks.Count)
        {
            if (doc.Blocks[triShape.DataRef] is NiTriShapeDataBlock data)
            {
                foreach ((ushort v1, ushort v2, ushort v3) in data.Triangles)
                {
                    triangles.Add(new NifWorldTriangle(
                        worldTransform.TransformPoint(data.Vertices[v1]),
                        worldTransform.TransformPoint(data.Vertices[v2]),
                        worldTransform.TransformPoint(data.Vertices[v3])));
                }
            }
        }

        if (block is NiNodeBlock node)
        {
            foreach (int childRef in node.ChildRefs)
            {
                Visit(doc, childRef, worldTransform, triangles, visited);
            }
        }
    }
}
