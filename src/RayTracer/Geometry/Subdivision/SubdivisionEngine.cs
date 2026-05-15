using System.Numerics;
using RayTracer.Geometry;
using RayTracer.Materials;

namespace RayTracer.Geometry.Subdivision;

/// <summary>
/// Orchestrates the full subdivision pipeline: scheme selection, adaptive
/// iteration count, the subdivision pass itself, normal reconstruction and
/// the final conversion to <see cref="SmoothTriangle"/> instances ready for
/// the renderer's BVH. Lives at the same architectural level as
/// <see cref="ObjLoader"/> — the loader hands us a parsed
/// <see cref="PolyMesh"/>, we hand back a triangle list.
/// </summary>
internal static class SubdivisionEngine
{
    /// <summary>
    /// Subdivides <paramref name="mesh"/> per <paramref name="options"/> in
    /// place. Returns the actual number of iterations performed and the
    /// resolved scheme (after <see cref="SubdivisionScheme.Auto"/> resolution).
    /// </summary>
    public static (int IterationsRun, SubdivisionScheme ResolvedScheme) Apply(
        PolyMesh mesh, SubdivisionOptions options)
    {
        if (!options.IsActive || mesh.FaceCount == 0)
            return (0, SubdivisionScheme.None);

        // ── Resolve Auto scheme ───────────────────────────────────────────
        var scheme = options.Scheme;
        if (scheme == SubdivisionScheme.Auto)
        {
            // Prefer Catmull-Clark when the mesh is a pure quad sheet —
            // matches OpenSubdiv's recommended default. Triangle-only meshes
            // go through Loop. Mixed-arity meshes route to CC, which
            // tolerates any input arity in the first iteration.
            if (mesh.IsAllQuads())             scheme = SubdivisionScheme.CatmullClark;
            else if (mesh.IsAllTriangles())    scheme = SubdivisionScheme.Loop;
            else                                scheme = SubdivisionScheme.CatmullClark;
        }

        // Loop only handles triangles — fan-triangulate non-triangle input.
        if (scheme == SubdivisionScheme.Loop && !mesh.IsAllTriangles())
            mesh.TriangulateInPlace();

        // ── Compute iteration count ───────────────────────────────────────
        int adaptive = 0;
        if (options.PixelError > 0 && options.Screen is { } screen)
            adaptive = AdaptiveIterations(mesh, screen, options.PixelError);

        int max = Math.Max(1, options.MaxIterations);
        int iters = Math.Clamp(Math.Max(options.Iterations, adaptive), 0, max);
        if (iters <= 0) return (0, scheme);

        // ── Run the algorithm ─────────────────────────────────────────────
        switch (scheme)
        {
            case SubdivisionScheme.Loop:
                LoopSubdivider.Subdivide(mesh, iters);
                break;
            case SubdivisionScheme.CatmullClark:
                CatmullClarkSubdivider.Subdivide(mesh, iters);
                break;
            default:
                return (0, scheme);
        }

        return (iters, scheme);
    }

    /// <summary>
    /// Estimates how many subdivision iterations bring the longest projected
    /// edge below <paramref name="pixelError"/>. Each iteration roughly
    /// halves edge length, so we need <c>ceil(log2(longestPixelEdge / target))</c>
    /// rounds — clamped to <see cref="SubdivisionOptions.MaxIterations"/> by
    /// the caller.
    ///
    /// <para>We use the <i>longest</i> edge rather than the average so a
    /// mesh with mixed face sizes is dimensionally correct for its largest
    /// silhouette features — the same conservative choice as Arnold's
    /// <c>subdiv_pixel_error</c>.</para>
    /// </summary>
    private static int AdaptiveIterations(PolyMesh mesh, ScreenSpaceContext s, float pixelError)
    {
        if (mesh.FaceCount == 0 || pixelError <= 0) return 0;

        // Pixels per radian at the camera's vertical FOV
        float pixelsPerRadian = s.ImageHeight / s.VerticalFovRadians;

        float longestPixelEdge = 0f;
        foreach (var f in mesh.FacePositions)
        {
            for (int i = 0; i < f.Length; i++)
            {
                Vector3 aLocal = mesh.Positions[f[i]];
                Vector3 bLocal = mesh.Positions[f[(i + 1) % f.Length]];

                Vector3 aWorld = Vector3.Transform(aLocal, s.EntityToWorld);
                Vector3 bWorld = Vector3.Transform(bLocal, s.EntityToWorld);

                // Depth = projection onto camera forward axis. Floor at a
                // small positive value so points behind the camera or
                // exactly on it don't blow up the pixel size estimate.
                Vector3 midRel = 0.5f * (aWorld + bWorld) - s.CameraOrigin;
                float depth = Vector3.Dot(midRel, s.CameraForward);
                if (depth < 1e-3f) depth = 1e-3f;

                float edgeLen = (bWorld - aWorld).Length();
                float pixelEdge = edgeLen / depth * pixelsPerRadian;
                if (pixelEdge > longestPixelEdge) longestPixelEdge = pixelEdge;
            }
        }

        if (longestPixelEdge <= pixelError) return 0;
        float ratio = longestPixelEdge / pixelError;
        return (int)MathF.Ceiling(MathF.Log2(ratio));
    }

    /// <summary>
    /// Converts a subdivided <see cref="PolyMesh"/> into a list of
    /// <see cref="SmoothTriangle"/> instances. Quad faces are fan-triangulated
    /// (0,1,2) + (0,2,3) — Catmull-Clark always emits convex quads so the fan
    /// pivot is safe.
    ///
    /// <para>
    /// Per-vertex normals are recomputed from the subdivided geometry as the
    /// angle-weighted average of incident face normals (Max 1999, the
    /// industry-standard smooth-shading recipe used by Blender's "Auto Smooth"
    /// and Maya's default). This overrides any normals that may have been
    /// propagated through subdivision because the limit surface is *smoother*
    /// than the originally interpolated input normals — the latter would
    /// reintroduce the polygonal silhouette in the shading.
    /// </para>
    /// </summary>
    public static List<IHittable> Triangulate(PolyMesh mesh, IMaterial material)
    {
        var output = new List<IHittable>(mesh.FaceCount * 2);

        // ── Step 1: triangulate quads / n-gons → triangle list with the
        // mesh's *original* attribute indices preserved per corner. We
        // need this to compute per-vertex normals correctly (and to emit
        // SmoothTriangles with the right UV pickup).
        var triPos = new List<int[]>(mesh.FaceCount * 2);
        var triUv  = mesh.HasUVs     ? new List<int[]>(mesh.FaceCount * 2) : null;

        for (int fi = 0; fi < mesh.FaceCount; fi++)
        {
            int[] f = mesh.FacePositions[fi];
            int[]? fu = mesh.HasUVs ? mesh.FaceUVs![fi] : null;

            // Fan triangulation (0,k,k+1) for k = 1 ... len-2
            for (int k = 1; k < f.Length - 1; k++)
            {
                triPos.Add(new[] { f[0], f[k], f[k + 1] });
                if (triUv != null)
                    triUv.Add(new[] { fu![0], fu[k], fu[k + 1] });
            }
        }

        // ── Step 2: angle-weighted vertex normals from the triangulated
        // limit surface. The angle weighting reduces seam artefacts at
        // T-junctions where the simple area-weighted average would bias
        // toward larger neighbors.
        var vertexNormals = new Vector3[mesh.Positions.Count];
        for (int i = 0; i < triPos.Count; i++)
        {
            var t = triPos[i];
            Vector3 p0 = mesh.Positions[t[0]];
            Vector3 p1 = mesh.Positions[t[1]];
            Vector3 p2 = mesh.Positions[t[2]];

            Vector3 e01 = p1 - p0;
            Vector3 e12 = p2 - p1;
            Vector3 e20 = p0 - p2;

            Vector3 faceN = Vector3.Cross(e01, -e20);
            float faceNLen = faceN.Length();
            if (faceNLen < 1e-12f) continue;
            faceN /= faceNLen;

            // Angles at each corner via cross/dot
            float a0 = CornerAngle( e01, -e20);
            float a1 = CornerAngle(-e01,  e12);
            float a2 = CornerAngle(-e12,  e20);

            vertexNormals[t[0]] += faceN * a0;
            vertexNormals[t[1]] += faceN * a1;
            vertexNormals[t[2]] += faceN * a2;
        }

        // Normalize (or fall back to triangle-face-normal for orphan
        // vertices that never received a contribution — degenerate input).
        for (int i = 0; i < vertexNormals.Length; i++)
        {
            float len = vertexNormals[i].Length();
            vertexNormals[i] = len > 1e-12f ? vertexNormals[i] / len : Vector3.UnitY;
        }

        // ── Step 3: emit SmoothTriangles ──────────────────────────────────
        for (int i = 0; i < triPos.Count; i++)
        {
            var t = triPos[i];
            Vector3 p0 = mesh.Positions[t[0]];
            Vector3 p1 = mesh.Positions[t[1]];
            Vector3 p2 = mesh.Positions[t[2]];

            // Degenerate-triangle filter — same threshold as ObjLoader
            Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);
            if (cross.LengthSquared() < 1e-12f) continue;

            Vector3 n0 = vertexNormals[t[0]];
            Vector3 n1 = vertexNormals[t[1]];
            Vector3 n2 = vertexNormals[t[2]];

            if (triUv != null)
            {
                var u = triUv[i];
                output.Add(new SmoothTriangle(
                    p0, p1, p2, n0, n1, n2,
                    mesh.UVs![u[0]], mesh.UVs[u[1]], mesh.UVs[u[2]],
                    material));
            }
            else
            {
                output.Add(new SmoothTriangle(p0, p1, p2, n0, n1, n2, material));
            }
        }

        return output;
    }

    /// <summary>
    /// Angle between two edge vectors emanating from the same triangle
    /// corner. Clamped to <c>[-1, 1]</c> before <c>acos</c> to absorb
    /// floating-point error on near-degenerate angles.
    /// </summary>
    private static float CornerAngle(Vector3 e1, Vector3 e2)
    {
        float len1 = e1.Length();
        float len2 = e2.Length();
        if (len1 < 1e-12f || len2 < 1e-12f) return 0f;
        float c = Vector3.Dot(e1, e2) / (len1 * len2);
        c = Math.Clamp(c, -1f, 1f);
        return MathF.Acos(c);
    }
}
