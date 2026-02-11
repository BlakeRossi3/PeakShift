namespace PeakShift;

/// <summary>
/// Static class containing string constants for all signal names.
/// This is the SHARED CONTRACT that all agents and systems reference.
/// </summary>
public static class SignalBus
{
    // ── Vehicle signals ──────────────────────────────────────────

    /// <summary>Emitted when the player swaps between bike and ski.</summary>
    public const string VehicleSwapped = "vehicle_swapped";

    /// <summary>Emitted when a swap occurs on the optimal terrain for the new vehicle.</summary>
    public const string PerfectSwap = "perfect_swap";

    /// <summary>Emitted when the player crashes into an obstacle.</summary>
    public const string PlayerCrashed = "player_crashed";

    /// <summary>Emitted when the player performs a trick (jump, spin, etc.).</summary>
    public const string TrickPerformed = "trick_performed";

    // ── Terrain signals ──────────────────────────────────────────

    /// <summary>Emitted when the terrain type changes under the player.</summary>
    public const string TerrainChanged = "terrain_changed";

    /// <summary>Emitted when the player enters any new terrain chunk.</summary>
    public const string ChunkEntered = "chunk_entered";

    // ── Game state signals ───────────────────────────────────────

    /// <summary>Emitted when a new run begins.</summary>
    public const string GameStarted = "game_started";

    /// <summary>Emitted when the run ends (crash, quit, etc.).</summary>
    public const string GameOver = "game_over";

    /// <summary>Emitted whenever the player's score changes.</summary>
    public const string ScoreUpdated = "score_updated";

    // ── Biome signals (emitted by PC2, name defined here) ────────

    /// <summary>Emitted when the visual biome transitions (e.g. alpine to forest).</summary>
    public const string BiomeTransition = "biome_transition";

    // ── Hazard signals (emitted by PC2, name defined here) ───────

    /// <summary>Emitted to warn the player of an incoming hazard.</summary>
    public const string HazardWarning = "hazard_warning";

    /// <summary>Emitted when a hazard becomes active and dangerous.</summary>
    public const string HazardActive = "hazard_active";

    /// <summary>Emitted when a hazard has been cleared or passed.</summary>
    public const string HazardCleared = "hazard_cleared";
}
