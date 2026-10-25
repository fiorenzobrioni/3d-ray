using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Volumetrics;

namespace RayTracer.Rendering;

/// <summary>
/// Bidirectional path tracing and vertex connection and merging
/// (PIANO-REVIEW.md Phase 8b), in the formulation of Georgiev, Křivánek,
/// Davidovič and Slusallek 2012 ("Light Transport Simulation with Vertex
/// Connection and Merging"): each subpath carries three running quantities
/// (dVCM, dVC, dVM) from which the balance-heuristic weight of every strategy
/// that could have made the same path (camera ray hitting a light, light
/// sample, connection of any two vertices, light tracing into the camera,
/// merging with a nearby light vertex) is computed in constant time.
/// <c>--integrator bdpt</c> is the same code without merging.
///
/// <para><b>Iterations.</b> One camera path per pixel per iteration; light
/// subpaths are traced in chunks of at most <see cref="MaxLightPathsPerChunk"/>
/// pixels: the chunk's light paths first (stored, and connected to the
/// camera: light tracing into all pixels, normalised by the image's pixel
/// count), then its camera paths, each connected to the light path of its own
/// index and merged with the chunk's light vertices.</para>
///
/// <para><b>Departures from the paper.</b> The light-sample densities of
/// sphere, rectangle and sky lights depend on the receiving point (solid-angle
/// and spherical-rectangle sampling), so dVCM of a light subpath is set at its
/// first vertex, where the receiver is known, instead of at the light.
/// Surfaces that transmit straight (index-matched shells, cut-outs, thin
/// sheets passed through) are null interfaces: they never become vertices,
/// connections cross them as shadow rays do, so no path is counted twice.
/// Light subpaths use the adjoint BSDF: the radiance factor (η_i/η_t)² of a
/// refraction is left out of the importance they carry. Russian roulette is
/// not part of the MIS densities (the weights still sum to one).</para>
///
/// <para><b>Media.</b> Scattering events are vertices whose BSDF is the
/// phase function (cosines 1); they are connected but never merged, so the
/// merging terms of the weights are dropped there. A subsurface entity
/// behind a refraction is walked by camera subpaths with the path tracer's
/// random walk, and light subpaths stop at it: the walk's last light sample
/// is the only partner of the emission met by the ray that leaves it.</para>
/// </summary>
public partial class Renderer
{
    /// <summary>The light transport algorithm.</summary>
    public IntegratorKind Integrator { get; set; } = IntegratorKind.Path;

    /// <summary>Merging radius at the first iteration, as a fraction of the scene's radius.</summary>
    public float VcmRadiusFactor { get; set; } = 0.003f;

    /// <summary>Radius reduction exponent α (radius ∝ iteration^((α−1)/2)), 0.75 as in the paper.</summary>
    public float VcmRadiusAlpha { get; set; } = 0.75f;

    /// <summary>Light subpaths stored at once (memory bound of the vertex store).</summary>
    private const int MaxLightPathsPerChunk = 1 << 18;

    // ── Vertices and subpath state ──────────────────────────────────────────

    /// <summary>A stored vertex of a light subpath.</summary>
    private struct BidiVertex
    {
        public HitRecord Rec;          // as hit (a medium vertex: its point and time)
        public IPhaseFunction? Phase;  // set at a medium vertex
        public Vector3 Fix;            // unit, toward the previous vertex
        public Vector3 Throughput;
        public float DVcm, DVc, DVm;
        public int PathLength;         // edges from the light
    }

    private struct SubPath
    {
        public Ray Ray;
        public Vector3 Throughput;
        public int PathLength;
        public float DVcm, DVc, DVm;
        public float Distance;         // from the previous vertex, across null interfaces
        public IorStack Iors;
        public MediumStack Mediums;
        public Vector3 SigmaA;         // absorption of the dielectric interior the ray is in
        public Vector3 StartThroughput; // light paths: the roulette is relative to it
    }

    // ── Per-render state ────────────────────────────────────────────────────

    private ILight[] _emitters = Array.Empty<ILight>();
    private float[] _emitterCdf = Array.Empty<float>();
    private Dictionary<ILight, float> _emitterPick = new(ReferenceEqualityComparer.Instance);
    private EmissionContext _emissionCtx;
    private float _lightTracePaths;          // light subpaths per iteration (light tracing)
    private float _vmNormalization, _misVmWeight, _misVcWeight, _mergeRadius2;
    private bool _useVm, _useVc;
    private int _maxPathLength;
    private float[] _splatA = Array.Empty<float>(), _splatB = Array.Empty<float>();
    private int _imageWidth, _imageHeight;

    private void PrepareBidirectional(int width, int height)
    {
        // Emitters: every light but the portals (a sampling aid of the path
        // tracer over the same sky), picked by power.
        _emitters = _lights.Where(l => l is not PortalLight).ToArray();
        var power = _emitters.Select(l => Math.Max(0.0, l.ApproximatePower(_finiteSceneBounds))).ToArray();
        double total = power.Sum();
        _emitterCdf = new float[_emitters.Length + 1];
        _emitterPick = new Dictionary<ILight, float>(ReferenceEqualityComparer.Instance);
        double acc = 0;
        for (int i = 0; i < _emitters.Length; i++)
        {
            double p = total > 0 ? power[i] / total : 1.0 / _emitters.Length;
            _emitterPick[_emitters[i]] = (float)p;
            acc += p;
            _emitterCdf[i + 1] = (float)acc;
        }
        Vector3 center = 0.5f * (_finiteSceneBounds.Min + _finiteSceneBounds.Max);
        float radius = MathF.Max(0.5f * (_finiteSceneBounds.Max - _finiteSceneBounds.Min).Length(), 1e-3f) * 1.01f;
        _emissionCtx = new EmissionContext(center, radius);
        _imageWidth = width;
        _imageHeight = height;
        _lightTracePaths = (float)width * height;
        _useVc = true;
        _useVm = Integrator == IntegratorKind.Vcm;
        // -d D lights paths up to D + 1 segments, as the path tracer.
        _maxPathLength = _maxDepth + 1;
        _splatA = new float[width * height * 3];
        _splatB = new float[width * height * 3];
    }

    private float EmitterPick(ILight light) => _emitterPick.TryGetValue(light, out float p) ? p : 0f;

    // ── Render ──────────────────────────────────────────────────────────────

    private RenderResult RenderBidirectional(int width, int height, RenderCaptureOptions options)
    {
        PrepareBidirectional(width, height);
        bool useSobol = Sampler.Kind == SamplerKind.Sobol;
        int spp = _samplesPerPixel;
        int pixels = width * height;
        var sumA = new Vector3[pixels];
        var sumB = new Vector3[pixels];
        _captureAovs = options.CaptureAovs;
        bool captureAovs = _captureAovs;
        CaptureAccumulator[]? capture = options.Any ? new CaptureAccumulator[pixels] : null;
        float sceneRadius = _emissionCtx.SceneRadius;
        var grid = new VertexGrid();
        var ranges = new (int Start, int Count)[Math.Min(MaxLightPathsPerChunk, pixels)];
        Vector3[] positions = Array.Empty<Vector3>();
        bool[] include = Array.Empty<bool>();

        Console.Write("\r  Rendering: 0.0%   ");
        for (int it = 0; it < spp; it++)
        {
            // Radius schedule (Knaus and Zwicker 2011, as the paper).
            float radius = MathF.Max(VcmRadiusFactor * sceneRadius * MathF.Pow(it + 1, 0.5f * (VcmRadiusAlpha - 1f)), 1e-7f);
            _mergeRadius2 = radius * radius;
            // Dual-buffer halves: the first ⌈spp/2⌉ iterations against the
            // rest (RenderBuffers).
            bool inA = it < (spp + 1) / 2;
            float[] splat = inA ? _splatA : _splatB;
            Vector3[] sum = inA ? sumA : sumB;

            for (int chunkStart = 0; chunkStart < pixels; chunkStart += MaxLightPathsPerChunk)
            {
                int count = Math.Min(MaxLightPathsPerChunk, pixels - chunkStart);
                float etaVcm = MathF.PI * _mergeRadius2 * count;
                _misVmWeight = _useVm ? etaVcm : 0f;
                _misVcWeight = _useVc ? 1f / etaVcm : 0f;
                _vmNormalization = 1f / etaVcm;

                // ── Light subpaths ─────────────────────────────────────────
                int stored = TraceLightPaths(chunkStart, count, it, useSobol, splat, ranges);
                BidiVertex[] vertices = _lightVertices;

                if (_useVm)
                {
                    if (positions.Length < stored)
                    {
                        positions = new Vector3[stored + stored / 4];
                        include = new bool[positions.Length];
                    }
                    for (int v = 0; v < stored; v++)
                    {
                        positions[v] = vertices[v].Rec.Point;
                        include[v] = vertices[v].Phase == null;   // merged at surfaces only
                    }
                    grid.Build(positions.AsSpan(0, stored), include.AsSpan(0, stored), radius);
                }

                // ── Camera subpaths ────────────────────────────────────────
                Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                    () => { PrepareBidirectionalWorker(useSobol); return 0; },
                    (k, _, local) =>
                    {
                        int pixel = chunkStart + k;
                        int i = pixel % width, j = pixel / width;
                        uint pixelSeed = (uint)(i * 73856093) ^ (uint)(j * 19349663) ^ _seedMix;
                        if (useSobol) Sampler.BeginPixelSample(pixelSeed, (uint)it);
                        if (captureAovs) (t_aovProbe ??= new AovProbe()).Reset();
                        Vector3 c = SanitizeRadiance(TraceCameraPath(i, j, width, height, it, vertices, ranges[k], grid));
                        if (useSobol) Sampler.EndPixelSample();
                        sum[pixel] += c;
                        if (capture != null) capture[pixel].Add(c, inA, captureAovs);
                        return local;
                    },
                    _ => ReleaseWorker());
            }
            // Every 2%: on redirected output each write is a line.
            if ((it + 1) * 50 / spp != it * 50 / spp || it + 1 == spp)
                Console.Write($"\r  Rendering: {100f * (it + 1) / spp:F1}% ({it + 1}/{spp} iterations)   ");
        }
        Console.WriteLine();

        // ── Film ────────────────────────────────────────────────────────────
        int nA = (spp + 1) / 2, nB = spp / 2;
        var result = new Vector3[height, width];
        RenderBuffers? buffers = options.Any ? new RenderBuffers(width, height, spp, captureAovs) : null;
        for (int j = 0; j < height; j++)
            for (int i = 0; i < width; i++)
            {
                int p = j * width + i;
                Vector3 a = sumA[p] + new Vector3(_splatA[3 * p], _splatA[3 * p + 1], _splatA[3 * p + 2]);
                Vector3 b = sumB[p] + new Vector3(_splatB[3 * p], _splatB[3 * p + 1], _splatB[3 * p + 2]);
                Vector3 linear = (a + b) / spp;
                result[j, i] = _display.Apply(linear * _exposureScale);
                if (buffers != null)
                {
                    // The guides from the camera subpaths; the beauty with the splats.
                    capture![p].Store(buffers, i, j, linear, spp);
                    buffers.BeautyA.SetRgb(i, j, a / nA);
                    buffers.BeautyB.SetRgb(i, j, nB > 0 ? b / nB : a / nA);
                }
            }
        _captureAovs = false;
        LastRenderNonFiniteCount = Interlocked.Read(ref s_nonFiniteCount);
        return new RenderResult { Pixels = result, Buffers = buffers };
    }

    private void PrepareBidirectionalWorker(bool useSobol)
    {
        MathUtils.SceneScale = _sceneScale;
        // Glass is opaque to connections: light through it travels only along
        // sampled subpaths (straight transmission excepted, exactly).
        ShadowRay.BlockAllTransmission = true;
        if (_seedMix != 0 && !useSobol)
            MathUtils.ReseedThread(unchecked((int)(_seedMix ^ (uint)Environment.CurrentManagedThreadId * 2654435761u)));
    }

    // The light vertices of the current chunk (reused across iterations).
    private List<BidiVertex>[] _lightLists = Array.Empty<List<BidiVertex>>();
    private BidiVertex[] _lightVertices = Array.Empty<BidiVertex>();
    private int[] _lightOwner = Array.Empty<int>();

    /// <summary>
    /// The light subpaths of one chunk, traced in parallel into
    /// <see cref="_lightVertices"/> (the count is returned), with per path
    /// the range it occupies. Each vertex is also connected to the camera
    /// (light tracing) into <paramref name="splat"/>.
    /// </summary>
    private int TraceLightPaths(int chunkStart, int count, int iteration, bool useSobol, float[] splat,
                                (int Start, int Count)[] ranges)
    {
        int workers = Environment.ProcessorCount;
        if (_lightLists.Length != workers)
        {
            _lightLists = new List<BidiVertex>[workers];
            for (int w = 0; w < workers; w++) _lightLists[w] = new List<BidiVertex>();
        }
        if (_lightOwner.Length < count) _lightOwner = new int[count];
        var lists = _lightLists;
        var owner = _lightOwner;
        int nextWorker = -1;
        Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, _ =>
        {
            int w = Interlocked.Increment(ref nextWorker);
            var list = lists[w];
            list.Clear();
            PrepareBidirectionalWorker(useSobol);
            for (int k = w; k < count; k += workers)
            {
                int pixel = chunkStart + k;
                int i = pixel % _imageWidth, j = pixel / _imageWidth;
                uint seed = ((uint)(i * 73856093) ^ (uint)(j * 19349663) ^ _seedMix) * 0x9E3779B9u + 0x7F4A7C15u;
                if (useSobol) Sampler.BeginPixelSample(seed, (uint)iteration);
                int start = list.Count;
                TraceLightPath(PathTime(pixel, iteration), list, splat);
                if (useSobol) Sampler.EndPixelSample();
                ranges[k] = (start, list.Count - start);
                owner[k] = w;
            }
            ReleaseWorker();
        });
        // One array: the ranges are offset by the lists before.
        Span<int> offsets = stackalloc int[workers + 1];
        for (int w = 0; w < workers; w++) offsets[w + 1] = offsets[w] + lists[w].Count;
        int total = offsets[workers];
        if (_lightVertices.Length < total) _lightVertices = new BidiVertex[total + total / 4];
        for (int w = 0; w < workers; w++) lists[w].CopyTo(_lightVertices, offsets[w]);
        for (int k = 0; k < count; k++) ranges[k] = (ranges[k].Start + offsets[owner[k]], ranges[k].Count);
        return total;
    }

    /// <summary>The shutter time shared by a pixel's camera and light subpaths in an iteration.</summary>
    private float PathTime(int pixel, int iteration)
    {
        if (!_motionActive) return 0f;
        uint h = MixSeed((uint)pixel * 0x9E3779B9u ^ (uint)iteration * 0x85EBCA6Bu ^ _seedMix);
        return _shutterOpen + (_shutterClose - _shutterOpen) * ((h >> 8) * (1f / 16777216f));
    }

    // ── BSDF at a vertex ────────────────────────────────────────────────────

    /// <summary>The record seen from the side <paramref name="d"/> points to.</summary>
    private static HitRecord Oriented(in HitRecord rec, Vector3 d)
    {
        if (Vector3.Dot(rec.Normal, d) >= 0f) return rec;
        HitRecord r = rec;
        r.Normal = -r.Normal;
        r.ShadingNg = -r.ShadingNg;
        r.FrontFace = !r.FrontFace;
        if (r.RelativeEta > 0f) r.RelativeEta = 1f / r.RelativeEta;
        return r;
    }

    /// <summary>
    /// The radiance BSDF f(V, L) times the shadow-terminator factor for the
    /// light side, and the densities of sampling L from V and V from L.
    /// </summary>
    private static Vector3 BidiBsdf(in HitRecord rec, Vector3 V, Vector3 L, out float pdfVtoL, out float pdfLtoV)
    {
        IMaterial m = rec.Material!;
        HitRecord rv = Oriented(rec, V);
        Vector3 f = m.Evaluate(V, L, in rv) * LightSideTerminator(in rv, L);
        pdfVtoL = m.Pdf(V, L, in rv);
        HitRecord rl = Oriented(rec, L);
        pdfLtoV = m.Pdf(L, V, in rl);
        return f;
    }

    /// <summary>Area density from a solid-angle one: p_ω |cos| / d².</summary>
    private static float PdfWtoA(float pdfW, float dist, float cosThere) => pdfW * MathF.Abs(cosThere) / (dist * dist);

    // ── Vertices of either kind ─────────────────────────────────────────────

    /// <summary>What a subpath met next.</summary>
    private enum VertexKind : byte { None, Surface, Medium }

    /// <summary>The cosine at a vertex toward <paramref name="d"/>: 1 in a medium.</summary>
    private static float CosAt(in HitRecord rec, IPhaseFunction? phase, Vector3 d)
        => phase != null ? 1f : MathF.Abs(Vector3.Dot(rec.Normal, d));

    /// <summary>The side a ray toward <paramref name="d"/> leaves from (none in a medium).</summary>
    private static Vector3 SideToward(in HitRecord rec, IPhaseFunction? phase, Vector3 d)
        => phase != null ? Vector3.Zero : Vector3.Dot(rec.Normal, d) >= 0f ? rec.Normal : -rec.Normal;

    /// <summary>
    /// f(V, L) at a surface (<see cref="BidiBsdf"/>) or a medium vertex (the
    /// phase function, whose σ_s the free flight already weighed), with the
    /// densities of sampling L from V and V from L.
    /// </summary>
    private static Vector3 VertexF(in HitRecord rec, IPhaseFunction? phase, Vector3 V, Vector3 L,
                                   out float pdfVtoL, out float pdfLtoV)
    {
        if (phase == null) return BidiBsdf(in rec, V, L, out pdfVtoL, out pdfLtoV);
        // Phase functions take the propagation direction: light runs from L to V.
        pdfVtoL = phase.Pdf(-V, L);
        pdfLtoV = phase.Pdf(-L, V);
        return new Vector3(phase.Evaluate(-V, L));
    }

    /// <summary>Merging happens only at surfaces (no photon beams).</summary>
    private float VmWeightAt(IPhaseFunction? phase) => phase == null ? _misVmWeight : 0f;

    /// <summary>
    /// The shadow-ray context of a vertex: the media its subpath is in and,
    /// at a surface that can be crossed, its own boundary, as next-event
    /// estimation sets it in the path tracer.
    /// </summary>
    private void BeginVertexShadows(in SubPath sp, in HitRecord rec, IPhaseFunction? phase, Vector3 fix)
    {
        ShadowRay.BeginMedia(in sp.Mediums, _globalMedium);
        if (phase != null) return;
        IMaterial m = rec.Material!;
        bool transmits = m.HasTransmission(in rec);
        ShadowRay.ReceiverTransmits = transmits;
        if (transmits && m.TryGetDielectricIor(in rec, out _) || rec.MediumIface.Interior != null)
            ShadowRay.SetReceiver(SideToward(in rec, null, fix), rec.FrontFace, rec.MediumIface.Interior, rec.EntityRoot);
    }

    private static void EndVertexShadows()
    {
        ShadowRay.ReceiverTransmits = false;
        ShadowRay.EndMedia();
    }

    /// <summary>
    /// A refraction into an entity the path tracer walks as subsurface
    /// scattering (<see cref="RandomWalkSubsurface"/>): bidirectional subpaths
    /// hand it the same walk, so both integrators render the same material.
    /// </summary>
    private bool EntersSubsurface(in SubPath sp, in HitRecord rec)
        => _sssMode == SssMode.Auto && sp.Mediums.Top is HomogeneousMedium interior
           && IsScatteringMedium(interior) && rec.EntityRoot != null;

    private void Enter(ref SubPath sp, in HitRecord rec, IMaterial material)
    {
        sp.Mediums.Push(ResolvePushedMedium(rec.MediumIface.Interior), rec.EntityRoot);
        sp.Iors.Push(material.TryGetDielectricIor(in rec, out float pushIor) ? pushIor : sp.Iors.Top);
    }

    // ── Hits ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The next vertex along the subpath's ray: a scattering event in the
    /// medium the ray is in, or a surface (maps applied, relative index set).
    /// Null interfaces are crossed on the way (their transmission folded into
    /// the throughput, their media and indices switched). None when the ray
    /// leaves the scene, the throughput then holding what reaches the sky, or
    /// when nothing gets through.
    /// </summary>
    private VertexKind NextVertex(ref SubPath sp, out HitRecord rec, out IPhaseFunction? phase)
    {
        rec = new HitRecord();
        phase = null;
        for (int crossings = 0; crossings < 64; crossings++)
        {
            rec = new HitRecord();
            bool hit = _world.Hit(sp.Ray, MathUtils.RayEpsilon, MathUtils.Infinity, ref rec);
            IMedium? medium = sp.Mediums.Top ?? _globalMedium;
            if (medium != null)
            {
                MediumEvent ev = medium.Sample(sp.Ray, hit ? rec.T : 1e30f, sp.Throughput);
                sp.Throughput *= ev.Weight;
                if (ev.Scattered)
                {
                    sp.Throughput *= BeerLambert(sp.SigmaA, ev.T);
                    sp.Distance += ev.T;
                    rec = new HitRecord { Point = sp.Ray.At(ev.T), T = ev.T, Time = sp.Ray.Time };
                    phase = ev.Phase;
                    return MathUtils.Luminance(sp.Throughput) > 0f ? VertexKind.Medium : VertexKind.None;
                }
                if (!(MathUtils.Luminance(sp.Throughput) > 0f)) return VertexKind.None;
            }
            if (!hit)
            {
                // Out of an absorbing interior nothing reaches the sky.
                if (HasAbsorption(sp.SigmaA)) sp.Throughput = Vector3.Zero;
                return VertexKind.None;
            }
            rec.Time = sp.Ray.Time;
            sp.Distance += rec.T;
            sp.Throughput *= BeerLambert(sp.SigmaA, rec.T);
            IMaterial? material = rec.Material;
            if (material == null) break;
            if (material.TryGetDielectricIor(in rec, out float ior) && ior > 0f)
                rec.RelativeEta = rec.FrontFace ? sp.Iors.Top / ior : ior / sp.Iors.Enclosing;
            if (!material.TransmitsStraight(in rec)) break;

            // A surface that may pass light straight on: sample it; a straight
            // pass is a null crossing, anything else is a vertex sampled anew.
            // (Behind an index-matched boundary a subsurface medium is an
            // ordinary medium: the walk would be the same estimator.)
            Vector3 V = -sp.Ray.Direction;
            BsdfSample? s = material.Sample(V, in rec);
            if (s is not { } bs || !bs.IsDelta || Vector3.Dot(bs.Wo, sp.Ray.Direction) < 0.99999f) break;
            sp.Throughput *= bs.F;      // a delta lobe's F is its whole weight
            sp.SigmaA = bs.NextSegmentAbsorption ?? sp.SigmaA;
            if (bs.Transition == MediumTransition.Enter)
                Enter(ref sp, in rec, material);
            else if (bs.Transition == MediumTransition.Exit)
            {
                sp.Mediums.Pop();
                sp.Iors.Pop();
            }
            sp.Ray = new Ray(MathUtils.OffsetOrigin(rec.Point, -rec.Normal), sp.Ray.Direction, sp.Ray.Time);
            if (!(MathUtils.Luminance(sp.Throughput) > 0f)) return VertexKind.None;
        }
        rec = PrepareVertexRecord(rec);
        return VertexKind.Surface;
    }

    private static HitRecord PrepareVertexRecord(HitRecord rec)
    {
        IMaterial? material = rec.Material;
        rec.ShadingNg = rec.Normal;
        if (material?.NormalMap != null && rec.Tangent.LengthSquared() > 0.5f) ApplyNormalMap(ref rec, material.NormalMap);
        if (material?.BumpMap != null && rec.Tangent.LengthSquared() > 0.5f) ApplyBumpMap(ref rec, material.BumpMap);
        if (rec.AutoBump != null && rec.Tangent.LengthSquared() > 0.5f) ApplyBumpMap(ref rec, rec.AutoBump);
        return rec;
    }

    /// <summary>
    /// Samples the scattering at a vertex and advances the subpath, updating
    /// the MIS quantities (the paper's SampleScattering). Light subpaths
    /// carry the adjoint: f(out, in) with the incoming direction as the light.
    /// <paramref name="transition"/> is the medium change of a refraction.
    /// </summary>
    private bool ScatterBidi(ref SubPath sp, in HitRecord rec, IPhaseFunction? phase, Vector3 fix, bool lightPath,
                             out MediumTransition transition)
    {
        transition = MediumTransition.None;
        Vector3 gen, weight;
        float dirPdf, revPdf, cosOut;
        bool delta = false;
        if (phase != null)
        {
            (gen, dirPdf) = phase.Sample(-fix);
            if (!(dirPdf > 1e-20f)) return false;
            revPdf = phase.Pdf(-gen, fix);
            weight = new Vector3(phase.Evaluate(-fix, gen) / dirPdf);
            cosOut = 1f;
        }
        else
        {
            IMaterial material = rec.Material!;
            BsdfSample? sampled = material.Sample(fix, in rec);
            if (sampled is not { } s) return false;
            gen = s.Wo;
            if (!lightPath && _captureAovs && t_aovProbe is { Pending: true } aov)
            {
                if (s.IsDelta) aov.AlbedoWeight *= Clamp01(s.F);
                else aov.Commit();
            }
            cosOut = MathF.Abs(Vector3.Dot(rec.Normal, gen));
            if (!(cosOut > 0f)) return false;
            delta = s.IsDelta;
            dirPdf = s.Pdf;
            if (delta)
            {
                weight = s.F;
                if (lightPath && s.Lobe == BsdfLobe.Transmission) weight *= s.Eta * s.Eta;   // no (η_i/η_t)² for importance
                revPdf = dirPdf;
            }
            else
            {
                if (!(dirPdf > 0f)) return false;
                weight = lightPath
                    ? BidiBsdf(in rec, gen, fix, out _, out _) * (cosOut / dirPdf)   // f(V = gen, L = fix)
                    : s.F * (cosOut * LightSideTerminator(in rec, gen) / dirPdf);
                HitRecord rg = Oriented(rec, gen);
                revPdf = material.Pdf(gen, fix, in rg);
            }
            sp.SigmaA = s.NextSegmentAbsorption ?? sp.SigmaA;
            transition = s.Transition;
            if (transition == MediumTransition.Enter) Enter(ref sp, in rec, material);
            else if (transition == MediumTransition.Exit)
            {
                sp.Mediums.Pop();
                sp.Iors.Pop();
            }
        }
        if (!(MathUtils.Luminance(weight) > 0f)) return false;

        if (delta)
        {
            sp.DVcm = 0f;
            sp.DVc *= cosOut;
            sp.DVm *= cosOut;
        }
        else
        {
            // The merging terms count a merge at this vertex: none in a medium.
            float vmOne = phase == null ? 1f : 0f;
            sp.DVc = cosOut / dirPdf * (sp.DVc * revPdf + sp.DVcm + VmWeightAt(phase));
            sp.DVm = cosOut / dirPdf * (sp.DVm * revPdf + sp.DVcm * _misVcWeight + vmOne);
            sp.DVcm = 1f / dirPdf;
        }
        sp.Throughput *= weight;

        // Russian roulette, outside the MIS densities.
        if (sp.PathLength >= 5)
        {
            Vector3 rel = lightPath ? sp.Throughput / Vector3.Max(sp.StartThroughput, new Vector3(1e-30f)) : sp.Throughput;
            float q = MathF.Min(1f, MathF.Max(rel.X, MathF.Max(rel.Y, rel.Z)));
            if (q < 1f)
            {
                if (!(q > 0f) || MathUtils.RandomFloat() >= q) return false;
                sp.Throughput /= q;
            }
        }

        Vector3 origin = phase != null ? rec.Point
                       : MathUtils.OffsetOrigin(rec.Point, Vector3.Dot(gen, rec.Normal) >= 0f ? rec.Normal : -rec.Normal);
        sp.Ray = new Ray(origin, gen, sp.Ray.Time);
        sp.Distance = 0f;
        return true;
    }

    private static bool Connectible(in HitRecord rec, IPhaseFunction? phase)
        => phase != null || rec.Material is { } m && !m.IsDeltaScatter && m.NeedsDirectLighting;

    // ── Light subpaths ──────────────────────────────────────────────────────

    private void TraceLightPath(float time, List<BidiVertex> store, float[] splat)
    {
        if (_emitters.Length == 0) return;
        Sampler.BeginVertex();
        float u = MathUtils.RandomFloat();
        int li = Array.BinarySearch(_emitterCdf, u);
        li = li >= 0 ? Math.Min(li, _emitters.Length - 1) : Math.Clamp(~li - 1, 0, _emitters.Length - 1);
        ILight light = _emitters[li];
        float pick = _emitterCdf[li + 1] - _emitterCdf[li];
        if (!(pick > 0f)) return;
        var (p1, p2) = MathUtils.Random2D();
        var (d1, d2) = MathUtils.Random2D();
        if (!light.SampleEmission(_emissionCtx, new Vector2(p1, p2), new Vector2(d1, d2), out LightEmission e)) return;
        float emissionPdfW = pick * e.PdfPos * e.PdfDir;
        if (!(emissionPdfW > 0f) || !(e.Cos > 0f)) return;

        var sp = new SubPath
        {
            Ray = new Ray(e.Infinite ? e.Position : MathUtils.OffsetOrigin(e.Position, e.Normal), e.Direction, time),
            Throughput = e.Radiance * (e.Cos / emissionPdfW),
            PathLength = 1,
            Iors = new IorStack(),
            Mediums = new MediumStack(),
        };
        sp.StartThroughput = sp.Throughput;
        bool deltaLight = e.DeltaPosition || e.DeltaDirection;
        sp.DVc = deltaLight ? 0f : (e.Infinite ? 1f : e.Cos) / emissionPdfW;
        sp.DVm = sp.DVc * _misVcWeight;

        while (true)
        {
            Sampler.BeginVertex();
            VertexKind kind = NextVertex(ref sp, out HitRecord rec, out IPhaseFunction? phase);
            if (kind == VertexKind.None) break;
            if (phase == null && rec.Material == null) break;
            Vector3 fix = -sp.Ray.Direction;
            float cosFix = CosAt(in rec, phase, fix);
            if (!(cosFix > 0f)) break;

            if (sp.PathLength == 1)
            {
                // dVCM needs the density of a light sample of this origin
                // taken from here: known only now (see the remarks).
                float directPdfW = DirectPdfW(light, rec.Point, fix, sp.Distance, e);
                sp.DVcm = directPdfW * (e.Infinite ? 1f : e.Cos) / (emissionPdfW * cosFix);
            }
            else
            {
                sp.DVcm *= sp.Distance * sp.Distance / cosFix;
            }
            sp.DVc /= cosFix;
            sp.DVm /= cosFix;

            if (Connectible(in rec, phase))
            {
                store.Add(new BidiVertex
                {
                    Rec = rec, Phase = phase, Fix = fix, Throughput = sp.Throughput,
                    DVcm = sp.DVcm, DVc = sp.DVc, DVm = sp.DVm, PathLength = sp.PathLength,
                });
                if (sp.PathLength + 1 <= _maxPathLength) ConnectToCamera(in sp, in rec, phase, fix, splat);
            }

            if (sp.PathLength + 2 > _maxPathLength) break;
            if (!ScatterBidi(ref sp, in rec, phase, fix, lightPath: true, out var transition)) break;
            // Subsurface entities are walked by the camera subpaths only.
            if (transition == MediumTransition.Enter && EntersSubsurface(in sp, in rec)) break;
            sp.PathLength++;
        }
    }

    /// <summary>
    /// Solid-angle density with which next-event estimation at
    /// <paramref name="receiver"/> picks the emission origin of
    /// <paramref name="e"/> seen along <paramref name="dirToLight"/>: the pick
    /// probability times the light's own density (for a point source its
    /// area density 1 over 1 / d², for a single direction 1).
    /// </summary>
    private float DirectPdfW(ILight light, Vector3 receiver, Vector3 dirToLight, float dist, in LightEmission e)
    {
        float pick = _mis.Pick(light, receiver);
        if (e.DeltaPosition) return pick * dist * dist;
        if (e.DeltaDirection) return pick;
        return pick * light.PdfSolidAngle(receiver, dirToLight);
    }

    /// <summary>Light tracing: a light vertex seen by the camera, splatted through the pixel filter.</summary>
    private void ConnectToCamera(in SubPath sp, in HitRecord rec, IPhaseFunction? phase, Vector3 fix, float[] splat)
    {
        if (!_camera.ConnectToFilm(rec.Point, sp.Ray.Time, out var c)) return;
        float px = c.S * _imageWidth, py = (1f - c.T) * _imageHeight;
        float fr = _pixelFilter.Radius;
        if (px < -fr || py < -fr || px > _imageWidth + fr || py > _imageHeight + fr) return;
        Vector3 toCam = -c.Direction;
        // pdfRev: the density of the light-side reverse sample (fix from toCam).
        Vector3 f = VertexF(in rec, phase, toCam, fix, out float pdfRev, out _);
        if (!(MathUtils.Luminance(f) > 0f)) return;
        float cosToCam = CosAt(in rec, phase, toCam);
        float cameraPdfW = _camera.FilmDirectionPdf(c.CosAxis, sp.Ray.Time) * (_imageWidth * (float)_imageHeight);
        float cameraPdfA = cameraPdfW * cosToCam / (c.Distance * c.Distance);
        float wLight = cameraPdfA / _lightTracePaths * (VmWeightAt(phase) + sp.DVcm + sp.DVc * pdfRev);
        float weight = 1f / (wLight + 1f);
        Vector3 contrib = weight * sp.Throughput * f * (cameraPdfA / _lightTracePaths);
        if (!(MathUtils.Luminance(contrib) > 0f)) return;
        Vector3 tr;
        BeginVertexShadows(in sp, in rec, phase, fix);
        try
        {
            tr = ShadowRay.TransmittanceToPoint(_world, rec.Point, SideToward(in rec, phase, toCam),
                                               c.LensPoint, Vector3.Zero, sp.Ray.Time);
        }
        finally
        {
            EndVertexShadows();
        }
        if (MathUtils.NearZero(tr)) return;
        SplatFiltered(splat, px, py, SanitizeRadiance(contrib * tr));
    }

    /// <summary>Adds a contribution at film position (px, py) to the pixels the filter covers.</summary>
    private void SplatFiltered(float[] splat, float px, float py, Vector3 v)
    {
        if (_pixelFilter.Kind == PixelFilterKind.Box)
        {
            int i = (int)MathF.Floor(px), j = (int)MathF.Floor(py);
            if (i < 0 || j < 0 || i >= _imageWidth || j >= _imageHeight) return;
            AtomicAdd(splat, j * _imageWidth + i, v);
            return;
        }
        float r = _pixelFilter.Radius;
        int i0 = (int)MathF.Floor(px - r), i1 = (int)MathF.Floor(px + r);
        int j0 = (int)MathF.Floor(py - r), j1 = (int)MathF.Floor(py + r);
        double norm = FilterNorm;
        for (int j = Math.Max(j0, 0); j <= Math.Min(j1, _imageHeight - 1); j++)
        {
            double wy = _pixelFilter.Evaluate(j + 0.5 - py);
            if (wy <= 0) continue;
            for (int i = Math.Max(i0, 0); i <= Math.Min(i1, _imageWidth - 1); i++)
            {
                double wx = _pixelFilter.Evaluate(i + 0.5 - px);
                if (wx <= 0) continue;
                AtomicAdd(splat, j * _imageWidth + i, v * (float)(wx * wy / (norm * norm)));
            }
        }
    }

    private double? _filterNorm;
    /// <summary>∫ of the filter's 1D profile, so the splat weights integrate to 1.</summary>
    private double FilterNorm => _filterNorm ??= ComputeFilterNorm();

    private double ComputeFilterNorm()
    {
        const int n = 4096;
        double r = _pixelFilter.Radius, sum = 0;
        for (int k = 0; k < n; k++) sum += _pixelFilter.Evaluate(-r + (k + 0.5) * 2 * r / n);
        return sum * 2 * r / n;
    }

    private static void AtomicAdd(float[] a, int pixel, Vector3 v)
    {
        AtomicAdd(ref a[3 * pixel], v.X);
        AtomicAdd(ref a[3 * pixel + 1], v.Y);
        AtomicAdd(ref a[3 * pixel + 2], v.Z);
    }

    private static void AtomicAdd(ref float target, float value)
    {
        if (value == 0f) return;
        float initial, computed;
        do
        {
            initial = target;
            computed = initial + value;
        }
        while (Interlocked.CompareExchange(ref target, computed, initial) != initial);
    }

    // ── Camera subpaths ─────────────────────────────────────────────────────

    private Vector3 TraceCameraPath(int i, int j, int width, int height, int iteration, BidiVertex[] lightVertices,
                                    (int Start, int Count) lightPath, VertexGrid grid)
    {
        Sampler.BeginVertex();
        float time = PathTime(j * width + i, iteration);
        var (ju, jv) = MathUtils.Random2D();
        float u = (i + _pixelFilter.SamplePosition(ju)) / width;
        float v = (height - j - 1 + _pixelFilter.SamplePosition(jv)) / height;
        Ray primary = _camera.GetRay(u, v, time);
        Vector3 dir = Vector3.Normalize(primary.Direction);
        if (!_camera.ConnectToFilm(primary.Origin + dir, time, out var film)) return Vector3.Zero;
        float cameraPdfW = _camera.FilmDirectionPdf(film.CosAxis, time) * (width * (float)height);

        var sp = new SubPath
        {
            Ray = new Ray(primary.Origin, dir, time),
            Throughput = Vector3.One,
            PathLength = 1,
            DVcm = _lightTracePaths / cameraPdfW,
            Iors = new IorStack(),
            Mediums = new MediumStack(),
        };
        Vector3 color = Vector3.Zero;
        Vector3 prevPoint = primary.Origin;
        bool afterWalk = false;

        while (true)
        {
            Sampler.BeginVertex();
            VertexKind kind = NextVertex(ref sp, out HitRecord rec, out IPhaseFunction? phase);
            if (kind == VertexKind.None)
            {
                if (MathUtils.Luminance(sp.Throughput) > 0f)
                    color += sp.Throughput * EscapeRadiance(in sp, prevPoint);
                break;
            }
            if (_captureAovs && t_aovProbe is { Pending: true } probe)
            {
                if (!probe.HaveDepth)
                {
                    probe.Depth = sp.Distance;
                    probe.HaveDepth = true;
                }
                if (phase != null) probe.CommitAs(Vector3.One, Vector3.Zero);   // volumes: featureless guides
                else
                {
                    probe.TentNormal = rec.Normal;
                    probe.TentAlbedo = rec.Material != null ? Clamp01(rec.Material.AovAlbedo(in rec)) : Vector3.One;
                }
            }
            if (phase == null && rec.Material == null) break;
            Vector3 fix = -sp.Ray.Direction;
            float cosFix = CosAt(in rec, phase, fix);
            if (!(cosFix > 0f)) break;
            sp.DVcm *= sp.Distance * sp.Distance / cosFix;
            sp.DVc /= cosFix;
            sp.DVm /= cosFix;

            // ── Emission (s = 0) ──────────────────────────────────────────
            if (phase == null)
            {
                Vector3 le = rec.Material!.Emit(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed, rec.FrontFace);
                if (le != Vector3.Zero)
                    color += sp.Throughput * le * EmitterHitWeight(in sp, in rec, prevPoint, cosFix);
            }
            // Past a subsurface walk only its own light sample pairs with
            // the emission above: no other strategy crosses the entity.
            if (afterWalk)
            {
                sp.DVcm = 0f;
                afterWalk = false;
            }

            if (sp.PathLength >= _maxPathLength) break;
            if (Connectible(in rec, phase))
            {
                BeginVertexShadows(in sp, in rec, phase, fix);
                try
                {
                    // ── Light sample (s = 1) ──────────────────────────────
                    if (sp.PathLength + 1 <= _maxPathLength)
                        color += sp.Throughput * LightSample(in sp, in rec, phase, fix);

                    // ── Connections to the light subpath (s > 1) ──────────
                    for (int k = 0; k < lightPath.Count; k++)
                    {
                        ref BidiVertex lv = ref lightVertices[lightPath.Start + k];
                        if (lv.PathLength + 1 + sp.PathLength > _maxPathLength) break;
                        color += sp.Throughput * lv.Throughput * ConnectVertices(in lv, in sp, in rec, phase, fix);
                    }
                }
                finally
                {
                    EndVertexShadows();
                }

                // ── Merging ───────────────────────────────────────────────
                if (_useVm && phase == null)
                    color += sp.Throughput * MergeAt(in sp, in rec, fix, lightVertices, grid);
            }

            prevPoint = rec.Point;
            Vector3 beta = sp.Throughput;
            if (!ScatterBidi(ref sp, in rec, phase, fix, lightPath: false, out var transition)) break;
            if (transition == MediumTransition.Enter && EntersSubsurface(in sp, in rec)
                && sp.Mediums.Top is HomogeneousMedium interior)
            {
                // The path tracer's walk, its light samples included; what
                // leaves the entity goes on as a camera subpath whose only
                // MIS partner is the walk's last light sample.
                var walk = RandomWalkSubsurface(sp.Ray, interior, rec.EntityRoot!, ref sp.Mediums, ref sp.Iors, sp.Throughput);
                color += sp.Throughput * walk.Radiance;
                if (!walk.Escaped) break;
                sp.Throughput *= walk.Throughput;
                sp.Ray = walk.EscapeRay;
                sp.Distance = 0f;
                sp.DVcm = walk.Scattered && walk.LastPhasePdf > 0f ? 1f / walk.LastPhasePdf : 0f;
                sp.DVc = 0f;
                sp.DVm = 0f;
                prevPoint = walk.EscapeRay.Origin;
                afterWalk = true;
            }
            sp.PathLength++;
        }
        return color;
    }

    /// <summary>MIS weight of an emitter the camera subpath hit (s = 0).</summary>
    private float EmitterHitWeight(in SubPath sp, in HitRecord rec, Vector3 prevPoint, float cosAtLight)
    {
        if (sp.PathLength == 1) return 1f;
        ILight? light = _mis.EmitterOwner(rec.Material!, sp.Ray, rec.T);
        if (light == null) return 1f;
        Vector3 dirToLight = sp.Ray.Direction;
        float directPdfW = _mis.Pick(light, prevPoint) * light.PdfSolidAngle(prevPoint, dirToLight);
        float directPdfA = PdfWtoA(directPdfW, sp.Distance, cosAtLight);
        float emissionPdfW = EmitterPick(light) * light.EmissionPdfOverCos(_emissionCtx, rec.Point, -dirToLight) * cosAtLight;
        float wCamera = directPdfA * sp.DVcm + emissionPdfW * sp.DVc;
        return 1f / (1f + wCamera);
    }

    /// <summary>The sky a camera subpath escaped to, each emitter weighted.</summary>
    private Vector3 EscapeRadiance(in SubPath sp, Vector3 prevPoint)
    {
        Vector3 dir = sp.Ray.Direction;
        bool direct = sp.PathLength == 1;
        Vector3 result = Vector3.Zero;
        bool skyLit = _envLight != null && _envLight.Sky.CanSampleDirectly;
        Vector3 body = _sky.Sample(sp.Ray, direct ? RayCategory.Camera : RayCategory.Diffuse,
                                   includeAnalyticalSun: direct || _mis.Suns.Length == 0);
        if (direct || !skyLit)
            result += body;
        else
        {
            float directPdfW = _mis.Pick(_envLight!, prevPoint) * _envLight!.PdfSolidAngle(prevPoint, dir);
            float emissionPdfW = EmitterPick(_envLight) * _envLight.EmissionPdfOverCos(_emissionCtx, prevPoint, -dir);
            result += body / (1f + directPdfW * sp.DVcm + emissionPdfW * sp.DVc);
        }
        if (direct) return result;
        foreach (var sun in _mis.Suns)
        {
            Vector3 ls = sun.RadianceToward(dir);
            if (ls == Vector3.Zero) continue;
            float directPdfW = _mis.Pick(sun, prevPoint) * sun.PdfSolidAngle(prevPoint, dir);
            float emissionPdfW = EmitterPick(sun) * sun.EmissionPdfOverCos(_emissionCtx, prevPoint, -dir);
            result += ls / (1f + directPdfW * sp.DVcm + emissionPdfW * sp.DVc);
        }
        return result;
    }

    /// <summary>
    /// Next-event estimation at a camera vertex (s = 1), MIS-weighted against
    /// every other strategy. The caller has opened the vertex's shadow context.
    /// </summary>
    private Vector3 LightSample(in SubPath sp, in HitRecord rec, IPhaseFunction? phase, Vector3 fix)
    {
        Vector3 result = Vector3.Zero;
        if (_lights.Count == 0) return result;
        Vector3 side = SideToward(in rec, phase, fix);
        if (_mis.PicksOneLight)
        {
            var (idx, pick) = _mis.Choose(rec.Point, MathUtils.RandomFloat());
            if (idx >= 0 && pick > 0f) result += LightSampleOne(_lights[idx], pick, in sp, in rec, phase, fix, side);
        }
        else
        {
            foreach (ILight light in _lights)
                if (light is not PortalLight) result += LightSampleOne(light, 1f, in sp, in rec, phase, fix, side);
        }
        return result;
    }

    private Vector3 LightSampleOne(ILight light, float pick, in SubPath sp, in HitRecord rec, IPhaseFunction? phase,
                                   Vector3 fix, Vector3 side)
    {
        Vector3 result = Vector3.Zero;
        if (light is PortalLight) return result;
        float pickE = EmitterPick(light);
        for (int s = 0; s < light.ShadowSamples; s++)
        {
            var (inShadow, color, dirToLight, dist) = SampleLight(light, rec.Point, side, s, sp.Ray.Time);
            if (inShadow || color == Vector3.Zero) continue;
            Vector3 f = VertexF(in rec, phase, fix, dirToLight, out float bsdfDirPdfW, out float bsdfRevPdfW);
            if (!(MathUtils.Luminance(f) > 0f)) continue;
            float cosToLight = CosAt(in rec, phase, dirToLight);
            bool infinite = float.IsInfinity(dist) || dist >= MathUtils.Infinity * 0.5f;
            bool deltaPos = light is PointLight or SpotLight;
            bool deltaDir = light is DirectionalLight { IsDelta: true };
            float directPdfW = deltaPos ? pick * dist * dist
                             : deltaDir ? pick
                             : pick * light.PdfSolidAngle(rec.Point, dirToLight);
            if (!(directPdfW > 0f)) continue;
            Vector3 lightPoint = infinite ? rec.Point : rec.Point + dirToLight * dist;
            float emissionOverCos = pickE * light.EmissionPdfOverCos(_emissionCtx, lightPoint, -dirToLight);
            float wLight = _mis.PairsWithBsdf(light) ? bsdfDirPdfW / directPdfW : 0f;
            float wCamera = emissionOverCos * cosToLight / directPdfW
                          * (VmWeightAt(phase) + sp.DVcm + sp.DVc * bsdfRevPdfW);
            float weight = 1f / (wLight + 1f + wCamera);
            // The light's sample already holds L G / pdf (and 1 / samples).
            result += weight * color * f * (cosToLight / pick);
        }
        return result;
    }

    /// <summary>A camera vertex joined to a light-subpath vertex (inside the vertex's shadow context).</summary>
    private Vector3 ConnectVertices(in BidiVertex lv, in SubPath sp, in HitRecord rec, IPhaseFunction? phase, Vector3 fix)
    {
        Vector3 d = lv.Rec.Point - rec.Point;
        float dist2 = d.LengthSquared();
        if (!(dist2 > 0f)) return Vector3.Zero;
        float dist = MathF.Sqrt(dist2);
        Vector3 dir = d / dist;
        Vector3 fCam = VertexF(in rec, phase, fix, dir, out float camDirPdfW, out float camRevPdfW);
        if (!(MathUtils.Luminance(fCam) > 0f)) return Vector3.Zero;
        // At the light vertex the viewer is the camera side (V = −dir), the light its incoming direction.
        Vector3 fLight = VertexF(in lv.Rec, lv.Phase, -dir, lv.Fix, out float lightRevPdfW, out float lightDirPdfW);
        if (!(MathUtils.Luminance(fLight) > 0f)) return Vector3.Zero;
        float cosCam = CosAt(in rec, phase, dir);
        float cosLight = CosAt(in lv.Rec, lv.Phase, dir);
        float g = cosCam * cosLight / dist2;
        if (!(g > 0f)) return Vector3.Zero;
        float camDirPdfA = PdfWtoA(camDirPdfW, dist, cosLight);
        float lightDirPdfA = PdfWtoA(lightDirPdfW, dist, cosCam);
        float wLight = camDirPdfA * (VmWeightAt(lv.Phase) + lv.DVcm + lv.DVc * lightRevPdfW);
        float wCamera = lightDirPdfA * (VmWeightAt(phase) + sp.DVcm + sp.DVc * camRevPdfW);
        float weight = 1f / (wLight + 1f + wCamera);
        Vector3 contrib = weight * g * fCam * fLight;
        if (!(MathUtils.Luminance(contrib) > 0f)) return Vector3.Zero;
        Vector3 tr = ShadowRay.TransmittanceToPoint(_world, rec.Point, SideToward(in rec, phase, dir),
                                                   lv.Rec.Point, SideToward(in lv.Rec, lv.Phase, -dir), sp.Ray.Time);
        return MathUtils.NearZero(tr) ? Vector3.Zero : contrib * tr;
    }

    /// <summary>Vertex merging: the light vertices within the radius, estimated with this vertex's BSDF.</summary>
    private Vector3 MergeAt(in SubPath sp, in HitRecord rec, Vector3 fix, BidiVertex[] lightVertices, VertexGrid grid)
    {
        Span<(int Start, int End)> cells = stackalloc (int, int)[8];
        int n = grid.Candidates(rec.Point, cells);
        var indices = grid.Indices;
        Vector3 sum = Vector3.Zero;
        for (int c = 0; c < n; c++)
        {
            for (int k = cells[c].Start; k < cells[c].End; k++)
            {
                ref BidiVertex lv = ref lightVertices[indices[k]];
                if ((lv.Rec.Point - rec.Point).LengthSquared() > _mergeRadius2) continue;
                if (lv.PathLength + sp.PathLength > _maxPathLength) continue;
                Vector3 f = BidiBsdf(in rec, fix, lv.Fix, out float camDirPdfW, out float camRevPdfW);
                if (!(MathUtils.Luminance(f) > 0f)) continue;
                float wLight = lv.DVcm * _misVcWeight + lv.DVm * camDirPdfW;
                float wCamera = sp.DVcm * _misVcWeight + sp.DVm * camRevPdfW;
                float weight = 1f / (wLight + 1f + wCamera);
                sum += weight * f * lv.Throughput;
            }
        }
        return sum * _vmNormalization;
    }
}
