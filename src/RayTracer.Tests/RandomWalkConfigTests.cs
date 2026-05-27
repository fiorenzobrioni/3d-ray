using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Unit tests for the random-walk quality presets exposed by
/// <see cref="RandomWalkConfig"/>. The presets are the source of truth that
/// <c>--sss-quality</c> in <c>Program.cs</c> and the per-quality bindings in
/// <c>QualityPreset</c> resolve to, so locking their values down here protects
/// the CLI surface from accidental drift.
/// </summary>
public class RandomWalkConfigTests
{
    [Fact]
    public void Preview_HasCheapBudget_NeeCappedToEarlyBounces()
    {
        var cfg = RandomWalkConfig.Preview;
        Assert.Equal(16, cfg.MaxVolumeBounces);
        Assert.Equal(1,  cfg.RrStartBounce);
        Assert.True(cfg.NeeInsideWalk);
        // Small cap retains the visually dominant in-scattering NEE
        // contribution at minimal shadow-ray cost — without it dense SSS
        // media render near-black in draft.
        Assert.Equal(2, cfg.NeeMaxBounce);
    }

    [Fact]
    public void Normal_HasProductionBudget_NeeEnabled()
    {
        var cfg = RandomWalkConfig.Normal;
        Assert.Equal(64, cfg.MaxVolumeBounces);
        Assert.Equal(3,  cfg.RrStartBounce);
        Assert.True(cfg.NeeInsideWalk);
        Assert.Equal(int.MaxValue, cfg.NeeMaxBounce);
    }

    [Fact]
    public void High_HasGenerousBudget_NeeEnabled()
    {
        var cfg = RandomWalkConfig.High;
        Assert.Equal(256, cfg.MaxVolumeBounces);
        Assert.Equal(6,   cfg.RrStartBounce);
        Assert.True(cfg.NeeInsideWalk);
        Assert.Equal(int.MaxValue, cfg.NeeMaxBounce);
    }

    /// <summary>
    /// Each preset must be strictly more capable than the prior one along the
    /// MaxVolumeBounces axis — that is the dominant cost knob and the one the
    /// CLI <c>--max-volume-bounces</c> override interacts with. A regression
    /// that reorders or duplicates them would silently kill the tier hierarchy.
    /// </summary>
    [Fact]
    public void Presets_AreMonotonicInVolumeBounces()
    {
        Assert.True(RandomWalkConfig.Preview.MaxVolumeBounces
                  < RandomWalkConfig.Normal.MaxVolumeBounces);
        Assert.True(RandomWalkConfig.Normal.MaxVolumeBounces
                  < RandomWalkConfig.High.MaxVolumeBounces);
    }

    [Fact]
    public void Construct_FromExplicitValues_RoundTrips()
    {
        var cfg = new RandomWalkConfig(maxVolumeBounces: 42,
                                       rrStartBounce:    5,
                                       neeInsideWalk:    true,
                                       neeMaxBounce:     7);
        Assert.Equal(42, cfg.MaxVolumeBounces);
        Assert.Equal(5,  cfg.RrStartBounce);
        Assert.True(cfg.NeeInsideWalk);
        Assert.Equal(7,  cfg.NeeMaxBounce);
    }
}
