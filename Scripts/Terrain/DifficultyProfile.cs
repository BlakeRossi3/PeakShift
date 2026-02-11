using Godot;

namespace PeakShift.Terrain;

/// <summary>
/// Controls how difficulty scales with distance. The generator queries this
/// to determine max allowed difficulty, terrain switch frequency, gap tolerances,
/// and slope steepness at any given distance into the run.
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
    public int MaxSameTerrainEarly { get; set; } = 3;

    /// <summary>Maximum same-type modules in a row at high difficulty.</summary>
    public int MaxSameTerrainLate { get; set; } = 1;

    /// <summary>Distance at which switching becomes most aggressive.</summary>
    public float MaxSwitchFrequencyDistance { get; set; } = 12000f;

    // ── Slope scaling ────────────────────────────────────────────

    /// <summary>Base drop multiplier at start of run.</summary>
    public float BaseDropMultiplier { get; set; } = 1.0f;

    /// <summary>Drop multiplier at maximum difficulty.</summary>
    public float MaxDropMultiplier { get; set; } = 1.6f;

    /// <summary>Distance at which drop multiplier is fully ramped.</summary>
    public float DropRampDistance { get; set; } = 20000f;

    // ── Gap scaling ──────────────────────────────────────────────

    /// <summary>Gap width multiplier at start.</summary>
    public float BaseGapMultiplier { get; set; } = 0.8f;

    /// <summary>Gap width multiplier at maximum difficulty.</summary>
    public float MaxGapMultiplier { get; set; } = 1.4f;

    /// <summary>Distance at which gap multiplier is fully ramped.</summary>
    public float GapRampDistance { get; set; } = 18000f;

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
}
