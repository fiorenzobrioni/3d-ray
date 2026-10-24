using System.Numerics;

namespace RayTracer.Rendering;

/// <summary>
/// Hashed uniform grid over the light vertices of an iteration, for the
/// range queries of vertex merging: cells of twice the merging radius, so the
/// vertices within the radius of a point lie in the 2 × 2 × 2 cells around it.
/// Built with a counting sort; hash collisions only add candidates, which the
/// caller rejects by distance.
/// </summary>
internal sealed class VertexGrid
{
    private int[] _cellStart = Array.Empty<int>();
    private int[] _indices = Array.Empty<int>();
    private int _tableSize;
    private float _invCell;
    private Vector3 _min;

    public void Build(ReadOnlySpan<Vector3> positions, ReadOnlySpan<bool> include, float radius)
    {
        int n = positions.Length;
        _tableSize = Math.Max(1, n);
        _invCell = 1f / (2f * radius);
        _min = new Vector3(float.MaxValue);
        for (int i = 0; i < n; i++)
            if (include[i]) _min = Vector3.Min(_min, positions[i]);
        if (_cellStart.Length < _tableSize + 1) _cellStart = new int[_tableSize + 1];
        else Array.Clear(_cellStart, 0, _tableSize + 1);
        if (_indices.Length < n) _indices = new int[n];

        var cell = new int[n];
        for (int i = 0; i < n; i++)
        {
            if (!include[i]) { cell[i] = -1; continue; }
            cell[i] = Hash(CellOf(positions[i]));
            _cellStart[cell[i] + 1]++;
        }
        for (int c = 0; c < _tableSize; c++) _cellStart[c + 1] += _cellStart[c];
        var fill = new int[_tableSize];
        for (int i = 0; i < n; i++)
            if (cell[i] >= 0) _indices[_cellStart[cell[i]] + fill[cell[i]]++] = i;
    }

    private (int X, int Y, int Z) CellOf(Vector3 p)
    {
        Vector3 c = (p - _min) * _invCell;
        return ((int)MathF.Floor(c.X), (int)MathF.Floor(c.Y), (int)MathF.Floor(c.Z));
    }

    private int Hash((int X, int Y, int Z) c)
    {
        uint h = unchecked((uint)c.X * 73856093u ^ (uint)c.Y * 19349663u ^ (uint)c.Z * 83492791u);
        return (int)(h % (uint)_tableSize);
    }

    /// <summary>
    /// The candidates near <paramref name="p"/>: the 8 cells whose union holds
    /// every point within the radius. <paramref name="cells"/> receives the
    /// [start, end) ranges into <see cref="Indices"/> (duplicates when cells
    /// hash alike are removed).
    /// </summary>
    public int Candidates(Vector3 p, Span<(int Start, int End)> cells)
    {
        Vector3 c = (p - _min) * _invCell;
        int x0 = (int)MathF.Floor(c.X - 0.5f), y0 = (int)MathF.Floor(c.Y - 0.5f), z0 = (int)MathF.Floor(c.Z - 0.5f);
        int count = 0;
        Span<int> seen = stackalloc int[8];
        for (int dz = 0; dz < 2; dz++)
        for (int dy = 0; dy < 2; dy++)
        for (int dx = 0; dx < 2; dx++)
        {
            int h = Hash((x0 + dx, y0 + dy, z0 + dz));
            bool dup = false;
            for (int k = 0; k < count; k++) if (seen[k] == h) { dup = true; break; }
            if (dup) continue;
            seen[count] = h;
            cells[count++] = (_cellStart[h], _cellStart[h + 1]);
        }
        return count;
    }

    public ReadOnlySpan<int> Indices => _indices;
}
