using System.Collections.Generic;

namespace TerrainGen.Splatting;

/// <summary>
/// Builds the altitude/slope band table that the YAML emitter writes into the
/// heightfield's <c>strata:</c> list. The thresholds drive the engine's
/// runtime stratum selector (<c>HeightField.SelectMaterial</c>) — they are
/// what used to be the per-triangle classification logic of the OBJ pipeline,
/// projected back onto a continuous altitude axis so a single heightfield
/// primitive can shade itself with the right material at every hit point.
///
/// Bands overlap on purpose: the engine's selector picks the highest combined
/// weight, so a small overlap region effectively widens each band's
/// dominance halo. The <c>blend_width</c> values are emitted but currently
/// only affect the dominance margin — proper inter-band material lerp is a
/// post-v1 follow-up (see <c>HeightField.SelectMaterial</c>).
/// </summary>
public static class StratumClassifier
{
    public sealed record Band(
        Stratum Stratum,
        float MinAltitude, float MaxAltitude,
        float MinSlopeDeg, float MaxSlopeDeg,
        float BlendWidth);

    /// <summary>
    /// Returns the bands appropriate for this run. Snow line tracks the
    /// season; sand only appears when a sea/lake is in play (so the beach
    /// ring is visible against the water plane). Order is irrelevant —
    /// the engine resolves by weight, not by list position.
    /// </summary>
    public static List<Band> Build(GenerationConfig cfg)
    {
        // Snow line drops in winter and rises in summer — same calibration the
        // old triangle classifier used, just expressed as a min-altitude band.
        float snowMin = cfg.Season switch
        {
            Season.Inverno   => 0.55f,
            Season.Autunno   => 0.78f,
            Season.Primavera => 0.82f,
            _                => 0.85f,
        };

        bool waterPresent = cfg.SeaLevel >= 0f
            || cfg.HasFlag(WaterFeatures.Mare)
            || cfg.HasFlag(WaterFeatures.Laghi)
            || cfg.HasFlag(WaterFeatures.Fiumi);
        float beachMax = waterPresent ? Math0Clamp(cfg.SeaLevel) + 0.06f : -1f;

        var bands = new List<Band>();
        if (beachMax > 0f)
        {
            // Sand band hugs the waterline, fades out 4% above the beach top.
            bands.Add(new Band(
                Stratum.Sand,
                MinAltitude: 0f,
                MaxAltitude: beachMax,
                MinSlopeDeg: 0f, MaxSlopeDeg: 35f,
                BlendWidth: 0.04f));
        }

        // Ground (grass / forest floor / autumn leaf) covers everything that
        // isn't beach, cliff or snow. Extra reach into the rock band keeps
        // the transition visually soft.
        bands.Add(new Band(
            Stratum.Ground,
            MinAltitude: beachMax > 0f ? beachMax - 0.02f : 0f,
            MaxAltitude: 0.75f,
            MinSlopeDeg: 0f, MaxSlopeDeg: 45f,
            BlendWidth: 0.06f));

        // Rock takes over on cliffs at any altitude AND on the upper third
        // of the elevation range regardless of slope.
        bands.Add(new Band(
            Stratum.Rock,
            MinAltitude: 0.50f,
            MaxAltitude: 1.00f,
            MinSlopeDeg: 0f, MaxSlopeDeg: 90f,
            BlendWidth: 0.08f));

        // Snow caps the peaks; the dominance band overlaps with rock so the
        // transition isn't a hard line.
        bands.Add(new Band(
            Stratum.Snow,
            MinAltitude: snowMin,
            MaxAltitude: 1.00f,
            MinSlopeDeg: 0f, MaxSlopeDeg: 60f,
            BlendWidth: 0.05f));

        return bands;
    }

    private static float Math0Clamp(float v) => v < 0f ? 0f : v;
}
