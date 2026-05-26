using System.Numerics;
using RayTracer.Geometry;
using RayTracer.Materials;
using RayTracer.Textures;
using RayTracer.Volumetrics;

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
    /// Participating-media boundary at this surface (interior + exterior).
    /// Populated by <see cref="Geometry.MediumBoundHittable"/> when the entity
    /// was bound to one or more media in YAML (<c>interior_medium</c> /
    /// <c>exterior_medium</c>). Default-zero on plain entities — both sides
    /// resolve to vacuum / the outer stack's current top. The renderer
    /// consults this on refractive samples to push/pop the medium stack
    /// driving Beer-Lambert and volumetric scattering along the next segment.
    /// </summary>
    public MediumInterface MediumIface;

    /// <summary>
    /// Root <see cref="IHittable"/> of the entity that produced this hit —
    /// the inner geometry that <see cref="Geometry.MediumBoundHittable"/>
    /// wraps. Populated by the same wrapper that stamps
    /// <see cref="MediumIface"/>, alongside it. The random-walk SSS
    /// integrator uses it to query boundary intersections restricted to
    /// the bound entity (rather than the global BVH), preventing the walk
    /// from leaking into other geometry through coincident surfaces.
    /// Default-null on plain entities — the SSS path is gated on a
    /// non-null binding here anyway.
    /// </summary>
    public IHittable? EntityRoot;

    /// <summary>
    /// Per-ray-category visibility bitmask (Arnold <c>visibility.*</c> / Cycles
    /// "Ray Visibility"). A non-zero bit means the renderer should treat this
    /// surface as transparent for the corresponding ray category — the ray
    /// advances past the hit and re-traces, leaving the underlying geometry
    /// visible via the other categories (a mirror still reflects a
    /// camera-invisible surface, NEE shadow still notices a shadow-invisible
    /// one, etc.). Set by <see cref="Geometry.VisibilityFilteredHittable"/> or
    /// the legacy <see cref="Geometry.CameraInvisibleHittable"/>; consumed by
    /// <see cref="Rendering.Renderer.TraceRay"/> and
    /// <see cref="Geometry.ShadowRay"/>.
    /// </summary>
    public HitVisibilityMask VisibilityMask;

    /// <summary>
    /// Legacy bridge to the <see cref="HitVisibilityMask.Camera"/> bit. Kept so
    /// existing call sites (Renderer's primary-ray skip loop, light proxies)
    /// continue to compile unchanged after the bitmask refactor.
    /// </summary>
    public bool CameraInvisible
    {
        get => (VisibilityMask & HitVisibilityMask.Camera) != 0;
        set
        {
            if (value) VisibilityMask |= HitVisibilityMask.Camera;
            else       VisibilityMask &= ~HitVisibilityMask.Camera;
        }
    }

    /// <summary>
    /// Mesh-level "autobump" residual bump map (DEVLOG surface-displacement
    /// step 5 — Arnold's <c>autobump_visibility</c> equivalent). Populated
    /// by <see cref="Geometry.Mesh.Hit"/> when the mesh was loaded with
    /// <c>displacement.autobump: true</c>. The renderer consumes it in
    /// <c>ShadeSurface</c> after the material-level <c>BumpMap</c>, so the
    /// final shading normal composes
    /// <c>normal_map → material.bump_map → mesh.autobump</c> on top of the
    /// already-displaced geometry. Independent of the material — sharing
    /// the material across multiple meshes is therefore still safe even
    /// when only some of them carry an autobump.
    /// </summary>
    public IBumpMap? AutoBump;

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

    // ── Parametric partials ∂P/∂u, ∂P/∂v (world or object space) ────────────
    // Used by texture-filtering to convert a screen-space surface footprint
    // (∂P/∂x, ∂P/∂y) into UV partials (∂u/∂x, ∂v/∂x, ∂u/∂y, ∂v/∂y) so image
    // textures can pick a mipmap LOD and procedural 2D textures (checker,
    // brick, gradient) can downsample analytically.
    //
    // Their magnitudes encode "world distance per UV unit" at the hit and
    // must be primitive-specific — Sphere of radius R has |∂P/∂u| = 2π·R·sinθ,
    // a quad of size W,H has |∂P/∂u| = W. When a primitive's Hit() leaves
    // these zero, the footprint code falls back to the unit-tangent /
    // unit-bitangent vectors, which is a conservative under-estimate (slight
    // residual aliasing at tight UV scales but no over-blurring).
    public Vector3 DpDu;
    public Vector3 DpDv;

    /// <summary>
    /// Analytic filter footprint at this shading point (PBRT §10.1).
    /// Populated by <see cref="Rendering.Renderer"/> after the world Hit()
    /// returns for primary rays that carry ray differentials; left
    /// default-zero (<see cref="FilterFootprint.HasFootprint"/> == false)
    /// for shadow / NEE / BSDF-bounce rays. Textures consume it through the
    /// footprint-aware <c>ITexture.Value</c> overload and fall back to point
    /// sampling when it's not present.
    /// </summary>
    public FilterFootprint Footprint;

    public void SetFaceNormal(Ray ray, Vector3 outwardNormal)
    {
        FrontFace = Vector3.Dot(ray.Direction, outwardNormal) < 0;
        Normal = FrontFace ? outwardNormal : -outwardNormal;
    }
}
