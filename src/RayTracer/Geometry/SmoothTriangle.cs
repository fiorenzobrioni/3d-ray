using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// Triangle with per-vertex normals and texture coordinates for smooth-shaded meshes.
///
/// Uses the same Möller–Trumbore intersection as <see cref="Triangle"/>, but
/// interpolates normals and UVs via barycentric coordinates for:
///   • Smooth shading (Phong normal interpolation) — no visible facet edges.
///   • Artist UV mapping — texture coordinates from OBJ/glTF vertex data.
///   • UV-derived TBN — tangent and bitangent computed from the UV mapping
///     gradient, enabling correct normal map orientation on arbitrary meshes.
///
/// <b>Barycentric convention:</b>
///   w0 = 1 - u - v  (weight for V0)
///   w1 = u           (weight for V1)
///   w2 = v           (weight for V2)
///   This matches the Möller–Trumbore output where u, v are the coordinates
///   along edges V0→V1 and V0→V2 respectively.
///
/// <b>Normal interpolation:</b>
///   The shading normal is N = normalize(w0*N0 + w1*N1 + w2*N2).
///   The geometric (face) normal is still used for FrontFace determination
///   to prevent light leaking through thin geometry.
///
/// <b>TBN from UV gradients:</b>
///   T and B are computed from ∂P/∂u_tex and ∂P/∂v_tex using the standard
///   edge-UV matrix inversion. This ensures normal maps align with the
///   artist's UV layout, not the geometric edge directions.
///
/// Implements ISamplable for area-light support (same as flat Triangle).
/// </summary>
public class SmoothTriangle : IHittable, ISamplable
{
    // ── Vertex positions ────────────────────────────────────────────────────
    public Vector3 V0 { get; }
    public Vector3 V1 { get; }
    public Vector3 V2 { get; }

    // ── Per-vertex normals (for smooth shading) ─────────────────────────────
    public Vector3 N0 { get; }
    public Vector3 N1 { get; }
    public Vector3 N2 { get; }

    // ── Per-vertex texture coordinates ──────────────────────────────────────
    public Vector2 UV0 { get; }
    public Vector2 UV1 { get; }
    public Vector2 UV2 { get; }

    public IMaterial Material { get; }

    // Precomputed geometric (face) normal — used for FrontFace and as fallback
    private readonly Vector3 _faceNormal;

    // Precomputed TBN from UV gradients (computed once in constructor)
    private readonly Vector3 _tangent;
    private readonly Vector3 _bitangent;

    public SmoothTriangle(
        Vector3 v0, Vector3 v1, Vector3 v2,
        Vector3 n0, Vector3 n1, Vector3 n2,
        Vector2 uv0, Vector2 uv1, Vector2 uv2,
        IMaterial material)
    {
        V0 = v0; V1 = v1; V2 = v2;
        N0 = Vector3.Normalize(n0);
        N1 = Vector3.Normalize(n1);
        N2 = Vector3.Normalize(n2);
        UV0 = uv0; UV1 = uv1; UV2 = uv2;
        Material = material;

        _faceNormal = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));

        // ── Precompute tangent/bitangent from UV gradients ──────────────
        //
        // Given edges E1 = V1-V0, E2 = V2-V0 and UV deltas:
        //   dUV1 = UV1-UV0,  dUV2 = UV2-UV0
        //
        // The tangent and bitangent satisfy:
        //   E1 = dUV1.x * T + dUV1.y * B
        //   E2 = dUV2.x * T + dUV2.y * B
        //
        // Solving: [T, B] = (1/det) * [[dUV2.y, -dUV1.y], [-dUV2.x, dUV1.x]] × [E1, E2]
        Vector3 edge1 = v1 - v0;
        Vector3 edge2 = v2 - v0;
        Vector2 dUV1 = uv1 - uv0;
        Vector2 dUV2 = uv2 - uv0;

        float det = dUV1.X * dUV2.Y - dUV2.X * dUV1.Y;

        if (MathF.Abs(det) > 1e-8f)
        {
            float invDet = 1f / det;
            Vector3 t = invDet * (dUV2.Y * edge1 - dUV1.Y * edge2);
            Vector3 b = invDet * (-dUV2.X * edge1 + dUV1.X * edge2);

            float tLen = t.Length();
            float bLen = b.Length();

            if (tLen > 1e-8f && bLen > 1e-8f)
            {
                _tangent = t / tLen;
                _bitangent = b / bLen;
            }
            else
            {
                // Degenerate UV mapping — fall back to edge-based TBN
                _tangent = Vector3.Normalize(edge1);
                _bitangent = Vector3.Normalize(edge2);
            }
        }
        else
        {
            // Degenerate UV (all vertices share same UV point) — edge-based fallback
            _tangent = Vector3.Normalize(edge1);
            _bitangent = Vector3.Normalize(edge2);
        }
    }

    /// <summary>
    /// Convenience constructor for meshes without texture coordinates.
    /// Assigns barycentric UVs (same as flat Triangle) and edge-based TBN.
    /// </summary>
    public SmoothTriangle(
        Vector3 v0, Vector3 v1, Vector3 v2,
        Vector3 n0, Vector3 n1, Vector3 n2,
        IMaterial material)
        : this(v0, v1, v2, n0, n1, n2,
               new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1),
               material)
    { }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        // ── Möller–Trumbore (identical to flat Triangle) ────────────────
        Vector3 edge1 = V1 - V0;
        Vector3 edge2 = V2 - V0;
        Vector3 h = Vector3.Cross(ray.Direction, edge2);
        float a = Vector3.Dot(edge1, h);

        if (MathF.Abs(a) < 1e-7f) return false;

        float f = 1f / a;
        Vector3 s = ray.Origin - V0;
        float u = f * Vector3.Dot(s, h);
        if (u < 0f || u > 1f) return false;

        Vector3 q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(ray.Direction, q);
        if (v < 0f || u + v > 1f) return false;

        float t = f * Vector3.Dot(edge2, q);
        if (t < tMin || t > tMax) return false;

        // ── Barycentric weights ─────────────────────────────────────────
        float w0 = 1f - u - v;
        float w1 = u;
        float w2 = v;

        rec.T = t;
        rec.Point = ray.At(t);
        rec.LocalPoint = rec.Point;

        // ── Interpolated shading normal ─────────────────────────────────
        Vector3 shadingNormal = Vector3.Normalize(w0 * N0 + w1 * N1 + w2 * N2);

        // Use the FACE normal for front/back classification (prevents light
        // leaking through thin geometry where interpolated normals might
        // face the wrong way near edges).
        rec.FrontFace = Vector3.Dot(ray.Direction, _faceNormal) < 0;
        rec.Normal = rec.FrontFace ? shadingNormal : -shadingNormal;

        // ── Interpolated UV coordinates ─────────────────────────────────
        Vector2 interpUV = w0 * UV0 + w1 * UV1 + w2 * UV2;
        rec.U = interpUV.X;
        rec.V = interpUV.Y;

        // ── TBN basis ───────────────────────────────────────────────────
        rec.Tangent = _tangent;
        rec.Bitangent = _bitangent;

        rec.ObjectSeed = Seed;
        rec.Material = Material;
        return true;
    }

    public (Vector3 Point, Vector3 Normal, float Area) Sample()
    {
        float r1 = MathF.Sqrt(MathUtils.RandomFloat());
        float r2 = MathUtils.RandomFloat();
        float w1 = 1f - r1;
        float w2 = r2 * r1;
        float w0 = 1f - w1 - w2;
        Vector3 point = w0 * V0 + w1 * V1 + w2 * V2;
        Vector3 normal = Vector3.Normalize(w0 * N0 + w1 * N1 + w2 * N2);
        float area = 0.5f * Vector3.Cross(V1 - V0, V2 - V0).Length();
        return (point, normal, area);
    }

    public int Seed { get; set; }

    public AABB BoundingBox()
    {
        var min = Vector3.Min(Vector3.Min(V0, V1), V2) - new Vector3(MathUtils.Epsilon);
        var max = Vector3.Max(Vector3.Max(V0, V1), V2) + new Vector3(MathUtils.Epsilon);
        return new AABB(min, max);
    }
}
