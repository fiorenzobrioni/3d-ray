using System.Numerics;

namespace RayTracer.Geometry;

/// <summary>
/// Per-vertex tangents for a triangle mesh, in the MikkTSpace convention
/// (Mikkelsen 2008), the frame every normal-map baker writes its maps in.
///
/// <para>
/// Each face contributes its UV tangent (∂P/∂u) to each of its corners,
/// projected into the plane of that corner's normal and weighted by the
/// corner's angle. Corners are shared when they have the same position,
/// normal and texture coordinate and their faces have the same handedness
/// (the sign of the bitangent): a UV seam or a mirrored UV island keeps its
/// own tangents, as in MikkTSpace. At the hit the tangent is interpolated and
/// the bitangent rebuilt as sign · (N × T) (<see cref="SmoothTriangle"/>).
/// </para>
///
/// <para>
/// Not a byte-exact port of the reference implementation: its handling of
/// degenerate and zero-area UV triangles differs in corner cases, where a
/// baker's own output is ill-defined too. On a regular mesh the frames match.
/// </para>
/// </summary>
public static class MeshTangents
{
    private readonly record struct CornerKey(Vector3 Position, Vector3 Normal, Vector2 Uv, bool Mirrored);

    /// <summary>
    /// Computes and assigns vertex tangents to every <see cref="SmoothTriangle"/>
    /// with texture coordinates in <paramref name="triangles"/>. Other
    /// primitives are left as they are.
    /// </summary>
    public static void Generate(IReadOnlyList<IHittable> triangles)
    {
        var faces = new List<(SmoothTriangle Tri, bool Mirrored)>();
        var sums = new Dictionary<CornerKey, Vector3>();

        foreach (var h in triangles)
        {
            if (h is not SmoothTriangle tri || !tri.HasTextureUvs) continue;

            Vector3 faceN = Vector3.Cross(tri.V1 - tri.V0, tri.V2 - tri.V0);
            bool mirrored = Vector3.Dot(Vector3.Cross(faceN, tri.FaceTangent), tri.FaceBitangent) < 0f;
            faces.Add((tri, mirrored));

            for (int c = 0; c < 3; c++)
            {
                var (p, n, uv) = Corner(tri, c);
                Vector3 t = tri.FaceTangent - Vector3.Dot(tri.FaceTangent, n) * n;
                if (t.LengthSquared() < 1e-16f) continue;
                t = Vector3.Normalize(t);

                Vector3 e1 = Corner(tri, (c + 1) % 3).P - p;
                Vector3 e2 = Corner(tri, (c + 2) % 3).P - p;
                float l1 = e1.Length(), l2 = e2.Length();
                if (l1 <= 0f || l2 <= 0f) continue;
                float angle = MathF.Acos(Math.Clamp(Vector3.Dot(e1, e2) / (l1 * l2), -1f, 1f));

                var key = new CornerKey(p, n, uv, mirrored);
                sums[key] = sums.GetValueOrDefault(key) + angle * t;
            }
        }

        foreach (var (tri, mirrored) in faces)
        {
            Span<Vector3> vt = stackalloc Vector3[3];
            for (int c = 0; c < 3; c++)
            {
                var (p, n, uv) = Corner(tri, c);
                Vector3 sum = sums.GetValueOrDefault(new CornerKey(p, n, uv, mirrored));
                // A corner whose tangents cancel (or a degenerate one) falls
                // back to the face's own tangent in its normal's plane.
                if (sum.LengthSquared() < 1e-16f)
                    sum = tri.FaceTangent - Vector3.Dot(tri.FaceTangent, n) * n;
                vt[c] = sum.LengthSquared() > 1e-16f ? Vector3.Normalize(sum) : tri.FaceTangent;
            }
            tri.SetVertexTangents(vt[0], vt[1], vt[2], mirrored ? -1f : 1f);
        }
    }

    private static (Vector3 P, Vector3 N, Vector2 Uv) Corner(SmoothTriangle t, int c) => c switch
    {
        0 => (t.V0, t.N0, t.UV0),
        1 => (t.V1, t.N1, t.UV1),
        _ => (t.V2, t.N2, t.UV2),
    };
}
