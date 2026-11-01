using System.Numerics;
using System.Linq;
using System.Runtime.CompilerServices;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Textures;
using RayTracer.Volumetrics;

namespace RayTracer.Rendering;

// Renderer, part: AOV capture and the dual-buffer accumulator.
public partial class Renderer
{
    // ═════════════════════════════════════════════════════════════════════════
    // AOV CAPTURE (denoiser guide buffers)
    // ═════════════════════════════════════════════════════════════════════════

    // Whether the current Render call captures AOVs. Every per-hit hook is a
    // single test on this bool when capture is off, and no hook ever draws
    // from the sampler — the path tracer's random stream (and therefore the
    // image) is identical with capture on or off.
    private bool _captureAovs;

    // Per-thread probe for the camera path currently being traced. A path
    // stays on one thread, so thread-static state is safe; the probe
    // object is allocated once per worker and reset per camera sample.
    [ThreadStatic] private static AovProbe? t_aovProbe;

    /// <summary>
    /// Records the denoiser guide features of one camera path, following the
    /// first-non-delta-hit convention: depth at the first surface hit of the
    /// camera ray; albedo/normal at the first non-delta (rough/diffuse)
    /// surface, with perfect-specular chains accumulating their tint into
    /// <see cref="AlbedoWeight"/> (a mirror pixel's albedo is the mirror tint
    /// times what it reflects). Environment misses commit the sky colour with
    /// zero normal; medium scatter events commit featureless white.
    /// </summary>
    private sealed class AovProbe
    {
        public bool Pending;
        public bool HaveDepth;
        public float Depth;
        public Vector3 AlbedoWeight;
        public Vector3 TentAlbedo;
        public Vector3 TentNormal;
        public Vector3 Albedo;
        public Vector3 Normal;

        public void Reset()
        {
            Pending = true;
            HaveDepth = false;
            Depth = -1f;
            AlbedoWeight = Vector3.One;
            TentAlbedo = Vector3.One;
            TentNormal = Vector3.Zero;
            Albedo = Vector3.Zero;
            Normal = Vector3.Zero;
        }

        /// <summary>Commit at (or falling back to) the last observed surface.</summary>
        public void Commit()
        {
            Albedo = AlbedoWeight * TentAlbedo;
            Normal = TentNormal;
            Pending = false;
        }

        /// <summary>Commit with explicit features (sky / medium events).</summary>
        public void CommitAs(Vector3 albedo, Vector3 normal)
        {
            Albedo = AlbedoWeight * albedo;
            Normal = normal;
            Pending = false;
        }
    }

    /// <summary>
    /// Per-pixel side accumulators for the optional capture: dual-buffer
    /// beauty halves plus AOV halves (<see cref="RenderBuffers"/>). Lives on the worker's
    /// stack next to the legacy accumulator; zero allocations.
    /// </summary>
    private struct CaptureAccumulator
    {
        private Vector3 _sumA, _sumB;
        private Vector3 _albA, _albB, _nrmA, _nrmB;
        private float _depA, _depB;
        private int _depCntA, _depCntB;
        private int _aovCntA, _aovCntB;
        private int _nA, _nB;

        public readonly int CountA => _nA;
        public readonly int CountB => _nB;
        public readonly Vector3 SumA => _sumA;
        public readonly Vector3 SumB => _sumB;

        /// <summary>Adds one sample to half A (<paramref name="isA"/>) or B.</summary>
        public void Add(Vector3 sample, bool isA, bool aovs)
        {
            if (isA) { _sumA += sample; _nA++; } else { _sumB += sample; _nB++; }
            if (!aovs) return;
            if (isA) _aovCntA++; else _aovCntB++;

            var p = t_aovProbe!;
            // Paths that ended without a non-delta vertex (RR kill, emissive
            // hit, depth exhausted, absorbed) commit their tentative surface.
            if (p.Pending) p.Commit();
            if (isA)
            {
                _albA += p.Albedo;
                _nrmA += p.Normal;
                if (p.HaveDepth) { _depA += p.Depth; _depCntA++; }
            }
            else
            {
                _albB += p.Albedo;
                _nrmB += p.Normal;
                if (p.HaveDepth) { _depB += p.Depth; _depCntB++; }
            }
        }

        /// <summary>
        /// Adaptive sampling's doubling passes: the samples so far become half
        /// A, the next pass fills B, so the halves stay the first and second
        /// half of the pixel's sequence.
        /// </summary>
        public void Promote()
        {
            _sumA += _sumB; _sumB = Vector3.Zero;
            _nA += _nB; _nB = 0;
            _albA += _albB; _albB = Vector3.Zero;
            _nrmA += _nrmB; _nrmB = Vector3.Zero;
            _depA += _depB; _depB = 0f;
            _depCntA += _depCntB; _depCntB = 0;
            _aovCntA += _aovCntB; _aovCntB = 0;
        }

        /// <summary>
        /// Divides the half sums into means and writes one pixel of every
        /// captured plane. With a single sample (nB == 0) the B half mirrors
        /// A so downstream variance estimates read as zero.
        /// </summary>
        public readonly void Store(RenderBuffers buffers, int x, int y,
                                   Vector3 linearColor, int samples)
        {
            int nA = Math.Max(_nA, 1), nB = _nB;
            buffers.Beauty.SetRgb(x, y, linearColor);
            Vector3 meanA = _sumA / nA;
            Vector3 meanB = nB > 0 ? _sumB / nB : meanA;
            buffers.BeautyA.SetRgb(x, y, meanA);
            buffers.BeautyB.SetRgb(x, y, meanB);
            buffers.SampleCount[y * buffers.Width + x] = samples;
            if (buffers.HalfCountA != null) buffers.HalfCountA[y * buffers.Width + x] = _nA;

            if (!buffers.HasAovs) return;
            // The guides come from the render's own samples only (training
            // samples carry no guides), so they divide by their own counts.
            int aA = Math.Max(_aovCntA, 1), aB = _aovCntB;
            Vector3 albA = _albA / aA;
            Vector3 albB = aB > 0 ? _albB / aB : albA;
            Vector3 nrmA = _nrmA / aA;
            Vector3 nrmB = aB > 0 ? _nrmB / aB : nrmA;
            float depA = _depCntA > 0 ? _depA / _depCntA : -1f;
            float depB = aB > 0 ? (_depCntB > 0 ? _depB / _depCntB : -1f) : depA;
            buffers.AlbedoA!.SetRgb(x, y, albA);
            buffers.AlbedoB!.SetRgb(x, y, albB);
            buffers.NormalA!.SetRgb(x, y, nrmA);
            buffers.NormalB!.SetRgb(x, y, nrmB);
            buffers.DepthA!.Set(0, x, y, depA);
            buffers.DepthB!.Set(0, x, y, depB);
        }
    }
}
