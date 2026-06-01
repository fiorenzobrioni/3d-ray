using System.Numerics;

namespace RayTracer.Rendering;

/// <summary>
/// A spatial acceleration structure over caustic <see cref="Photon"/>s built
/// once after the photon-emission pre-pass and queried during the camera pass.
///
/// <para><b>Structure.</b> A uniform spatial hash grid (Teschner et al. 2003):
/// each photon is bucketed by its quantised cell coordinate, hashed into a
/// fixed power-of-two bucket table, and stored in a flat CSR array via a single
/// counting sort — zero per-query allocation and cache-friendly bucket scans.
/// Hash collisions (distinct cells sharing a bucket) are resolved at query time
/// by re-deriving each candidate photon's own cell, so a photon is visited
/// exactly once per query and the radius query is <b>exact</b> (validated
/// against a brute-force oracle in the tests).</para>
///
/// <para><b>Queries.</b> <see cref="QueryRadius(System.Numerics.Vector3,float,System.Collections.Generic.List{int})"/>
/// returns every photon within a radius (the tested primitive);
/// <see cref="GatherKNearest"/> returns the k nearest within a cap using an
/// expanding-shell search with a provable early-out, giving the adaptive kernel
/// radius the density estimate needs — no per-pixel allocation.</para>
/// </summary>
public sealed class PhotonMap
{
    private readonly Photon[] _photons;
    private readonly int[]    _bucketStart; // CSR offsets, length _nBuckets + 1
    private readonly int[]    _indices;     // photon indices grouped by bucket
    private readonly Vector3  _origin;
    private readonly float    _cellSize;
    private readonly float    _invCellSize;
    private readonly int      _mask;        // _nBuckets - 1 (power of two)

    public int Count => _photons.Length;
    public ReadOnlySpan<Photon> Photons => _photons;
    /// <summary>Grid cell size — a sensible default gather radius scale.</summary>
    public float CellSize => _cellSize;

    public PhotonMap(Photon[] photons, float cellSize)
    {
        _photons     = photons ?? throw new ArgumentNullException(nameof(photons));
        _cellSize    = MathF.Max(cellSize, 1e-6f);
        _invCellSize = 1f / _cellSize;

        // Bucket table sized to the next power of two ≥ photon count keeps the
        // average bucket occupancy near one and the mask-AND hash cheap.
        int n = _photons.Length;
        int buckets = 1;
        while (buckets < n) buckets <<= 1;
        _mask = buckets - 1;

        // Origin at the photon-cloud min so cell coordinates stay small. An
        // empty map degenerates gracefully (origin = zero, no buckets walked).
        _origin = Vector3.Zero;
        if (n > 0)
        {
            var min = new Vector3(float.PositiveInfinity);
            for (int i = 0; i < n; i++) min = Vector3.Min(min, _photons[i].Position);
            _origin = min;
        }

        // Counting sort into CSR buckets.
        _bucketStart = new int[buckets + 1];
        for (int i = 0; i < n; i++)
            _bucketStart[BucketOf(_photons[i].Position) + 1]++;
        for (int b = 1; b <= buckets; b++)
            _bucketStart[b] += _bucketStart[b - 1];

        _indices = new int[n];
        var cursor = new int[buckets];
        Array.Copy(_bucketStart, cursor, buckets);
        for (int i = 0; i < n; i++)
        {
            int b = BucketOf(_photons[i].Position);
            _indices[cursor[b]++] = i;
        }
    }

    private void CellOf(Vector3 p, out int x, out int y, out int z)
    {
        x = (int)MathF.Floor((p.X - _origin.X) * _invCellSize);
        y = (int)MathF.Floor((p.Y - _origin.Y) * _invCellSize);
        z = (int)MathF.Floor((p.Z - _origin.Z) * _invCellSize);
    }

    private int BucketOf(Vector3 p)
    {
        CellOf(p, out int x, out int y, out int z);
        return Bucket(x, y, z);
    }

    private int Bucket(int x, int y, int z)
    {
        // Teschner spatial hash; the mask folds it onto the bucket table.
        uint h = (uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)z * 83492791u;
        return (int)(h & (uint)_mask);
    }

    /// <summary>
    /// Exact radius query: appends to <paramref name="result"/> the index of
    /// every photon whose position lies within <paramref name="radius"/> of
    /// <paramref name="p"/>. Allocation-light; used by tests as the reference
    /// primitive and rarely on the hot path (the camera pass uses
    /// <see cref="GatherKNearest"/>).
    /// </summary>
    public void QueryRadius(Vector3 p, float radius, List<int> result)
    {
        if (_photons.Length == 0 || radius <= 0f) return;
        float r2 = radius * radius;

        int cx0 = (int)MathF.Floor((p.X - radius - _origin.X) * _invCellSize);
        int cx1 = (int)MathF.Floor((p.X + radius - _origin.X) * _invCellSize);
        int cy0 = (int)MathF.Floor((p.Y - radius - _origin.Y) * _invCellSize);
        int cy1 = (int)MathF.Floor((p.Y + radius - _origin.Y) * _invCellSize);
        int cz0 = (int)MathF.Floor((p.Z - radius - _origin.Z) * _invCellSize);
        int cz1 = (int)MathF.Floor((p.Z + radius - _origin.Z) * _invCellSize);

        for (int z = cz0; z <= cz1; z++)
        for (int y = cy0; y <= cy1; y++)
        for (int x = cx0; x <= cx1; x++)
        {
            int b = Bucket(x, y, z);
            for (int k = _bucketStart[b]; k < _bucketStart[b + 1]; k++)
            {
                int pi = _indices[k];
                Vector3 pos = _photons[pi].Position;
                // Resolve hash collisions: only count a photon while iterating
                // ITS cell, so each photon is tested at most once per query.
                CellOf(pos, out int px, out int py, out int pz);
                if (px != x || py != y || pz != z) continue;
                if (Vector3.DistanceSquared(pos, p) <= r2) result.Add(pi);
            }
        }
    }

    /// <summary>
    /// Allocation-free fixed-radius query: writes the indices of photons within
    /// <paramref name="radius"/> of <paramref name="p"/> into <paramref name="outIdx"/>
    /// (up to its capacity) and returns the count written. Used by the camera-pass
    /// density estimate, where the grid cell is sized to the gather radius so only
    /// the 3×3×3 neighbourhood is scanned — O(1) per gather, fast even in empty
    /// regions (the buckets are simply empty).
    /// </summary>
    public int QueryRadius(Vector3 p, float radius, Span<int> outIdx)
    {
        if (_photons.Length == 0 || radius <= 0f || outIdx.Length == 0) return 0;
        float r2 = radius * radius;
        int n = 0;

        int cx0 = (int)MathF.Floor((p.X - radius - _origin.X) * _invCellSize);
        int cx1 = (int)MathF.Floor((p.X + radius - _origin.X) * _invCellSize);
        int cy0 = (int)MathF.Floor((p.Y - radius - _origin.Y) * _invCellSize);
        int cy1 = (int)MathF.Floor((p.Y + radius - _origin.Y) * _invCellSize);
        int cz0 = (int)MathF.Floor((p.Z - radius - _origin.Z) * _invCellSize);
        int cz1 = (int)MathF.Floor((p.Z + radius - _origin.Z) * _invCellSize);

        for (int z = cz0; z <= cz1; z++)
        for (int y = cy0; y <= cy1; y++)
        for (int x = cx0; x <= cx1; x++)
        {
            int b = Bucket(x, y, z);
            for (int k = _bucketStart[b]; k < _bucketStart[b + 1]; k++)
            {
                int pi = _indices[k];
                Vector3 pos = _photons[pi].Position;
                CellOf(pos, out int px, out int py, out int pz);
                if (px != x || py != y || pz != z) continue; // collision guard
                if (Vector3.DistanceSquared(pos, p) > r2) continue;
                outIdx[n++] = pi;
                if (n >= outIdx.Length) return n;
            }
        }
        return n;
    }

    /// <summary>
    /// Caustic gather: the k nearest photons within <paramref name="maxRadius"/>,
    /// scanning only the cell neighbourhood (the grid cell is sized to the radius,
    /// so this is the 3×3×3 block — O(1), fast in empty regions) while still
    /// reporting the **adaptive** kernel radius (distance to the farthest of the
    /// k gathered). That adaptive radius is what keeps a focused caustic sharp and
    /// bright: in the dense focal spot the k nearest photons sit in a tiny disc,
    /// so the <c>1/(π r²)</c> density estimate spikes correctly. Allocation-free
    /// (the bounded max-heap of squared distances lives in <paramref name="heapD2"/>).
    /// Returns the photon count gathered.
    /// </summary>
    public int GatherCaustic(Vector3 p, int k, float maxRadius,
                             Span<int> outIdx, Span<float> heapD2, out float radius)
    {
        radius = 0f;
        if (_photons.Length == 0 || k <= 0 || maxRadius <= 0f) return 0;
        k = Math.Min(k, Math.Min(outIdx.Length, heapD2.Length));
        float r2 = maxRadius * maxRadius;
        int count = 0;

        int cx0 = (int)MathF.Floor((p.X - maxRadius - _origin.X) * _invCellSize);
        int cx1 = (int)MathF.Floor((p.X + maxRadius - _origin.X) * _invCellSize);
        int cy0 = (int)MathF.Floor((p.Y - maxRadius - _origin.Y) * _invCellSize);
        int cy1 = (int)MathF.Floor((p.Y + maxRadius - _origin.Y) * _invCellSize);
        int cz0 = (int)MathF.Floor((p.Z - maxRadius - _origin.Z) * _invCellSize);
        int cz1 = (int)MathF.Floor((p.Z + maxRadius - _origin.Z) * _invCellSize);

        for (int z = cz0; z <= cz1; z++)
        for (int y = cy0; y <= cy1; y++)
        for (int x = cx0; x <= cx1; x++)
        {
            int b = Bucket(x, y, z);
            for (int kk = _bucketStart[b]; kk < _bucketStart[b + 1]; kk++)
            {
                int pi = _indices[kk];
                Vector3 pos = _photons[pi].Position;
                CellOf(pos, out int px, out int py, out int pz);
                if (px != x || py != y || pz != z) continue;   // collision guard
                float d2 = Vector3.DistanceSquared(pos, p);
                if (d2 > r2) continue;
                HeapInsert(outIdx, heapD2, ref count, k, pi, d2);
            }
        }

        radius = count > 0 ? MathF.Sqrt(heapD2[0]) : 0f;
        return count;
    }

    /// <summary>
    /// Gathers up to <paramref name="k"/> nearest photons to <paramref name="p"/>
    /// within <paramref name="maxRadius"/>, writing their indices into
    /// <paramref name="outIdx"/> (capacity ≥ k) and reporting the kernel
    /// <paramref name="radius"/> (the distance to the farthest gathered photon).
    /// Allocation-free: a bounded max-heap of squared distances lives in
    /// <paramref name="heapD2"/> (capacity ≥ k). Returns the photon count found.
    ///
    /// <para>Expanding-shell search with a provable early-out: after scanning
    /// every cell within Chebyshev distance <c>ring</c>, all unscanned photons
    /// are at least <c>ring·cellSize</c> away, so once the heap is full and its
    /// farthest entry is within that bound the k nearest are guaranteed.</para>
    /// </summary>
    public int GatherKNearest(Vector3 p, int k, float maxRadius,
                              Span<int> outIdx, Span<float> heapD2, out float radius)
    {
        radius = 0f;
        if (_photons.Length == 0 || k <= 0) return 0;
        k = Math.Min(k, outIdx.Length);
        k = Math.Min(k, heapD2.Length);

        CellOf(p, out int cx, out int cy, out int cz);
        float maxR2 = maxRadius * maxRadius;
        int count = 0;                  // current heap size
        int maxRings = (int)MathF.Ceiling(maxRadius * _invCellSize) + 1;

        for (int ring = 0; ring <= maxRings; ring++)
        {
            ScanShell(p, cx, cy, cz, ring, k, maxR2, outIdx, heapD2, ref count);

            // Early-out: heap full and its farthest photon is closer than any
            // photon a farther ring could contain.
            float ringReach = ring * _cellSize;
            if (count >= k && heapD2[0] <= ringReach * ringReach) break;
            if (ringReach > maxRadius) break;
        }

        radius = count > 0 ? MathF.Sqrt(heapD2[0]) : 0f;
        return count;
    }

    /// <summary>Scans the Chebyshev shell at distance <paramref name="ring"/> (ring 0 = the centre cell), inserting qualifying photons into the bounded max-heap.</summary>
    private void ScanShell(Vector3 p, int cx, int cy, int cz, int ring, int k,
                           float maxR2, Span<int> outIdx, Span<float> heapD2, ref int count)
    {
        for (int z = cz - ring; z <= cz + ring; z++)
        for (int y = cy - ring; y <= cy + ring; y++)
        for (int x = cx - ring; x <= cx + ring; x++)
        {
            // Shell only: skip the interior already scanned by smaller rings.
            int cheb = Math.Max(Math.Abs(x - cx), Math.Max(Math.Abs(y - cy), Math.Abs(z - cz)));
            if (cheb != ring) continue;

            int b = Bucket(x, y, z);
            for (int kk = _bucketStart[b]; kk < _bucketStart[b + 1]; kk++)
            {
                int pi = _indices[kk];
                Vector3 pos = _photons[pi].Position;
                CellOf(pos, out int px, out int py, out int pz);
                if (px != x || py != y || pz != z) continue; // collision guard
                float d2 = Vector3.DistanceSquared(pos, p);
                if (d2 > maxR2) continue;
                HeapInsert(outIdx, heapD2, ref count, k, pi, d2);
            }
        }
    }

    /// <summary>Inserts (index, d²) into a bounded max-heap keyed by d², evicting the current farthest once full.</summary>
    private static void HeapInsert(Span<int> idx, Span<float> d2, ref int count, int k, int pi, float dist2)
    {
        if (count < k)
        {
            // Sift up.
            int i = count++;
            idx[i] = pi; d2[i] = dist2;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (d2[parent] >= d2[i]) break;
                (d2[parent], d2[i]) = (d2[i], d2[parent]);
                (idx[parent], idx[i]) = (idx[i], idx[parent]);
                i = parent;
            }
        }
        else if (dist2 < d2[0])
        {
            // Replace root (farthest) and sift down.
            idx[0] = pi; d2[0] = dist2;
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = 2 * i + 2, largest = i;
                if (l < count && d2[l] > d2[largest]) largest = l;
                if (r < count && d2[r] > d2[largest]) largest = r;
                if (largest == i) break;
                (d2[largest], d2[i]) = (d2[i], d2[largest]);
                (idx[largest], idx[i]) = (idx[i], idx[largest]);
                i = largest;
            }
        }
    }
}
