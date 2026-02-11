using Godot;

namespace PeakShift;

/// <summary>
/// Tracks all run data: distance travelled, score, multiplier, and current terrain.
/// Attach as a child of GameManager or the main scene.
/// </summary>
public partial class RunManager : Node
{
    // ── Signals ──────────────────────────────────────────────────

    /// <summary>Emitted whenever the player's score changes.</summary>
    [Signal]
    public delegate void ScoreUpdatedEventHandler(int newScore);

    // ── Exports ──────────────────────────────────────────────────

    /// <summary>Base forward speed used to compute distance per second.</summary>
    [Export]
    public float BaseSpeed { get; set; } = 300f;

    // ── Properties ───────────────────────────────────────────────

    /// <summary>Total distance the player has travelled this run (in pixels).</summary>
    public float Distance { get; private set; }

    /// <summary>Current score for this run.</summary>
    public int Score { get; private set; }

    /// <summary>Score multiplier (e.g. perfect-swap streaks).</summary>
    public float Multiplier { get; set; } = 1.0f;

    /// <summary>The terrain type currently under the player.</summary>
    public TerrainType CurrentTerrain { get; set; } = TerrainType.Snow;

    // ── Public API ───────────────────────────────────────────────

    /// <summary>Reset all run data for a fresh run.</summary>
    public void ResetRun()
    {
        Distance = 0f;
        Score = 0;
        Multiplier = 1.0f;
        CurrentTerrain = TerrainType.Snow;
        EmitSignal(SignalName.ScoreUpdated, Score);
        GD.Print("[RunManager] Run reset");
    }

    /// <summary>
    /// Add points to the score, scaled by the current multiplier.
    /// </summary>
    /// <param name="points">Base points before multiplier.</param>
    public void AddScore(int points)
    {
        int gained = Mathf.RoundToInt(points * Multiplier);
        Score += gained;
        EmitSignal(SignalName.ScoreUpdated, Score);
    }

    /// <summary>
    /// Increment distance based on elapsed delta and speed.
    /// Call this every physics frame.
    /// </summary>
    /// <param name="delta">Frame delta time in seconds.</param>
    public void UpdateDistance(float delta)
    {
        Distance += delta * BaseSpeed;
    }
}
