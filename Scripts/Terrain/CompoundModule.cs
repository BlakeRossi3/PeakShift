using System.Collections.Generic;
using Godot;

namespace PeakShift.Terrain;

/// <summary>
/// Sub-section types available inside a compound module.
/// </summary>
public enum SubSectionType
{
    Landing,           // Always first. Matches airborne trajectory, gradually flattens.
    RollingHills,      // Multiple sine-wave hills
    RockGarden,        // Short steep bumps (high-frequency sine)
    LongCruise,        // Extended gentle descent (S-curve)
    MogulField,        // Dense rhythmic bumps
    TerrainTransition, // Smooth blend between terrain types
    SteepChute,        // Short intense drop (S-curve, high slope)
    PowderField,       // Gentle undulating terrain
    ExitRamp           // Always last. Upward ramp terminating at gap lip.
}

/// <summary>
/// A single sub-section within a compound module.
/// All X positions are LOCAL offsets from the compound module start.
/// </summary>
public struct SubSection
{
    public SubSectionType Type;
    public float LocalStartX;
    public float LocalEndX;
    public float Length;
    public float EntryY;
    public float ExitY;
    public float EntrySlope;   // dy/dx at entry (positive = downhill in Y-down)
    public float ExitSlope;    // dy/dx at exit
    public TerrainType Terrain;

    // Shape-specific parameters
    public float Drop;         // Net vertical change (positive = downhill)
    public float Rise;         // Amplitude for sine-based shapes / ramp rise
    public int Periods;        // Number of sine periods (RollingHills, MogulField, RockGarden)
    public int Difficulty;
}

/// <summary>
/// A compound module containing multiple sub-sections stitched together with
/// C1 continuity. Each compound module spans 8,000-20,000px and follows the
/// structure: Landing → [Interior Sub-Sections] → ExitRamp + Gap.
///
/// Height sampling uses existing shape formulas in the sub-section interior
/// with cubic Hermite blending zones at boundaries for C1 continuity.
/// </summary>
public class CompoundModule
{
    private const float MaxBlendFraction = 0.20f;
    private const float MaxBlendPx = 800f;

    public List<SubSection> Sections { get; } = new();

    /// <summary>Total horizontal length of all sub-sections (excludes gap).</summary>
    public float TotalLength { get; set; }

    /// <summary>Gap width after the exit ramp.</summary>
    public float GapWidth { get; set; }

    /// <summary>Overall difficulty rating.</summary>
    public int Difficulty { get; set; }

    /// <summary>Whether this compound module contains a terrain transition.</summary>
    public bool HasTerrainTransition { get; set; }

    /// <summary>Entry terrain (from Landing sub-section).</summary>
    public TerrainType EntryTerrain => Sections.Count > 0 ? Sections[0].Terrain : TerrainType.Snow;

    /// <summary>Exit terrain (from ExitRamp sub-section).</summary>
    public TerrainType ExitTerrain => Sections.Count > 0 ? Sections[^1].Terrain : TerrainType.Snow;

    /// <summary>Entry Y of the first sub-section.</summary>
    public float EntryY => Sections.Count > 0 ? Sections[0].EntryY : 0f;

    /// <summary>Exit Y of the last sub-section (ramp lip).</summary>
    public float ExitY => Sections.Count > 0 ? Sections[^1].ExitY : 0f;

    // ── Height Sampling ─────────────────────────────────────────

    /// <summary>
    /// Sample the terrain height at a local X offset within this compound module.
    /// Uses shape formulas in the interior with Hermite blending at boundaries.
    /// </summary>
    public float SampleHeight(float localX)
    {
        int idx = FindSubSectionIndex(localX);
        if (idx < 0) return Sections[0].EntryY;
        if (idx >= Sections.Count) return Sections[^1].ExitY;

        var sec = Sections[idx];
        float t = Mathf.Clamp((localX - sec.LocalStartX) / sec.Length, 0f, 1f);

        // Landing and ExitRamp use pure Hermite for exact slope matching at boundaries
        if (sec.Type == SubSectionType.Landing || sec.Type == SubSectionType.ExitRamp)
            return HermiteSample(sec, t);

        // Get the raw shape height and Hermite height
        float rawY = SampleSubSectionShape(sec, t);
        float hermiteY = HermiteSample(sec, t);

        // Blend: Hermite near boundaries, raw shape in the middle
        float blendWidth = ComputeBlendWidth(sec.Length);
        float distFromStart = localX - sec.LocalStartX;
        float distFromEnd = sec.LocalEndX - localX;

        if (distFromStart < blendWidth)
        {
            float blend = SmoothStep(distFromStart / blendWidth);
            return Mathf.Lerp(hermiteY, rawY, blend);
        }

        if (distFromEnd < blendWidth)
        {
            float blend = SmoothStep(distFromEnd / blendWidth);
            return Mathf.Lerp(hermiteY, rawY, blend);
        }

        return rawY;
    }

    /// <summary>
    /// Returns the terrain type at a local X position within the compound module.
    /// </summary>
    public TerrainType GetTerrainTypeAt(float localX)
    {
        int idx = FindSubSectionIndex(localX);
        if (idx < 0) return Sections[0].Terrain;
        if (idx >= Sections.Count) return Sections[^1].Terrain;
        return Sections[idx].Terrain;
    }

    /// <summary>
    /// Binary search for the sub-section containing the given local X.
    /// </summary>
    public int FindSubSectionIndex(float localX)
    {
        int lo = 0, hi = Sections.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (localX < Sections[mid].LocalStartX)
                hi = mid - 1;
            else if (localX >= Sections[mid].LocalEndX)
                lo = mid + 1;
            else
                return mid;
        }
        return localX < 0 ? -1 : Sections.Count;
    }

    // ── Hermite Interpolation ───────────────────────────────────

    /// <summary>
    /// Cubic Hermite interpolation across a sub-section.
    /// Guarantees C1 continuity by matching position and slope at both endpoints.
    /// </summary>
    private static float HermiteSample(SubSection sec, float t)
    {
        float p0 = sec.EntryY;
        float p1 = sec.ExitY;
        float m0 = sec.EntrySlope * sec.Length;  // tangent scaled by interval
        float m1 = sec.ExitSlope * sec.Length;

        float t2 = t * t;
        float t3 = t2 * t;

        float h00 = 2f * t3 - 3f * t2 + 1f;
        float h10 = t3 - 2f * t2 + t;
        float h01 = -2f * t3 + 3f * t2;
        float h11 = t3 - t2;

        return h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1;
    }

    // ── Shape Sampling ──────────────────────────────────────────

    /// <summary>
    /// Raw shape formula for each sub-section type, matching TrackModule conventions.
    /// </summary>
    private static float SampleSubSectionShape(SubSection sec, float t)
    {
        return sec.Type switch
        {
            SubSectionType.Landing =>
                // Pure Hermite (handled in SampleHeight, but fallback here)
                sec.EntryY + sec.Drop * t,

            SubSectionType.RollingHills =>
                // Multiple sine periods with net downhill
                sec.EntryY + sec.Drop * t - sec.Rise * Mathf.Sin(t * Mathf.Pi * 2f * sec.Periods),

            SubSectionType.RockGarden =>
                // High-frequency bumps with net downhill
                sec.EntryY + sec.Drop * t - sec.Rise * Mathf.Sin(t * Mathf.Pi * 2f * sec.Periods),

            SubSectionType.LongCruise =>
                // S-curve descent (same as TrackModule.Descent)
                sec.EntryY + sec.Drop * (1f - Mathf.Cos(t * Mathf.Pi)) / 2f,

            SubSectionType.MogulField =>
                // Dense rhythmic bumps with net downhill
                sec.EntryY + sec.Drop * t - sec.Rise * Mathf.Sin(t * Mathf.Pi * 2f * sec.Periods),

            SubSectionType.TerrainTransition =>
                // S-curve descent (smooth blending zone)
                sec.EntryY + sec.Drop * (1f - Mathf.Cos(t * Mathf.Pi)) / 2f,

            SubSectionType.SteepChute =>
                // S-curve descent (steep)
                sec.EntryY + sec.Drop * (1f - Mathf.Cos(t * Mathf.Pi)) / 2f,

            SubSectionType.PowderField =>
                // Gentle undulation layered on the downhill slope
                sec.EntryY + sec.Drop * t - sec.Rise * Mathf.Sin(t * Mathf.Pi * 2f * sec.Periods),

            SubSectionType.ExitRamp =>
                // Asymmetric cosine ramp (same as TrackModule.Ramp)
                sec.EntryY - sec.Rise * (1f - Mathf.Cos(t * 0.85f * Mathf.Pi))
                    / (1f - Mathf.Cos(0.85f * Mathf.Pi)),

            _ => sec.EntryY
        };
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static float SmoothStep(float t)
    {
        return t * t * (3f - 2f * t);
    }

    private static float ComputeBlendWidth(float sectionLength)
    {
        return Mathf.Min(MaxBlendPx, sectionLength * MaxBlendFraction);
    }
}
