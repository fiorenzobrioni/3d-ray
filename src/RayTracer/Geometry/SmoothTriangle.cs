using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// Triangle with per-vertex normals and texture coordinates for smooth-shaded meshes.
///
/// Uses the same watertight intersection as <see cref="Triangle"/>, but
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
///   u, v are the coordinates
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

    // ∂P/∂u, ∂P/∂v of the UV mapping (unnormalised): the texture footprint
    // needs the rate, not just the direction. Zero when the UVs are degenerate.
    private readonly Vector3 _dpdu;
    private readonly Vector3 _dpdv;

    // Per-vertex tangents (MeshTangents), interpolated at the hit like the
    // normals, and the face's bitangent sign: B = sign · (N × T). Without
    // them the face tangent is used, constant per triangle.
    private Vector3 _vt0, _vt1, _vt2;
    private float _tangentSign;
    private bool _hasVertexTangents;

    /// <summary>True when the triangle was given real texture coordinates.</summary>
    public bool HasTextureUvs { get; }

    /// <summary>The face tangent (∂P/∂u direction) and bitangent of the UV mapping.</summary>
    public Vector3 FaceTangent => _tangent;
    public Vector3 FaceBitangent => _bitangent;

    // Precomputed edges + area — immutable across the mesh's lifetime so the
    // hit-time subtractions and the SampleAt Cross/Length are wasted work.
    private readonly Vector3 _edge1;
    private readonly Vector3 _edge2;
    private readonly float _area;

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
        HasTextureUvs = true;

        _edge1 = v1 - v0;
        _edge2 = v2 - v0;
        Vector3 faceCross = Vector3.Cross(_edge1, _edge2);
        float faceCrossLen = faceCross.Length();
        _faceNormal = faceCrossLen > 0f ? faceCross / faceCrossLen : Vector3.UnitY;
        _area = 0.5f * faceCrossLen;

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
        Vector3 edge1 = _edge1;
        Vector3 edge2 = _edge2;
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
                _dpdu = t;
                _dpdv = b;
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
    {
        HasTextureUvs = false;
    }

    /// <summary>
    /// Gives the triangle per-vertex tangents (unit, in the plane of each
    /// vertex normal) and the sign of its bitangent, as computed over the
    /// whole mesh by <see cref="MeshTangents"/>.
    /// </summary>
    public void SetVertexTangents(Vector3 t0, Vector3 t1, Vector3 t2, float sign)
    {
        _vt0 = t0; _vt1 = t1; _vt2 = t2;
        _tangentSign = sign < 0f ? -1f : 1f;
        _hasVertexTangents = true;
    }

    public bool Hit(in Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        // Watertight test, shared with the flat Triangle.
        if (!TriangleIntersection.Intersect(ray, V0, V1, V2, tMin, tMax, out float t, out float u, out float v))
            return false;

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
        // With vertex tangents the frame is MikkTSpace's: the tangent
        // interpolated and made orthogonal to the interpolated normal, the
        // bitangent sign · (N × T) rebuilt at the hit. A baked normal map is
        // encoded in exactly this frame, so it shows no seams between faces.
        Vector3 tangent = _tangent, bitangent = _bitangent;
        if (_hasVertexTangents)
        {
            Vector3 ti = w0 * _vt0 + w1 * _vt1 + w2 * _vt2;
            ti -= Vector3.Dot(ti, shadingNormal) * shadingNormal;
            float tl = ti.Length();
            if (tl > 1e-8f)
            {
                tangent = ti / tl;
                bitangent = _tangentSign * Vector3.Cross(shadingNormal, tangent);
            }
        }
        rec.Tangent = tangent;
        rec.Bitangent = bitangent;
        rec.DpDu = _dpdu;
        rec.DpDv = _dpdv;

        rec.ObjectSeed = Seed;
        rec.Material = Material;
        return true;
    }

    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) Sample()
    {
        var (r2u, r2v) = MathUtils.Random2D();
        return SampleAt(r2u, r2v);
    }

    /// <inheritdoc/>
    public float SurfaceArea => _area;

    /// <summary>
    /// Stratified version — uses the same unit-square-to-triangle warp as
    /// <see cref="Triangle.SampleStratified"/>.
    /// </summary>
    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleStratified(int sampleIndex, int sampleCount)
    {
        var (xs1, xs2) = MathUtils.Stratum2D(sampleIndex, sampleCount);
        float xi1 = xs1;
        float xi2 = xs2;
        return SampleAt(xi1, xi2);
    }

    private (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleAt(float xi1, float xi2)
    {
        float r1 = MathF.Sqrt(xi1);
        float w1 = 1f - r1;
        float w2 = xi2 * r1;
        float w0 = 1f - w1 - w2;
        Vector3 point = w0 * V0 + w1 * V1 + w2 * V2;
        Vector3 normal = Vector3.Normalize(w0 * N0 + w1 * N1 + w2 * N2);
        // Interpolate per-vertex UVs — matches Hit() exactly so textured
        // emissives get the correct UV at NEE sample points.
        Vector2 uv = w0 * UV0 + w1 * UV1 + w2 * UV2;
        return (point, normal, uv, _area);
    }

    public int Seed { get; set; }

    /// <summary>
    /// Recovers the barycentrics (u along V0→V1, v along V0→V2)
    /// of a point assumed to lie in this triangle's plane.
    /// </summary>
    public void Barycentric(Vector3 p, out float u, out float v)
    {
        Vector3 w = p - V0;
        float d00 = Vector3.Dot(_edge1, _edge1);
        float d01 = Vector3.Dot(_edge1, _edge2);
        float d11 = Vector3.Dot(_edge2, _edge2);
        float d20 = Vector3.Dot(w, _edge1);
        float d21 = Vector3.Dot(w, _edge2);
        float denom = d00 * d11 - d01 * d01;
        if (MathF.Abs(denom) < 1e-20f) { u = 0f; v = 0f; return; }
        float inv = 1f / denom;
        u = (d11 * d20 - d01 * d21) * inv;
        v = (d00 * d21 - d01 * d20) * inv;
    }

    public AABB BoundingBox()
    {
        // Exact: the BVH's slab test is conservative (AABB.Hit).
        return new AABB(Vector3.Min(Vector3.Min(V0, V1), V2), Vector3.Max(Vector3.Max(V0, V1), V2));
    }
}
