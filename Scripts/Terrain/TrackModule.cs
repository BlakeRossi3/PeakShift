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
///   Bump      — Single smooth roller (up then down) for varied terrain.
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
        Flat,
        Bump,  // Small roller: goes up then back down
        RollingHills  // Multiple rolling hills for hill-to-hill jumping
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

    // ── Selection weight ─────────────────────────────────────────

    /// <summary>
    /// Base selection weight. Higher = more likely to be chosen.
    /// The generator modifies this based on constraints.
    /// </summary>
    [Export]
    public float Weight { get; set; } = 1.0f;

    // ── Obstacle metadata ────────────────────────────────────────

    /// <summary>
    /// Obstacle density: 0 = no obstacles, 1 = maximum obstacles.
    /// Used by the terrain generator to determine how many obstacles to spawn.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.1")]
    public float ObstacleDensity { get; set; } = 0.0f;

    /// <summary>
    /// Types of obstacles allowed in this module.
    /// Options: "Rock", "Tree", "Log"
    /// Empty list = all types allowed (if ObstacleDensity > 0)
    /// </summary>
    [Export]
    public string[] AllowedObstacleTypes { get; set; } = System.Array.Empty<string>();

    // ── Derived helpers ────────────────────────────────────────────

    /// <summary>Number of hill periods for RollingHills shape (derived from Length).</summary>
    private float RollingHillPeriods => Mathf.Max(2f, Mathf.Floor(Length / 3000f));

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
            ModuleShape.Bump => entryY + Drop,  // Bump: net drop (or rise if negative)
            ModuleShape.RollingHills => entryY + Drop,
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

            // Asymmetric ramp: flat start → still climbing at lip for upward launch
            // Phase < 1.0 shifts peak steepness toward the lip (1.0 = original symmetric)
            ModuleShape.Ramp => entryY - Rise * (1f - Mathf.Cos(t * 0.85f * Mathf.Pi)) / (1f - Mathf.Cos(0.85f * Mathf.Pi)),

            // Gap: no surface (return entry Y as reference for fall detection)
            ModuleShape.Gap => entryY,

            // Transition: same S-curve as descent but shorter, blends terrain types
            ModuleShape.Transition => entryY + Drop * (1f - Mathf.Cos(t * Mathf.Pi)) / 2f,

            // Flat: gentle micro-bumps using a small sine wave
            ModuleShape.Flat => entryY + Drop * t + 8f * Mathf.Sin(t * Mathf.Pi * 4f),

            // Bump: single smooth roller (up then down) using sine wave
            // Rise = height of bump, Drop = net vertical change
            ModuleShape.Bump => entryY + Drop * t - Rise * Mathf.Sin(t * Mathf.Pi),

            // Rolling hills: multiple sine periods for hill-to-hill jumping
            // Crests give air, valleys give speed. Net downhill by Drop.
            ModuleShape.RollingHills => entryY + Drop * t - Rise * Mathf.Sin(t * Mathf.Pi * 2f * RollingHillPeriods),

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
