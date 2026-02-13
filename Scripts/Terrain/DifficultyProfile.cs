using Godot;

namespace PeakShift.Terrain;

/// <summary>
/// Controls how difficulty scales with distance. The generator queries this
/// to determine max allowed difficulty, terrain switch frequency, gap tolerances,
/// slope steepness, guidance slope angle, and procedural variety at any given
/// distance into the run.
/// </summary>
public class DifficultyProfile
{
    // ── Distance thresholds (px) ─────────────────────────────────

    /// <summary>Distance at which difficulty tier 2 unlocks.</summary>
    public float Tier2Distance { get; set; } = 3000f;

    /// <summary>Distance at which difficulty tier 3 unlocks.</summary>
    public float Tier3Distance { get; set; } = 6000f;

    /// <summary>Distance at which difficulty tier 4 unlocks.</summary>
    public float Tier4Distance { get; set; } = 10000f;

    /// <summary>Distance at which difficulty tier 5 unlocks.</summary>
    public float Tier5Distance { get; set; } = 15000f;

    // ── Terrain switching ────────────────────────────────────────

    /// <summary>Maximum same-type modules in a row at the start.</summary>
    public int MaxSameTerrainEarly { get; set; } = 8;

    /// <summary>Maximum same-type modules in a row at high difficulty.</summary>
    public int MaxSameTerrainLate { get; set; } = 4;

    /// <summary>Distance at which switching becomes most aggressive.</summary>
    public float MaxSwitchFrequencyDistance { get; set; } = 12000f;

    // ── Slope scaling ────────────────────────────────────────────

    /// <summary>Base drop multiplier at start of run.</summary>
    public float BaseDropMultiplier { get; set; } = 1.2f;

    /// <summary>Drop multiplier at maximum difficulty.</summary>
    public float MaxDropMultiplier { get; set; } = 2.0f;

    /// <summary>Distance at which drop multiplier is fully ramped.</summary>
    public float DropRampDistance { get; set; } = 20000f;

    // ── Gap scaling ──────────────────────────────────────────────

    /// <summary>Gap width multiplier at start.</summary>
    public float BaseGapMultiplier { get; set; } = 1.0f;

    /// <summary>Gap width multiplier at maximum difficulty.</summary>
    public float MaxGapMultiplier { get; set; } = 1.8f;

    /// <summary>Distance at which gap multiplier is fully ramped.</summary>
    public float GapRampDistance { get; set; } = 18000f;

    // ── Guidance Slope ───────────────────────────────────────────
    // Macro-level downhill angle the terrain follows.
    // Early run is gentle, late run is steep.

    /// <summary>Guidance slope angle at the start of the run (degrees).</summary>
    public float GuidanceSlopeAngleEarly { get; set; } = 18f;

    /// <summary>Guidance slope angle at maximum difficulty (degrees).</summary>
    public float GuidanceSlopeAngleLate { get; set; } = 35f;

    /// <summary>Distance at which guidance slope reaches maximum angle (px).</summary>
    public float GuidanceSlopeRampDistance { get; set; } = 25000f;

    // ── Procedural Variety Controls ──────────────────────────────

    /// <summary>Minimum variance around the guidance slope for descent drop.</summary>
    public float DescentDropVarianceMin { get; set; } = 0.7f;

    /// <summary>Maximum variance around the guidance slope for descent drop.</summary>
    public float DescentDropVarianceMax { get; set; } = 1.6f;

    /// <summary>Ramp rise as a fraction of the preceding descent's drop (early).</summary>
    public float RampRiseToDropRatio { get; set; } = 0.55f;

    /// <summary>Ramp rise as a fraction of the preceding descent's drop (late).</summary>
    public float RampRiseToDropRatioMax { get; set; } = 0.75f;

    /// <summary>Flat breather probability per generation cycle.</summary>
    public float FlatBreatherChance { get; set; } = 0.05f;

    /// <summary>Bump/roller probability per generation cycle.</summary>
    public float BumpRollerChance { get; set; } = 0.06f;

    /// <summary>Chance of skipping the ramp (descent flows into next descent).</summary>
    public float SkipRampChance { get; set; } = 0.08f;

    /// <summary>Chance of a double-descent (two descents back to back before a ramp).</summary>
    public float DoubleDescentChance { get; set; } = 0.12f;

    // ── Query methods ────────────────────────────────────────────

    /// <summary>Returns the maximum difficulty tier allowed at the given distance.</summary>
    public int GetMaxDifficulty(float distance)
    {
        if (distance >= Tier5Distance) return 5;
        if (distance >= Tier4Distance) return 4;
        if (distance >= Tier3Distance) return 3;
        if (distance >= Tier2Distance) return 2;
        return 1;
    }

    /// <summary>
    /// Returns the maximum number of same-terrain-type modules in a row,
    /// interpolated between early and late values based on distance.
    /// </summary>
    public int GetMaxSameTerrainRun(float distance)
    {
        float t = Mathf.Clamp(distance / MaxSwitchFrequencyDistance, 0f, 1f);
        return (int)Mathf.Lerp(MaxSameTerrainEarly, MaxSameTerrainLate, t);
    }

    /// <summary>Multiplier applied to module Drop values based on distance.</summary>
    public float GetDropMultiplier(float distance)
    {
        float t = Mathf.Clamp(distance / DropRampDistance, 0f, 1f);
        return Mathf.Lerp(BaseDropMultiplier, MaxDropMultiplier, t);
    }

    /// <summary>Multiplier applied to module GapWidth values based on distance.</summary>
    public float GetGapMultiplier(float distance)
    {
        float t = Mathf.Clamp(distance / GapRampDistance, 0f, 1f);
        return Mathf.Lerp(BaseGapMultiplier, MaxGapMultiplier, t);
    }

    /// <summary>
    /// Returns the guidance slope angle (degrees) at the given distance.
    /// Linearly interpolated between early and late values.
    /// </summary>
    public float GetGuidanceSlopeAngle(float distance)
    {
        float t = Mathf.Clamp(distance / GuidanceSlopeRampDistance, 0f, 1f);
        return Mathf.Lerp(GuidanceSlopeAngleEarly, GuidanceSlopeAngleLate, t);
    }

    /// <summary>
    /// Returns the tangent of the guidance slope at a given distance.
    /// Use: drop = length * GetGuidanceSlopeTan(distance)
    /// </summary>
    public float GetGuidanceSlopeTan(float distance)
    {
        float angleDeg = GetGuidanceSlopeAngle(distance);
        return Mathf.Tan(Mathf.DegToRad(angleDeg));
    }
}
