using System.Numerics;
using RayTracer.Materials;

namespace RayTracer.Core;

public struct HitRecord
{
    public Vector3 Point;
    public Vector3 LocalPoint;
    public Vector3 Normal;
    public float T;
    public float U;
    public float V;
    public int ObjectSeed;
    public bool FrontFace;
    public IMaterial? Material;

    /// <summary>
    /// True when this hit lies on a primitive whose owning entity (or owning
    /// light proxy) is flagged as not visible to primary camera rays (Arnold
    /// <c>camera</c> visibility, Cycles "Ray Visibility → Camera"). Set by
    /// <see cref="Geometry.CameraInvisibleHittable"/>; consumed by
    /// <see cref="Rendering.Renderer.TraceRay"/> which, on the primary ray
    /// only, advances past such hits and re-traces — leaving the underlying
    /// emitter still visible via mirror/glass paths and still illuminating
    /// the scene through NEE.
    /// </summary>
    public bool CameraInvisible;

    // ── TBN basis for normal mapping ────────────────────────────────────────
    // Tangent is aligned with the +U direction of the UV mapping.
    // Bitangent is aligned with the +V direction.
    // Together with Normal they form the TBN matrix used to transform
    // tangent-space normals from normal maps into world space.
    //
    // These are set by each primitive's Hit() method, consistent with
    // how that primitive computes its UV coordinates. If a primitive doesn't
    // set them, they remain zero — normal mapping will be skipped.
    public Vector3 Tangent;
    public Vector3 Bitangent;

    public void SetFaceNormal(Ray ray, Vector3 outwardNormal)
    {
        FrontFace = Vector3.Dot(ray.Direction, outwardNormal) < 0;
        Normal = FrontFace ? outwardNormal : -outwardNormal;
    }
}
