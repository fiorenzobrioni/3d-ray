using System.Globalization;
using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Lights;

/// <summary>
/// A luminaire's photometric distribution from an IES LM-63 file (type C
/// photometry, the one manufacturers publish): candela as a function of the
/// vertical angle (0° = nadir, straight down the light's axis) and the
/// horizontal angle around that axis.
///
/// <para>
/// Values are absolute candela (the file's multiplier and ballast factors
/// applied), so a light that uses the profile as it is emits the luminaire's
/// real flux in a scene measured in nits (<see cref="LightUnits"/>). Missing
/// horizontal planes follow the file's symmetry: one plane is rotationally
/// symmetric, 0-90° mirrors into four quadrants, 0-180° into two halves.
/// TILT data are not supported (TILT=INCLUDE is skipped with a warning by the
/// loader, the luminaire's tilt being taken as its mounting).
/// </para>
/// </summary>
public sealed class IesProfile
{
    private readonly float[] _vertical;     // degrees, ascending
    private readonly float[] _horizontal;   // degrees, ascending, after symmetry expansion
    private readonly float[,] _candela;     // [h, v]

    /// <summary>Total flux of the distribution (lm), integrated numerically.</summary>
    public float Flux { get; }

    /// <summary>Highest candela value of the distribution.</summary>
    public float MaxCandela { get; }

    /// <summary>True when the file carried TILT data that was ignored.</summary>
    public bool TiltIgnored { get; }

    private IesProfile(float[] vertical, float[] horizontal, float[,] candela, bool tiltIgnored)
    {
        _vertical = vertical;
        _horizontal = horizontal;
        _candela = candela;
        TiltIgnored = tiltIgnored;
        float max = 0f;
        foreach (float c in candela) max = MathF.Max(max, c);
        MaxCandela = max;
        Flux = Integrate();
    }

    public static IesProfile Load(string path) => Parse(File.ReadAllText(path));

    public static IesProfile Parse(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        int i = 0;
        while (i < lines.Length && !lines[i].TrimStart().StartsWith("TILT", StringComparison.OrdinalIgnoreCase)) i++;
        if (i >= lines.Length) throw new FormatException("IES: no TILT line.");
        string tilt = lines[i][(lines[i].IndexOf('=') + 1)..].Trim();

        // Everything after the TILT line is one stream of numbers: a section
        // may end and the next begin on the same line.
        var nums = new Queue<float>(lines.Skip(i + 1)
            .SelectMany(l => l.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(Num));
        float Next() => nums.Count > 0 ? nums.Dequeue() : throw new FormatException("IES: file ends early.");

        bool tiltIgnored = !tilt.Equals("NONE", StringComparison.OrdinalIgnoreCase);
        if (tilt.Equals("INCLUDE", StringComparison.OrdinalIgnoreCase))
        {
            Next();                               // lamp-to-luminaire geometry
            int n = (int)Next();
            for (int k = 0; k < 2 * n; k++) Next(); // angles and factors
        }

        Next();                                   // number of lamps
        Next();                                   // lumens per lamp (-1 = absolute)
        float multiplier = Next();
        int nv = (int)Next();
        int nh = (int)Next();
        int type = (int)Next();
        if (type != 1) throw new NotSupportedException($"IES: photometric type {type} not supported (type C only).");
        for (int k = 0; k < 4; k++) Next();       // units, width, length, height
        float ballast = Next();
        float ballastLamp = Next();
        Next();                                   // input watts
        float scale = multiplier * ballast * (ballastLamp > 0f ? ballastLamp : 1f);

        var v = new float[nv];
        for (int k = 0; k < nv; k++) v[k] = Next();
        var h = new float[nh];
        for (int k = 0; k < nh; k++) h[k] = Next();
        var cd = new float[nh, nv];
        for (int a = 0; a < nh; a++)
            for (int b = 0; b < nv; b++)
                cd[a, b] = MathF.Max(0f, Next() * scale);

        return Expand(v, h, cd, tiltIgnored);
    }

    /// <summary>Fills the horizontal planes a symmetric file leaves out, to a full 0-360° turn.</summary>
    private static IesProfile Expand(float[] v, float[] h, float[,] cd, bool tilt)
    {
        int nv = v.Length;
        float last = h[^1];
        if (h.Length == 1)
            return new IesProfile(v, new[] { 0f, 360f }, Planes(cd, nv, new[] { 0, 0 }), tilt);

        var angles = new List<float>(h);
        var source = Enumerable.Range(0, h.Length).ToList();
        if (MathF.Abs(last - 90f) < 1e-3f)
        {
            // Quadrant: mirror to 180, then to 360.
            for (int k = h.Length - 2; k >= 0; k--) { angles.Add(180f - h[k]); source.Add(k); }
        }
        if (MathF.Abs(angles[^1] - 180f) < 1e-3f)
        {
            int n = angles.Count;
            for (int k = n - 2; k >= 0; k--) { angles.Add(360f - angles[k]); source.Add(source[k]); }
        }
        if (angles[^1] < 360f - 1e-3f) { angles.Add(360f); source.Add(source[0]); }
        return new IesProfile(v, angles.ToArray(), Planes(cd, nv, source.ToArray()), tilt);
    }

    private static float[,] Planes(float[,] cd, int nv, int[] source)
    {
        var o = new float[source.Length, nv];
        for (int a = 0; a < source.Length; a++)
            for (int b = 0; b < nv; b++)
                o[a, b] = cd[source[a], b];
        return o;
    }

    /// <summary>
    /// Candela toward the direction at <paramref name="verticalDeg"/> from the
    /// nadir and <paramref name="horizontalDeg"/> around it; bilinear between
    /// the file's angles, zero beyond its last vertical angle.
    /// </summary>
    public float Candela(float verticalDeg, float horizontalDeg)
    {
        if (verticalDeg < _vertical[0] || verticalDeg > _vertical[^1]) return 0f;
        float hDeg = horizontalDeg - 360f * MathF.Floor(horizontalDeg / 360f);
        var (v0, fv) = Locate(_vertical, verticalDeg);
        var (h0, fh) = Locate(_horizontal, hDeg);
        int v1 = Math.Min(v0 + 1, _vertical.Length - 1), h1 = Math.Min(h0 + 1, _horizontal.Length - 1);
        float a = _candela[h0, v0] + fv * (_candela[h0, v1] - _candela[h0, v0]);
        float b = _candela[h1, v0] + fv * (_candela[h1, v1] - _candela[h1, v0]);
        return a + fh * (b - a);
    }

    /// <summary>
    /// Candela toward world direction <paramref name="dir"/> (away from the
    /// light) for a luminaire whose nadir is <paramref name="axis"/> and whose
    /// 0° horizontal plane contains <paramref name="reference"/>.
    /// </summary>
    public float Candela(Vector3 dir, Vector3 axis, Vector3 reference)
    {
        Vector3 side = Vector3.Cross(axis, reference);
        float c = Math.Clamp(Vector3.Dot(dir, axis), -1f, 1f);
        float vertical = MathF.Acos(c) * (180f / MathF.PI);
        float horizontal = MathF.Atan2(Vector3.Dot(dir, side), Vector3.Dot(dir, reference)) * (180f / MathF.PI);
        return Candela(vertical, horizontal);
    }

    private static (int Index, float Frac) Locate(float[] a, float x)
    {
        int hi = Array.BinarySearch(a, x);
        if (hi >= 0) return (Math.Min(hi, a.Length - 1), 0f);
        hi = ~hi;
        if (hi <= 0) return (0, 0f);
        if (hi >= a.Length) return (a.Length - 1, 0f);
        int lo = hi - 1;
        float span = a[hi] - a[lo];
        return (lo, span > 0f ? (x - a[lo]) / span : 0f);
    }

    private float Integrate()
    {
        const int nTheta = 720, nPhi = 360;
        double sum = 0;
        for (int i = 0; i < nTheta; i++)
        {
            double theta = (i + 0.5) * Math.PI / nTheta;
            double ring = 0;
            for (int j = 0; j < nPhi; j++)
                ring += Candela((float)(theta * 180 / Math.PI), (j + 0.5f) * 360f / nPhi);
            sum += ring / nPhi * Math.Sin(theta);
        }
        return (float)(sum * Math.PI / nTheta * 2 * Math.PI);
    }

    private static float Num(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}
