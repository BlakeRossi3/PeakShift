using Godot;

namespace PeakShift.Terrain;

/// <summary>
/// Defines a single modular track piece with entry/exit connectors and metadata.
/// Attach this as a Resource to define module types in the catalog.
///
/// Geometry types:
///   Descent   — Cosine S-curve (flat → steep → flat). Main downhill flow.
///   Ramp      — Cosine quarter-curve upward leading to a jump lip.
///   Gap       — Empty airborne section. Clear-or-die.
///   Transition— Short blending piece that switches terrain type mid-module.
///   Flat      — Brief flat/micro-bump section for pacing variety.
/// </summary>
[GlobalClass]
public partial class TrackModule : Resource
{
    // ── Module shape ─────────────────────────────────────────────

    public enum ModuleShape
    {
        Descent,
        Ramp,
        Gap,
        Transition,
        Flat
    }

    /// <summary>The geometric shape of this module.</summary>
    [Export]
    public ModuleShape Shape { get; set; } = ModuleShape.Descent;

    // ── Terrain type ─────────────────────────────────────────────

    /// <summary>Primary terrain type at module entry.</summary>
    [Export]
    public TerrainType EntryTerrain { get; set; } = TerrainType.Snow;

    /// <summary>Primary terrain type at module exit.</summary>
    [Export]
    public TerrainType ExitTerrain { get; set; } = TerrainType.Snow;

    /// <summary>True if this module transitions between two terrain types.</summary>
    public bool IsTransition => EntryTerrain != ExitTerrain;

    // ── Geometry parameters ──────────────────────────────────────

    /// <summary>Horizontal length of this module in pixels.</summary>
    [Export]
    public float Length { get; set; } = 1500f;

    /// <summary>Vertical drop (positive = downhill on screen) in pixels.</summary>
    [Export]
    public float Drop { get; set; } = 600f;

    /// <summary>For ramps: vertical rise (how high the lip goes) in pixels.</summary>
    [Export]
    public float Rise { get; set; } = 200f;

    // ── Difficulty ───────────────────────────────────────────────

    /// <summary>Difficulty rating 1-5. Generator uses this for difficulty curve filtering.</summary>
    [Export(PropertyHint.Range, "1,5,1")]
    public int Difficulty { get; set; } = 1;

    /// <summary>Minimum distance (px) before this module can appear.</summary>
    [Export]
    public float MinDistance { get; set; } = 0f;

    // ── Jump metadata ────────────────────────────────────────────

    /// <summary>If true, this module ends with a gap that must be cleared.</summary>
    [Export]
    public bool HasJump { get; set; }

    /// <summary>Width of the gap in pixels (only relevant if HasJump is true).</summary>
    [Export]
    public float GapWidth { get; set; } = 200f;

    /// <summary>
    /// Required clearance metric: minimum speed (px/s) needed to clear the gap.
    /// Used for difficulty display and validation. 0 = auto-calculated.
    /// </summary>
    [Export]
    public float RequiredClearanceSpeed { get; set; } = 0f;

    // ── Selection weight ─────────────────────────────────────────

    /// <summary>
    /// Base selection weight. Higher = more likely to be chosen.
    /// The generator modifies this based on constraints.
    /// </summary>
    [Export]
    public float Weight { get; set; } = 1.0f;

    // ── Connector transforms ─────────────────────────────────────
    // These are computed at spawn time based on placement context.
    // They define where this module begins and ends in world space.

    /// <summary>
    /// Computes the exit Y position given an entry Y and the module's geometry.
    /// </summary>
    public float ComputeExitY(float entryY)
    {
        return Shape switch
        {
            ModuleShape.Descent => entryY + Drop,
            ModuleShape.Ramp => entryY - Rise,
            ModuleShape.Gap => entryY,  // gaps are flat (lip to landing)
            ModuleShape.Transition => entryY + Drop,
            ModuleShape.Flat => entryY + Drop,  // Drop should be ~0 or small
            _ => entryY
        };
    }

    /// <summary>
    /// Returns the terrain surface Y at a normalized position t (0..1) within this module.
    /// Entry Y is the Y at t=0.
    /// </summary>
    public float SampleHeight(float t, float entryY)
    {
        t = Mathf.Clamp(t, 0f, 1f);

        return Shape switch
        {
            // Cosine S-curve: flat → steep → flat
            ModuleShape.Descent => entryY + Drop * (1f - Mathf.Cos(t * Mathf.Pi)) / 2f,

            // Cosine quarter-curve: flat at bottom → steep upward at lip
            ModuleShape.Ramp => entryY - Rise * (1f - Mathf.Cos(t * Mathf.Pi / 2f)),

            // Gap: no surface (return entry Y as reference for fall detection)
            ModuleShape.Gap => entryY,

            // Transition: same S-curve as descent but shorter, blends terrain types
            ModuleShape.Transition => entryY + Drop * (1f - Mathf.Cos(t * Mathf.Pi)) / 2f,

            // Flat: gentle micro-bumps using a small sine wave
            ModuleShape.Flat => entryY + Drop * t + 8f * Mathf.Sin(t * Mathf.Pi * 4f),

            _ => entryY
        };
    }

    /// <summary>
    /// Returns the terrain type at a normalized position t (0..1) within this module.
    /// For transition modules, blends at the midpoint.
    /// </summary>
    public TerrainType GetTerrainTypeAt(float t)
    {
        if (!IsTransition)
            return EntryTerrain;

        // Transition happens at the midpoint
        return t < 0.5f ? EntryTerrain : ExitTerrain;
    }
}
