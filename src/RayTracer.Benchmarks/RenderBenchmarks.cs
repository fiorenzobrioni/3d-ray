using BenchmarkDotNet.Attributes;
using RayTracer.Core.Sampling;
using RayTracer.Rendering;
using RayTracer.Scene;

namespace RayTracer.Benchmarks;

/// <summary>
/// End-to-end render wall-time baseline. Loads a canonical YAML scene once
/// in <see cref="GlobalSetup"/> (so scene parsing and BVH build are NOT in
/// the measured window) and times <see cref="Renderer.Render"/> at modest
/// resolution / spp so a single iteration completes in a few seconds.
///
/// Console progress output from <c>Renderer</c> is silenced during the
/// measured call to keep BDN's variance low.
///
/// This benchmark exists so that BVH/parallelism/material optimisations
/// can be evaluated against a real wall-time number rather than a synthetic
/// micro-bench. Pair with <see cref="AabbBenchmarks"/> and
/// <see cref="BvhBenchmarks"/> for sub-component drill-downs.
/// </summary>
[MemoryDiagnoser]
public class RenderBenchmarks
{
    private SceneState _scene = null!;
    private TextWriter _originalOut = null!;

    /// <summary>Image width in pixels.</summary>
    [Params(160)]
    public int Width;

    /// <summary>Image height in pixels.</summary>
    [Params(107)]
    public int Height;

    /// <summary>Samples per pixel.</summary>
    [Params(4, 16)]
    public int Spp;

    /// <summary>Maximum bounce depth.</summary>
    [Params(6)]
    public int Depth;

    /// <summary>Scene YAML, resolved relative to repo root at setup time.
    /// The default <c>cornell-box.yaml</c> covers the baseline path (BVH, NEE,
    /// classic BSDFs). The <c>showcases/sss-randomwalk-01-marble.yaml</c>
    /// variant exercises the Random Walk SSS dispatch — the marble bust under
    /// area lighting — so the benchmark can quantify the SSS overhead relative
    /// to a comparable non-SSS scene. Acceptance target from the implementation
    /// plan: marble scene render time within 2.5× of a comparable opaque scene
    /// at the same resolution / spp / depth.</summary>
    [Params("cornell-box.yaml", "showcases/sss-randomwalk-01-marble.yaml")]
    public string Scene = "cornell-box.yaml";

    [GlobalSetup]
    public void Setup()
    {
        // Sobol matches the production default and stresses the sampler
        // hot path the way real renders do.
        Sampler.SetKind(SamplerKind.Sobol);

        string yamlPath = ResolveScenePath(Scene);

        var loaded = SceneLoader.Load(yamlPath, Width, Height);
        var renderer = new Renderer(
            world:           loaded.World,
            camera:          loaded.Camera,
            lights:          loaded.Lights,
            sky:             loaded.Sky,
            samplesPerPixel: Spp,
            maxDepth:        Depth,
            globalMedium:    loaded.GlobalMedium);

        _scene = new SceneState(renderer);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Suppress the per-scanline progress prints that Renderer.Render
        // emits — they would add I/O jitter to wall-time measurements.
        _originalOut = Console.Out;
        Console.SetOut(TextWriter.Null);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        Console.SetOut(_originalOut);
    }

    [Benchmark]
    public int Render()
    {
        var pixels = _scene.Renderer.Render(Width, Height);
        // Return a value that depends on the output so the JIT can't
        // dead-code-eliminate the render call.
        return pixels.Length;
    }

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> until it finds a
    /// directory that contains <c>scenes/</c>, then returns the absolute path
    /// to the requested scene file. Supports nested paths (e.g.
    /// <c>showcases/sss-randomwalk-01-marble.yaml</c>) so <see cref="Scene"/>
    /// can address files outside the top-level <c>scenes/</c> folder. Falls
    /// back to the bare filename so BDN surfaces a clear FileNotFoundException
    /// if the layout changed.
    /// </summary>
    private static string ResolveScenePath(string sceneFile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "scenes", sceneFile);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return sceneFile;
    }

    private sealed record SceneState(Renderer Renderer);
}
