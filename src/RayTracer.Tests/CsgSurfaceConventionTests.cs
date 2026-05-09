using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Surface-record convention tests for CsgObject. These guard against a
/// recurrence of the regression where CSG subtraction flipped rec.Normal as
/// well as rec.FrontFace, leaving rec.Normal co-directional with the ray on
/// carved (B) surfaces. With the shading convention violated, every Lambertian
/// /Disney reflective lobe collapsed to zero (NdotL ≤ 0) and the carved
/// interior of subtraction CSG rendered black.
///
/// The contract every primitive in this codebase must honour is:
///   1. rec.Normal · ray.Direction &lt; 0  (shading normal opposes the incoming
///      ray — the post-condition of HitRecord.SetFaceNormal).
///   2. rec.FrontFace is true iff the ray is entering the shaded solid.
/// CSG operations are themselves IHittable solids, so the same contract holds
/// for CsgObject.Hit.
/// </summary>
public class CsgSurfaceConventionTests
{
    private static IMaterial Mat() => new Lambertian(new Vector3(0.5f));

    /// <summary>
    /// Sphere minus a smaller cylinder along Y, ray that grazes the carved
    /// interior wall. The first visible surface is on the cylinder wall
    /// (a B-surface — flipped path in CsgObject.TryCandidate). The shading
    /// normal must oppose the ray direction; if it doesn't, every reflective
    /// lobe will return zero radiance there and the carve renders black.
    /// </summary>
    [Fact]
    public void Subtraction_CarvedInteriorNormal_OpposesRay()
    {
        var outer    = new Sphere(Vector3.Zero, 1.0f, Mat());
        var cylinder = new Cylinder(new Vector3(0, 0, 0), radius: 0.4f, height: 4f, Mat());
        var carved   = new CsgObject(CsgOperation.Subtraction, outer, cylinder);

        // Ray descends through the carve from above the sphere with a small
        // horizontal component so it actually grazes the cylinder wall before
        // exiting at the bottom. A purely axial ray would miss the wall.
        var ray = new Ray(
            origin:    new Vector3(0.30f, 2f, 0f),
            direction: Vector3.Normalize(new Vector3(-0.05f, -1f, 0f)));

        var rec = new HitRecord();
        bool hit = carved.Hit(ray, 0.001f, float.PositiveInfinity, ref rec);
        Assert.True(hit, "Ray must hit the carved interior of the sphere-cylinder subtraction.");

        // Core invariant: shading normal opposes the incoming ray.
        float dot = Vector3.Dot(rec.Normal, ray.Direction);
        Assert.True(dot < 0f,
            $"rec.Normal must oppose the ray direction (Dot < 0), got Dot = {dot}. " +
            "If this fails, CSG is flipping rec.Normal again and Lambertian/Disney " +
            "reflection lobes will render the carved interior as black.");
    }

    /// <summary>
    /// Verifies FrontFace flips correctly on a B-surface. A ray starting
    /// inside the carved cavity (which is OUTSIDE the resulting solid A\B —
    /// that volume has been removed) and traveling outward through the
    /// cylinder wall is ENTERING A\B, so FrontFace must be true.
    /// </summary>
    [Fact]
    public void Subtraction_RayEnteringCarvedSurface_FrontFaceIsTrue()
    {
        var outer    = new Sphere(Vector3.Zero, 1.0f, Mat());
        var cylinder = new Cylinder(new Vector3(0, 0, 0), radius: 0.4f, height: 4f, Mat());
        var carved   = new CsgObject(CsgOperation.Subtraction, outer, cylinder);

        // Start at the sphere's centre (inside the cylinder cavity, which has
        // been removed → outside A\B) and shoot horizontally outward. First
        // hit is the cylinder wall from inside; after CSG that surface is
        // an entry into A\B (about to enter the surrounding sphere material).
        var ray = new Ray(
            origin:    new Vector3(0f, 0f, 0f),
            direction: Vector3.UnitX);

        var rec = new HitRecord();
        bool hit = carved.Hit(ray, 0.001f, float.PositiveInfinity, ref rec);
        Assert.True(hit);
        Assert.True(rec.FrontFace,
            "Crossing a B-surface from the carved cavity into the surrounding A " +
            "material is an entry of (A \\ B). FrontFace must be true.");

        float dot = Vector3.Dot(rec.Normal, ray.Direction);
        Assert.True(dot < 0f, $"rec.Normal must oppose ray direction (Dot < 0), got {dot}.");
    }

    /// <summary>
    /// Intersection (lens) — neither operand surface is flipped, so the
    /// shading-normal invariant must hold trivially. Included as a baseline
    /// against accidental "always flip" regressions in TryCandidate.
    /// </summary>
    [Fact]
    public void Intersection_BiconvexLens_NormalsOpposeRayBothFaces()
    {
        var sphereA = new Sphere(new Vector3(0, 0, -0.35f), 1.0f, Mat());
        var sphereB = new Sphere(new Vector3(0, 0,  0.35f), 1.0f, Mat());
        var lens    = new CsgObject(CsgOperation.Intersection, sphereA, sphereB);

        // Forward ray — first hit is the front face of the lens (entering).
        {
            var ray = new Ray(new Vector3(0, 0, -3f), Vector3.UnitZ);
            var rec = new HitRecord();
            Assert.True(lens.Hit(ray, 0.001f, float.PositiveInfinity, ref rec));
            Assert.True(rec.FrontFace, "Front face of lens — FrontFace should be true.");
            Assert.True(Vector3.Dot(rec.Normal, ray.Direction) < 0f);
        }

        // Ray starting inside the lens — first hit is the back face (exiting).
        // Stepping just past the lens-axis center keeps us inside the
        // intersection volume so the very next hit is the exit surface.
        {
            var ray = new Ray(new Vector3(0, 0, 0f), Vector3.UnitZ);
            var rec = new HitRecord();
            Assert.True(lens.Hit(ray, 0.001f, float.PositiveInfinity, ref rec));
            Assert.False(rec.FrontFace, "Back face of lens hit from inside — FrontFace should be false.");
            Assert.True(Vector3.Dot(rec.Normal, ray.Direction) < 0f,
                "Even on a back-face hit, rec.Normal is the shading normal and must oppose the ray.");
        }
    }
}
