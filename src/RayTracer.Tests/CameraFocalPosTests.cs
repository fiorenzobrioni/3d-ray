using System.Collections.Generic;
using System.Numerics;
using RayTracer.Scene;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Unit tests for <see cref="SceneLoader.ComputeFocusDistance"/> — the
/// helper that resolves the camera focus distance from either a scalar
/// <c>focal_dist</c> or a 3D <c>focal_pos</c> point. Verifies the standard
/// Arnold/Cycles/RenderMan "Focus Point" math: the result is the
/// projection of the camera→focal-point vector onto the optical axis,
/// not the Euclidean distance.
///
/// <para>All cases are deterministic and scalar — no PRNG, no
/// stochastic tolerance.</para>
/// </summary>
public class CameraFocalPosTests
{
    private const float Tol = 1e-4f;

    /// <summary>
    /// Focal point on the optical axis: projection equals the Euclidean
    /// distance (the two coincide only on-axis).
    /// </summary>
    [Fact]
    public void FocalPos_OnAxis_ReturnsExactDistance()
    {
        var camPos  = Vector3.Zero;
        var lookAt  = new Vector3(0f, 0f, -1f);
        var focal   = new List<float> { 0f, 0f, -5f };

        float d = SceneLoader.ComputeFocusDistance(camPos, lookAt, focal,
                                                    fallbackFocalDist: 99f);
        Assert.Equal(5f, d, Tol);
    }

    /// <summary>
    /// Focal point off-axis: the focus distance is the projection
    /// (component along forward), not the Euclidean magnitude. A subject
    /// at (3, 4, -5) yields focus distance 5, not √50 ≈ 7.07. This is
    /// what every production renderer does — the focus plane is
    /// perpendicular to the view direction.
    /// </summary>
    [Fact]
    public void FocalPos_OffAxis_ProjectsOntoOpticalAxis()
    {
        var camPos  = Vector3.Zero;
        var lookAt  = new Vector3(0f, 0f, -1f);
        var focal   = new List<float> { 3f, 4f, -5f };

        float d = SceneLoader.ComputeFocusDistance(camPos, lookAt, focal,
                                                    fallbackFocalDist: 99f);
        Assert.Equal(5f, d, Tol);
    }

    /// <summary>
    /// Diagonal optical axis: focus distance is the projection along
    /// the unit forward direction (1,1,1)/√3, so a focal point at
    /// (2, 2, 2) yields focusDist = (2+2+2)/√3 = 2√3 ≈ 3.4641.
    /// </summary>
    [Fact]
    public void FocalPos_DiagonalAxis_ProjectsCorrectly()
    {
        var camPos  = Vector3.Zero;
        var lookAt  = new Vector3(1f, 1f, 1f);   // un-normalized — helper normalizes internally
        var focal   = new List<float> { 2f, 2f, 2f };

        float d = SceneLoader.ComputeFocusDistance(camPos, lookAt, focal,
                                                    fallbackFocalDist: 99f);
        Assert.Equal(2f * System.MathF.Sqrt(3f), d, Tol);
    }

    /// <summary>
    /// Camera position offset: forward projection still works. Camera at
    /// (10, 5, 0), look_at along +X yields forward = (1,0,0). A focal
    /// point at (15, 5, 7) projects to (15-10) = 5 along forward —
    /// off-axis y/z components are discarded.
    /// </summary>
    [Fact]
    public void FocalPos_OffsetCamera_AccountsForCameraPosition()
    {
        var camPos  = new Vector3(10f, 5f, 0f);
        var lookAt  = new Vector3(11f, 5f, 0f);
        var focal   = new List<float> { 15f, 5f, 7f };

        float d = SceneLoader.ComputeFocusDistance(camPos, lookAt, focal,
                                                    fallbackFocalDist: 99f);
        Assert.Equal(5f, d, Tol);
    }

    /// <summary>
    /// Focal point behind the camera: the projection is negative, so the
    /// helper warns and falls back to the scalar focal_dist. A focal
    /// plane "behind the lens" is non-physical for a forward-facing
    /// camera; better to noisily ignore than silently produce a degenerate
    /// camera.
    /// </summary>
    [Fact]
    public void FocalPos_BehindCamera_FallsBackToFocalDist()
    {
        var camPos  = Vector3.Zero;
        var lookAt  = new Vector3(0f, 0f, -1f);   // forward = -Z
        var focal   = new List<float> { 0f, 0f, 5f };  // BEHIND camera

        float d = SceneLoader.ComputeFocusDistance(camPos, lookAt, focal,
                                                    fallbackFocalDist: 7f);
        Assert.Equal(7f, d, Tol);
    }

    /// <summary>
    /// Focal point coincident with the camera: projection is 0, fall back.
    /// </summary>
    [Fact]
    public void FocalPos_AtCameraPosition_FallsBackToFocalDist()
    {
        var camPos  = new Vector3(2f, 3f, 4f);
        var lookAt  = new Vector3(2f, 3f, 0f);
        var focal   = new List<float> { 2f, 3f, 4f };  // identical

        float d = SceneLoader.ComputeFocusDistance(camPos, lookAt, focal,
                                                    fallbackFocalDist: 8.5f);
        Assert.Equal(8.5f, d, Tol);
    }

    /// <summary>
    /// Degenerate camera (lookAt == camPos) → no usable forward axis.
    /// Helper falls back gracefully instead of dividing by zero.
    /// </summary>
    [Fact]
    public void FocalPos_DegenerateCamera_FallsBackToFocalDist()
    {
        var camPos  = new Vector3(1f, 2f, 3f);
        var lookAt  = camPos;
        var focal   = new List<float> { 0f, 0f, -10f };

        float d = SceneLoader.ComputeFocusDistance(camPos, lookAt, focal,
                                                    fallbackFocalDist: 4.2f);
        Assert.Equal(4.2f, d, Tol);
    }

    /// <summary>
    /// Malformed focal_pos (less than 3 components): warn + fall back.
    /// </summary>
    [Fact]
    public void FocalPos_ShortList_FallsBackToFocalDist()
    {
        var camPos  = Vector3.Zero;
        var lookAt  = new Vector3(0f, 0f, -1f);
        var focal   = new List<float> { 0f, 0f };  // missing Z

        float d = SceneLoader.ComputeFocusDistance(camPos, lookAt, focal,
                                                    fallbackFocalDist: 6f);
        Assert.Equal(6f, d, Tol);
    }

    /// <summary>
    /// No focal_pos supplied → trivially returns the scalar focal_dist
    /// (current default behaviour, backward-compatible with all existing
    /// scenes).
    /// </summary>
    [Fact]
    public void FocalPos_Null_ReturnsFallbackUnchanged()
    {
        float d = SceneLoader.ComputeFocusDistance(
            camPos:        Vector3.Zero,
            lookAt:        new Vector3(0f, 0f, -1f),
            focalPos:      null,
            fallbackFocalDist: 12.34f);
        Assert.Equal(12.34f, d, Tol);
    }
}
