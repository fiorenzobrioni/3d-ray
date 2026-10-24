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

        if (options.CreaseAngle > 0f)
            MarkCreases(mesh, options.CreaseAngle,
                        options.CreaseSharpness > 0f ? options.CreaseSharpness : float.PositiveInfinity);

        // ── Run the algorithm, then move the vertices onto the limit surface ─
        switch (scheme)
        {
            case SubdivisionScheme.Loop:
                LoopSubdivider.Subdivide(mesh, iters, options.Boundary);
                break;
            case SubdivisionScheme.CatmullClark:
                CatmullClarkSubdivider.Subdivide(mesh, iters, options.Boundary);
                break;
            default:
                return (0, scheme);
        }
        LimitSurface.Project(mesh, scheme, options.Boundary);

        return (iters, scheme);
    }

    /// <summary>
    /// Marks as creases the edges whose two faces meet at more than
    /// <paramref name="angleDegrees"/> (the angle between their normals),
    /// keeping any crease already on the mesh.
    /// </summary>
    internal static void MarkCreases(PolyMesh mesh, float angleDegrees, float sharpness)
    {
        float cosLimit = MathF.Cos(angleDegrees * MathF.PI / 180f);
        var faceNormals = new Vector3[mesh.FaceCount];
        for (int fi = 0; fi < mesh.FaceCount; fi++)
            faceNormals[fi] = LimitSurface.FaceNormal(mesh, mesh.FacePositions[fi]);
        var topo = new SubdivisionTopology(mesh, BoundaryInterpolation.EdgeAndCorner);
        foreach (var (key, e) in topo.Edges)
        {
            if (e.FaceCount != 2) continue;
            if (Vector3.Dot(faceNormals[e.Face0], faceNormals[e.Face1]) < cosLimit)
            {
                mesh.Creases ??= new Dictionary<long, float>();
                mesh.Creases[key] = mesh.Creases.TryGetValue(key, out float s) ? MathF.Max(s, sharpness) : sharpness;
            }
        }
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
    /// <see cref="SmoothTriangle"/> instances. Polygons are fan-triangulated
    /// (0, k, k+1); Catmull-Clark emits convex quads, so the fan is safe.
    ///
    /// <para>Shading normals per face corner: at a smooth vertex the limit
    /// surface's normal when the vertices are on it
    /// (<see cref="PolyMesh.LimitNormals"/>), otherwise the angle-weighted
    /// mean of its faces; at a vertex on a crease, a boundary or a corner,
    /// the angle-weighted mean of the faces of its own sector (the faces
    /// reached around the vertex without crossing a sharp edge), so a crease
    /// stays sharp in the shading too. Normals given with the input are not
    /// used: after subdivision or displacement they no longer belong to the
    /// surface.</para>
    /// </summary>
    public static List<IHittable> Triangulate(PolyMesh mesh, IMaterial material)
        => Triangulate(mesh, new[] { material });

    /// <summary>
    /// As <see cref="Triangulate(PolyMesh, IMaterial)"/>, each face with the
    /// material of its slot (<see cref="PolyMesh.FaceMaterials"/>; slot 0 when
    /// the mesh has none).
    /// </summary>
    public static List<IHittable> Triangulate(PolyMesh mesh, IReadOnlyList<IMaterial> materials)
    {
        var output = new List<IHittable>(mesh.FaceCount * 2);
        var corner = CornerNormals(mesh);

        for (int fi = 0; fi < mesh.FaceCount; fi++)
        {
            int[] f = mesh.FacePositions[fi];
            int[]? fu = mesh.HasUVs ? mesh.FaceUVs![fi] : null;
            Vector3[] cn = corner[fi];
            IMaterial material = materials[mesh.FaceMaterials?[fi] ?? 0];
            foreach (var (a, b, c) in mesh.TriangulateFace(f))
            {
                Vector3 p0 = mesh.Positions[f[a]];
                Vector3 p1 = mesh.Positions[f[b]];
                Vector3 p2 = mesh.Positions[f[c]];

                // Only an exactly degenerate triangle is dropped: a threshold on
                // the area would drop every triangle of a small enough model.
                if (Vector3.Cross(p1 - p0, p2 - p0) == Vector3.Zero) continue;

                if (fu is { Length: > 0 })
                    output.Add(new SmoothTriangle(p0, p1, p2, cn[a], cn[b], cn[c],
                        mesh.UVs![fu[a]], mesh.UVs[fu[b]], mesh.UVs[fu[c]], material));
                else
                    output.Add(new SmoothTriangle(p0, p1, p2, cn[a], cn[b], cn[c], material));
            }
        }

        return output;
    }

    /// <summary>The shading normal of every face corner (see <see cref="Triangulate"/>).</summary>
    internal static Vector3[][] CornerNormals(PolyMesh mesh)
    {
        var topo = new SubdivisionTopology(mesh, BoundaryInterpolation.EdgeAndCorner);
        var faceNormals = new Vector3[mesh.FaceCount];
        for (int fi = 0; fi < mesh.FaceCount; fi++)
            faceNormals[fi] = LimitSurface.FaceNormal(mesh, mesh.FacePositions[fi]);
        var smooth = mesh.LimitNormals ?? LimitSurface.AngleWeightedNormals(mesh);

        var result = new Vector3[mesh.FaceCount][];
        var sector = new List<int>(8);
        for (int fi = 0; fi < mesh.FaceCount; fi++)
        {
            int[] f = mesh.FacePositions[fi];
            var cn = result[fi] = new Vector3[f.Length];
            for (int k = 0; k < f.Length; k++)
            {
                int v = f[k];
                bool sharp = false;
                foreach (long key in topo.VertexEdges[v])
                    if (topo.Edges[key].Sharpness > 0f) { sharp = true; break; }
                if (!sharp) { cn[k] = smooth[v]; continue; }

                // The faces around v reachable from fi across smooth edges.
                sector.Clear();
                sector.Add(fi);
                for (int at = 0; at < sector.Count; at++)
                {
                    int[] g = mesh.FacePositions[sector[at]];
                    int kg = Array.IndexOf(g, v);
                    foreach (int other in new[] { g[(kg + 1) % g.Length], g[(kg - 1 + g.Length) % g.Length] })
                    {
                        var e = topo.Edges[SubdivisionTopology.EdgeKey(v, other)];
                        if (e.Sharpness > 0f) continue;
                        int next = e.Face0 == sector[at] ? e.Face1 : e.Face0;
                        if (next >= 0 && !sector.Contains(next)) sector.Add(next);
                    }
                }
                Vector3 n = Vector3.Zero;
                foreach (int g in sector)
                {
                    int[] gf = mesh.FacePositions[g];
                    n += LimitSurface.CornerAngle(mesh, gf, Array.IndexOf(gf, v)) * faceNormals[g];
                }
                float len = n.Length();
                cn[k] = len > 1e-20f ? n / len : (faceNormals[fi] != Vector3.Zero ? faceNormals[fi] : smooth[v]);
            }
        }
        return result;
    }
}
